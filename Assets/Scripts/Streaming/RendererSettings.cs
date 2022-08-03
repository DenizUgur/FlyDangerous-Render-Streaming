using System;
using System.Threading;
using System.Reflection;
using System.Collections;
using System.Threading.Tasks;
using System.Collections.Generic;

using UnityEngine;
using Unity.Profiling;

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

[System.Serializable]
public class RendererAttributes
{
    public long unixTime;
    public double DrawCalls;
    public double VerticesCount;
    // add more attributes here. All should be double.
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
    private RendererAttributes rendererAttributes;
    private RendererModifications rendererModifications;
    private static Mutex writeLock = new Mutex();

    // Coroutines
    private IEnumerator coroutineSync;
    private IEnumerator coroutineSend;

    // Profiler
    private List<ProfilerInstance> profilerInstances;

    void Awake()
    {
        rendererAttributes = new RendererAttributes();
        rendererModifications = new RendererModifications();

        coroutineSync = SyncAttributes();
        coroutineSend = SendRendererAttributes();
    }

    void OnEnable()
    {
        profilerInstances = new List<ProfilerInstance>(){
            new ProfilerInstance("Render", "Draw Calls Count", "DrawCalls"),
            new ProfilerInstance("Render", "Vertices Count", "VerticesCount"),
            // add more profilers here
        };
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
                {
                    FieldInfo field = rendererAttributes.GetType().GetField(recorder.ShortName);
                    field.SetValue(rendererAttributes, recorder.Get());
                }
                rendererAttributes.unixTime = unixTime;

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

                string message = JsonUtility.ToJson(rendererAttributes);
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

}
