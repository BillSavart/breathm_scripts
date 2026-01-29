using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class RpiTcpClient : MonoBehaviour
{
    [Header("Raspberry Pi Settings")]
    public string serverIp = "192.168.50.251"; // 確認這跟 Server 端 IP 一樣
    public int serverPort = 5005;

    [Header("Animation Settings")]
    public Animator dogAnimator;
    public string breathParameterName = "BreathBlend"; // 確認 Animator 參數名稱
    public float smoothSpeed = 5.0f; // 數值越大，反應越快

    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private bool isConnected = false;
    private bool shouldReconnect = false;

    // 動畫目標值：0 = 吐氣 (Exhale), 1 = 吸氣 (Inhale)
    private float targetBreathVal = 0.0f; 
    private float currentBreathVal = 0.0f;

    void Start()
    {
        ConnectToServer();
    }

    void Update()
    {
        // 按 'A' -> 告訴 RPi 執行邏輯
        if (Input.GetKeyDown(KeyCode.A))
        {
            Debug.Log("[INPUT] Starting Logic (A)");
            SendCommand("RUN:FIX\n");
        }

        // 按 'X' -> 停止
        if (Input.GetKeyDown(KeyCode.X))
        {
            Debug.Log("[INPUT] Stopping (X)");
            SendCommand("STOP\n");
            targetBreathVal = 0.0f; // 歸零
        }

        // 動畫平滑過渡
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
        }
        catch (Exception e)
        {
            Debug.LogError("Send Error: " + e.Message);
            isConnected = false;
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
                        // 處理 RPi 傳來的訊號
                        if (cleanLine.Contains("ANIM:INHALE"))
                        {
                            targetBreathVal = 1.0f; // 推桿伸出 -> 吸氣動畫
                        }
                        else if (cleanLine.Contains("ANIM:EXHALE"))
                        {
                            targetBreathVal = 0.0f; // 推桿縮回 -> 吐氣動畫
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            isConnected = false;
        }
    }

    private void CloseConnection()
    {
        isConnected = false;
        if (receiveThread != null) receiveThread.Abort();
        if (client != null) client.Close();
    }
}