using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class RpiTcpClient : MonoBehaviour
{
    [Header("Raspberry Pi Settings")]
    public string serverIp = "172.20.10.4"; 
    public int serverPort = 5005;

    [Header("Animation Settings")]
    public Animator dogAnimator;
    public string breathParameterName = "BreathBlend"; // 對應 Animator 的參數名
    public float smoothSpeed = 2.0f; // 數值越大，切換越快

    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private bool isConnected = false;

    // 用於主執行緒讀取的目標值
    private float targetBreathVal = 0.0f; // 0 = 吐氣, 1 = 吸氣
    private float currentBreathVal = 0.0f;

    void Start() {
        ConnectToServer();
    }

    void Update() {
        // [關鍵] 在 Update 中平滑插值，讓動畫不生硬
        currentBreathVal = Mathf.Lerp(currentBreathVal, targetBreathVal, Time.deltaTime * smoothSpeed);
        
        if(dogAnimator != null) {
            dogAnimator.SetFloat(breathParameterName, currentBreathVal);
        }
    }

    // ... (ConnectToServer, SendCommand, OnApplicationQuit 保持不變) ...

    private void ReceiveLoop()
    {
        byte[] buffer = new byte[1024];
        try
        {
            while (isConnected)
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0) break;

                string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                string[] lines = msg.Split('\n'); // 處理多行指令

                foreach (string line in lines) {
                    string cleanLine = line.Trim();
                    if (string.IsNullOrEmpty(cleanLine)) continue;

                    Debug.Log("[CLIENT] Received: " + cleanLine);

                    // [關鍵] 解析指令
                    if (cleanLine == "ANIM:INHALE") {
                        targetBreathVal = 1.0f; // 設定目標為吸氣
                    }
                    else if (cleanLine == "ANIM:EXHALE") {
                        targetBreathVal = 0.0f; // 設定目標為吐氣
                    }
                }
            }
        }
        catch (Exception e) {
            Debug.LogError("[CLIENT] Receive error: " + e.Message);
        }
        finally {
            isConnected = false;
        }
    }
}