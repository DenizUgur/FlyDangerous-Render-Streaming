using System;
using System.Threading;
using System.Reflection;
using System.Collections;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.IO;
using System.Runtime.Serialization.Json;

using UnityEngine;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;

using NativeWebSocket;

struct ProfilerInstance
{
    string Category;
    string Name;
    public string ShortName;
    ProfilerRecorder Recorder;

    public ProfilerInstance(string category, string name, string shortName)
    {
        Category = category;
        Name = name;
        ShortName = shortName;
        Recorder = new ProfilerRecorder(ProfilerInstance.GetCategory(Category), Name);
    }

    public void Start()
    {
        Recorder.Start();
    }

    public void Dispose()
    {
        Recorder.Dispose();
    }

    public double Get()
    {
        string val = Recorder.LastValue.ToString();
        if (val == "true" || val == "false")
            return val == "true" ? 1 : 0;
        return Convert.ToDouble(val);
    }

    private static ProfilerCategory GetCategory(string propName)
    {
        ProfilerCategory pc = new ProfilerCategory();
        return (ProfilerCategory)pc.GetType().GetProperty(propName).GetValue(pc, null);
    }

}

[System.Serializable]
public class RendererModifications
{
    public bool instantiated;
    public int unixTimestamp;
    public int antiAliasing;
    public float lodBias;
    public int masterTextureLimit;
    public int pixelLightCount;
    public bool realtimeReflectionProbes;
    public int shadowCascades;
    public float shadowDistance;
    public bool softParticles;
    public int vSyncCount;
    public int targetFrameRate;
}


public class RendererSettings : MonoBehaviour
{
    [SerializeField, Tooltip("Proxy server url.")]
    private string proxyServer = "ws://localhost:8080/proxy/renderer";

    [SerializeField, Tooltip("Send interval (sec)."), Range(.1f, 4)]
    private float sendInterval = .5f;

    [SerializeField, Tooltip("Update interval (sec)."), Range(.1f, 4)]
    private float updateInterval = .5f;

    private WebSocket websocket;
    private Dictionary<string, double> rendererAttributes;
    private RendererModifications rendererModifications;
    private static Mutex writeLock = new Mutex();

    // Coroutines
    private IEnumerator coroutineSync;
    private IEnumerator coroutineSend;

    // Profiler
    private List<ProfilerInstance> profilerInstances;

    void Awake()
    {
        rendererAttributes = new Dictionary<string, double>();
        rendererModifications = new RendererModifications();

        coroutineSync = SyncAttributes();
        coroutineSend = SendRendererAttributes();
    }

    unsafe void OnEnable()
    {
        var availableStatHandles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(availableStatHandles);

        var validCategories = new List<String>() { "Ai", "Animation", "Audio", "Gui", "Internal", "Lighting", "Loading", "Memory", "Network", "Particles", "Physics", "Render", "Scripts", "Video", "Virtual Texturing", "Vr" };

        string pattern = @"['`)\[|(>:\] /\-\\_<!.']";
        Regex regex = new Regex(pattern);

        profilerInstances = new List<ProfilerInstance>();
        int total = 0;
        int failed = 0;
        foreach (var h in availableStatHandles)
        {
            var statDesc = ProfilerRecorderHandle.GetDescription(h);
            if (!validCategories.Contains(statDesc.Category.ToString()))
                continue;

            try
            {
                string shortName = regex.Replace(statDesc.Name, "");
                ProfilerInstance pi = new ProfilerInstance(statDesc.Category.ToString(), statDesc.Name, shortName);
                profilerInstances.Add(pi);
                rendererAttributes.Add(shortName, -1);
            }
            catch { failed++; };
            total++;
        }
        Debug.Log($"Found {total} stats, {failed} failed");
    }

    // Start is called before the first frame update
    async void Start()
    {
        websocket = new WebSocket(proxyServer);

        websocket.OnOpen += () =>
        {
            Debug.Log("Connection to proxy open!");
        };

        websocket.OnMessage += (bytes) =>
        {
            Task t = new Task(() => ReceiveRendererModifications(bytes));
            t.Start();
        };

        // Start profilers
        foreach (var recorder in profilerInstances)
            recorder.Start();

        // Update renderer attributes at regular intervals.
        StartCoroutine(coroutineSync);

        // Keep sending attributes at regular intervals.
        StartCoroutine(coroutineSend);

        // waiting for messages
        await websocket.Connect();
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket.DispatchMessageQueue();
#endif
    }

    IEnumerator SyncAttributes()
    {
        while (true)
        {
            yield return new WaitForSeconds(updateInterval);

            if (websocket.State != WebSocketState.Open)
                continue;

            // Get time
            DateTime now = DateTime.Now;
            long unixTime = ((DateTimeOffset)now).ToUnixTimeMilliseconds();

            // Read
            {
                writeLock.WaitOne();

                foreach (var recorder in profilerInstances)
                    rendererAttributes[recorder.ShortName] = recorder.Get();

                writeLock.ReleaseMutex();
            }

            // Write
            {
                if (!rendererModifications.instantiated)
                    continue;

                QualitySettings.antiAliasing = rendererModifications.antiAliasing;
                QualitySettings.lodBias = rendererModifications.lodBias;
                QualitySettings.masterTextureLimit = rendererModifications.masterTextureLimit;
                QualitySettings.pixelLightCount = rendererModifications.pixelLightCount;
                QualitySettings.realtimeReflectionProbes = rendererModifications.realtimeReflectionProbes;
                QualitySettings.shadowCascades = rendererModifications.shadowCascades;
                QualitySettings.shadowDistance = rendererModifications.shadowDistance;
                QualitySettings.softParticles = rendererModifications.softParticles;
                QualitySettings.vSyncCount = rendererModifications.vSyncCount;
                Application.targetFrameRate = rendererModifications.targetFrameRate;
            }
        }
    }

    IEnumerator SendRendererAttributes()
    {
        while (true)
        {
            yield return new WaitForSeconds(sendInterval);

            if (websocket.State != WebSocketState.Open)
                continue;

            {
                writeLock.WaitOne();

                string message = DataToJson(rendererAttributes);
                websocket.SendText(message);

                writeLock.ReleaseMutex();
            }
        }
    }

    void ReceiveRendererModifications(byte[] data)
    {
        string message = System.Text.Encoding.UTF8.GetString(data);
        rendererModifications = JsonUtility.FromJson<RendererModifications>(message);
        rendererModifications.instantiated = true;
    }

    void OnDisable()
    {
        foreach (var recorder in profilerInstances)
            recorder.Dispose();
    }

    private async void OnApplicationQuit()
    {
        await websocket.Close();
    }

    private static string DataToJson(Dictionary<string, double> data)
    {
        MemoryStream stream = new MemoryStream();

        DataContractJsonSerializer serialiser = new DataContractJsonSerializer(
            data.GetType(),
            new DataContractJsonSerializerSettings()
            {
                UseSimpleDictionaryFormat = true
            });

        serialiser.WriteObject(stream, data);

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
