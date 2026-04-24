using System;
using System.Collections;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

public class RpiTcpClient : MonoBehaviour
{
    public static RpiTcpClient Instance { get; private set; } = null;

    [Header("Raspberry Pi Settings")]
    public string serverIp = "192.168.50.251";
    public int serverPort = 5005;
    [SerializeField] private float connectTimeoutSeconds = 3f;

    [Header("Animation Control")]
    public Animator targetAnimator;
    public string progressParameterName = "BreathProgress";
    public string progressSpeedParameterName = "BreathSpeed";
    public UnityEvent onActivate;
    public UnityEvent onDisconnect;

    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private readonly object streamLock = new object();
    private readonly object progressLock = new object();

    private static volatile bool connected = false;
    private volatile bool connecting = false;
    private volatile bool closing = false;
    private int connectionAttemptId = 0;

    public static bool isConnected { get { return connected; } }
    public bool IsConnecting { get { return connecting; } }
    public string LastStatusMessage { get; private set; } = "Disconnected";

    private float targetProgress = 0f;
    private float smoothProgress = 0f;
    private float animSpeed = 0.3f;
    private float lastTime = 0f;
    private float breathDeltaTime = 2f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Debug.LogWarning("【警告】Rpi_Client 重複");
            Destroy(this);
        }
    }

    private void Start()
    {
        if (targetAnimator == null)
        {
            Debug.LogError("【嚴重錯誤】Target Animator 尚未指派！請在 Inspector 中將你的狗狗模型拖入 Target Animator 欄位。");
        }
    }

    private void Update()
    {
        if (targetAnimator == null)
        {
            return;
        }

        float latestTarget;
        lock (progressLock)
        {
            latestTarget = targetProgress;
        }

        smoothProgress = Mathf.Lerp(smoothProgress, latestTarget, Time.deltaTime * 5f);
        targetAnimator.SetFloat(progressParameterName, smoothProgress);

        if (smoothProgress <= 0.3f || smoothProgress >= 0.7f)
        {
            breathDeltaTime = Time.time - lastTime;
            lastTime = Time.time;
            if (breathDeltaTime > 0f)
            {
                animSpeed = 1f / breathDeltaTime;
                // targetAnimator.SetFloat(progressSpeedParameterName, animSpeed);
            }
        }
    }

#if UNITY_ANDROID
    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            Debug.Log("[AUTO] App paused, stopping Raspberry Pi device...");
            CloseConnection(true);
        }
    }
#endif

    private void OnApplicationQuit()
    {
        Debug.Log("[AUTO] App quitting, stopping Raspberry Pi device...");
        CloseConnection(true);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            CloseConnection(true);
            Instance = null;
        }
    }

    public void ConnectToServer()
    {
        if (connected || connecting)
        {
            return;
        }

        StartCoroutine(ConnectCoroutine());
    }

    private IEnumerator ConnectCoroutine()
    {
        int attemptId = ++connectionAttemptId;
        connecting = true;
        closing = false;
        LastStatusMessage = "Connecting to Raspberry Pi...";

        TcpClient newClient = new TcpClient();
        Task connectTask = newClient.ConnectAsync(serverIp, serverPort);
        float deadline = Time.realtimeSinceStartup + connectTimeoutSeconds;

        while (!connectTask.IsCompleted && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        if (!connectTask.IsCompleted)
        {
            newClient.Close();
            if (attemptId == connectionAttemptId)
            {
                connecting = false;
            }
            LastStatusMessage = "Connection timed out";
            Debug.LogError($"[CLIENT] Connection timed out: {serverIp}:{serverPort}");
            yield break;
        }

        if (connectTask.IsFaulted)
        {
            newClient.Close();
            if (attemptId == connectionAttemptId)
            {
                connecting = false;
            }
            string error = connectTask.Exception != null && connectTask.Exception.InnerException != null
                ? connectTask.Exception.InnerException.Message
                : "Unknown connection error";
            LastStatusMessage = "Connection failed: " + error;
            Debug.LogError("[CLIENT] Connection error: " + error);
            yield break;
        }

        if (attemptId != connectionAttemptId || closing || !connecting)
        {
            newClient.Close();
            yield break;
        }

        lock (streamLock)
        {
            client = newClient;
            stream = client.GetStream();
            connected = true;
        }

        receiveThread = new Thread(ReceiveLoop);
        receiveThread.IsBackground = true;
        receiveThread.Start();

        connecting = false;
        LastStatusMessage = "Connected to Raspberry Pi";
        Debug.Log("[CLIENT] Connected to Raspberry Pi");
        Debug.Log("[AUTO] 連線成功，已自動發送 ACTIVATE 指令啟動設備...");
        SendCommand("ACTIVATE\n");
        onActivate?.Invoke();
    }

    public void SendCommand(string cmd)
    {
        NetworkStream currentStream;
        lock (streamLock)
        {
            if (!connected || stream == null)
            {
                return;
            }

            currentStream = stream;
        }

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(cmd);
            currentStream.Write(data, 0, data.Length);
            currentStream.Flush();
            Debug.Log("[CLIENT] Sent: " + cmd.Trim());
        }
        catch (Exception e)
        {
            LastStatusMessage = "Send failed: " + e.Message;
            Debug.LogError("[CLIENT] Send error: " + e.Message);
            CloseConnection(false);
        }
    }

    private void ReceiveLoop()
    {
        byte[] buffer = new byte[1024];
        StringBuilder lineBuffer = new StringBuilder();

        try
        {
            while (connected && !closing)
            {
                NetworkStream currentStream;
                lock (streamLock)
                {
                    currentStream = stream;
                }

                if (currentStream == null)
                {
                    break;
                }

                int bytesRead = currentStream.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0)
                {
                    Debug.Log("[CLIENT] Server disconnected");
                    break;
                }

                string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                AppendAndProcessLines(lineBuffer, chunk);
            }
        }
        catch (Exception e)
        {
            if (!closing)
            {
                LastStatusMessage = "Receive failed: " + e.Message;
                Debug.LogError("[CLIENT] Receive error: " + e.Message);
            }
        }
        finally
        {
            connected = false;
            LastStatusMessage = closing ? "Disconnected" : "Server disconnected";
            CloseSocketResources();
        }
    }

    private void AppendAndProcessLines(StringBuilder lineBuffer, string chunk)
    {
        foreach (char c in chunk)
        {
            if (c == '\n')
            {
                ProcessLine(lineBuffer.ToString().Trim());
                lineBuffer.Length = 0;
            }
            else if (c != '\r')
            {
                lineBuffer.Append(c);
            }
        }
    }

    private void ProcessLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        if (line.StartsWith("SYNC_PROGRESS:", StringComparison.Ordinal))
        {
            string valueStr = line.Substring("SYNC_PROGRESS:".Length);
            if (float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float p))
            {
                if (p < 0f)
                    p = 0f;
                else if (p > 1f)
                    p = 1f;

                lock (progressLock)
                {
                    targetProgress = p;
                }
            }
            return;
        }

        Debug.Log("[CLIENT] Server: " + line);
    }

    public void CloseConnection()
    {
        CloseConnection(true);
    }

    public void CloseConnection(bool sendDeactivate)
    {
        if (!connected && !connecting)
        {
            return;
        }

        closing = true;
        connectionAttemptId++;

        if (sendDeactivate && connected)
        {
            SendCommand("DEACTIVATE\n");
        }

        connected = false;
        connecting = false;

        CloseSocketResources();

        if (receiveThread != null && receiveThread.IsAlive && Thread.CurrentThread != receiveThread)
        {
            receiveThread.Join(500);
        }

        receiveThread = null;
        LastStatusMessage = "Disconnected";
        onDisconnect?.Invoke();
    }

    private void CloseSocketResources()
    {
        lock (streamLock)
        {
            if (stream != null)
            {
                stream.Close();
                stream = null;
            }

            if (client != null)
            {
                client.Close();
                client = null;
            }
        }
    }
}
