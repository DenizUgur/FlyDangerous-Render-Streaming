using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

using NativeWebSocket;

[System.Serializable]
public class RendererAttributes
{
    public long unixTime;
    public int batches;
    public int triangles;
    public int vertices;
    public int shadowCasters;
    public int renderTextureChanges;
    public float frameTime;
    public float renderTime;
    public int renderTextureCount;
    public int renderTextureBytes;
    public int usedTextureMemorySize;
    public int usedTextureCount;
    public string screenRes;
    public int screenBytes;
    public int vboTotal;
    public int vboTotalBytes;
    public int vboUploads;
    public int vboUploadBytes;
    public int ibUploads;
    public int ibUploadBytes;
    public int visibleSkinnedMeshes;
    public int drawCalls;
}

[System.Serializable]
public class RendererModifications
{
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
    private string proxyServer = "ws://localhost:80/proxy/renderer";

    [SerializeField, Tooltip("Send interval (sec)."), Range(2, 10)]
    private int sendInterval = 2;

    [SerializeField, Tooltip("Update interval (sec)."), Range(2, 10)]
    private int updateInterval = 2;

    WebSocket websocket;
    RendererAttributes rendererAttributes;
    RendererModifications rendererModifications;

    void Awake()
    {
        rendererAttributes = new RendererAttributes();
        rendererModifications = new RendererModifications();
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

        // Update renderer attributes at regular intervals.
        InvokeRepeating("SyncAttributes", 0.0f, updateInterval);

        // Keep sending attributes at regular intervals.
        InvokeRepeating("SendRendererAttributes", updateInterval, sendInterval);

        // waiting for messages
        await websocket.Connect();
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket.DispatchMessageQueue();
#endif
    }

    void SyncAttributes()
    {
        if (websocket.State != WebSocketState.Open)
            return;

        // Get time
        DateTime now = DateTime.Now;
        long unixTime = ((DateTimeOffset)now).ToUnixTimeSeconds();

        // Read
        {
            rendererAttributes.unixTime = unixTime;
            rendererAttributes.batches = UnityEditor.UnityStats.batches;
            rendererAttributes.triangles = UnityEditor.UnityStats.triangles;
            rendererAttributes.vertices = UnityEditor.UnityStats.vertices;
            rendererAttributes.shadowCasters = UnityEditor.UnityStats.shadowCasters;
            rendererAttributes.renderTextureChanges = UnityEditor.UnityStats.renderTextureChanges;
            rendererAttributes.frameTime = UnityEditor.UnityStats.frameTime;
            rendererAttributes.renderTime = UnityEditor.UnityStats.renderTime;
            rendererAttributes.renderTextureCount = UnityEditor.UnityStats.renderTextureCount;
            rendererAttributes.renderTextureBytes = UnityEditor.UnityStats.renderTextureBytes;
            rendererAttributes.usedTextureMemorySize = UnityEditor.UnityStats.usedTextureMemorySize;
            rendererAttributes.usedTextureCount = UnityEditor.UnityStats.usedTextureCount;
            rendererAttributes.screenRes = UnityEditor.UnityStats.screenRes;
            rendererAttributes.screenBytes = UnityEditor.UnityStats.screenBytes;
            rendererAttributes.vboTotal = UnityEditor.UnityStats.vboTotal;
            rendererAttributes.vboTotalBytes = UnityEditor.UnityStats.vboTotalBytes;
            rendererAttributes.vboUploads = UnityEditor.UnityStats.vboUploads;
            rendererAttributes.vboUploadBytes = UnityEditor.UnityStats.vboUploadBytes;
            rendererAttributes.ibUploads = UnityEditor.UnityStats.ibUploads;
            rendererAttributes.ibUploadBytes = UnityEditor.UnityStats.ibUploadBytes;
            rendererAttributes.visibleSkinnedMeshes = UnityEditor.UnityStats.visibleSkinnedMeshes;
            rendererAttributes.drawCalls = UnityEditor.UnityStats.drawCalls;
        }

        // Write
        {
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

    async void SendRendererAttributes()
    {
        if (websocket.State != WebSocketState.Open)
            return;

        string message = JsonUtility.ToJson(rendererAttributes);
        await websocket.SendText(message);
    }

    void ReceiveRendererModifications(byte[] data)
    {
        if (websocket.State != WebSocketState.Open)
            return;

        string message = System.Text.Encoding.UTF8.GetString(data);
        rendererModifications = JsonUtility.FromJson<RendererModifications>(message);
    }

    private async void OnApplicationQuit()
    {
        await websocket.Close();
    }

}