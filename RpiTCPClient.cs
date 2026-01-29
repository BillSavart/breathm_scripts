using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class RpiTcpClient : MonoBehaviour
{
    [Header("Raspberry Pi Settings")]
    public string serverIp = "192.168.50.251"; // 確認 IP
    public int serverPort = 5005;

    [Header("Animation Settings")]
    public Animator dogAnimator;
    public string breathParameterName = "BreathBlend";
    public float smoothSpeed = 2.0f;

    [Header("Motion Time Settings")]
    [Range(0f, 1f)]
    public float inhaleValue = 0.5f; 
    
    [Range(0f, 1f)]
    public float exhaleValue = 0.0f; 

    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private bool isConnected = false;

    private float targetBreathVal = 0.0f;
    private float currentBreathVal = 0.0f;

    void Start()
    {
        ConnectToServer();
    }

    void Update()
    {
        // --- [修改] 多模式按鍵控制 ---
        
        // 按 'A' -> 執行 Fix Version
        if (Input.GetKeyDown(KeyCode.A))
        {
            Debug.Log("[INPUT] Key 'A' pressed. Running FIX version.");
            SendCommand("RUN:FIX\n");
        }

        // 按 'S' -> 執行 Demo Version (一般引導)
        if (Input.GetKeyDown(KeyCode.S))
        {
            Debug.Log("[INPUT] Key 'S' pressed. Running DEMO version.");
            SendCommand("RUN:DEMO\n");
        }

        // 按 'D' -> 執行 Demo with Mirror (鏡像+引導)
        if (Input.GetKeyDown(KeyCode.D))
        {
            Debug.Log("[INPUT] Key 'D' pressed. Running MIRROR version.");
            SendCommand("RUN:MIRROR\n");
        }

        // 按 'X' -> 全部停止
        if (Input.GetKeyDown(KeyCode.X))
        {
            Debug.Log("[INPUT] Key 'X' pressed. STOPPING.");
            SendCommand("STOP\n");
        }
        // ---------------------------

        // 動畫平滑運算
        currentBreathVal = Mathf.Lerp(currentBreathVal, targetBreathVal, Time.deltaTime * smoothSpeed);

        if (dogAnimator != null)
        {
            dogAnimator.SetFloat(breathParameterName, currentBreathVal);
        }
    }

    void OnApplicationQuit()
    {
        CloseConnection();
    }

    public void ConnectToServer()
    {
        try
        {
            if (isConnected) return;
            client = new TcpClient();
            client.Connect(serverIp, serverPort);
            stream = client.GetStream();
            isConnected = true;
            Debug.Log("[CLIENT] Connected to Raspberry Pi");

            receiveThread = new Thread(ReceiveLoop);
            receiveThread.IsBackground = true;
            receiveThread.Start();
        }
        catch (Exception e)
        {
            Debug.LogError("[CLIENT] Connection error: " + e.Message);
        }
    }

    public void SendCommand(string cmd)
    {
        if (!isConnected || stream == null) return;

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(cmd);
            stream.Write(data, 0, data.Length);
            stream.Flush();
        }
        catch (Exception e)
        {
            Debug.LogError("[CLIENT] Send error: " + e.Message);
        }
    }

    private void ReceiveLoop()
    {
        byte[] buffer = new byte[1024];
        try
        {
            while (isConnected)
            {
                if (stream.CanRead)
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead <= 0) break;

                    string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    string[] lines = msg.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string line in lines)
                    {
                        string cleanLine = line.Trim();
                        if (string.IsNullOrEmpty(cleanLine)) continue;
                        
                        if (cleanLine == "ANIM:INHALE")
                        {
                            targetBreathVal = inhaleValue;
                        }
                        else if (cleanLine == "ANIM:EXHALE")
                        {
                            targetBreathVal = exhaleValue;
                        }
                    }
                }
            }
        }
        catch (Exception) { }
        finally
        {
            isConnected = false;
        }
    }

    private void CloseConnection()
    {
        try
        {
            isConnected = false;
            if (receiveThread != null) receiveThread.Abort();
            if (stream != null) stream.Close();
            if (client != null) client.Close();
        }
        catch (Exception) { }
    }
}