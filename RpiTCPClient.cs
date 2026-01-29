using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class RpiTcpClient : MonoBehaviour
{
    [Header("Raspberry Pi Settings")]
    public string serverIp = "172.20.10.4"; // 請確認這是樹莓派的真實 IP
    public int serverPort = 5005;

    [Header("Animation Settings")]
    // 請將狗狗的 Animator 元件拖曳到這裡
    public Animator dogAnimator; 
    
    // Unity Animator Controller 裡面的 Parameter 名稱
    public string breathParameterName = "BreathBlend"; 
    
    // 數值變化的平滑速度，越大變化越快
    public float smoothSpeed = 2.0f; 

    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private bool isConnected = false;

    // 這些變數用來在不同執行緒間傳遞數據
    private float targetBreathVal = 0.0f; // 目標值 (0=吐氣, 1=吸氣)
    private float currentBreathVal = 0.0f; // 當前值 (用於平滑過渡)

    void Start()
    {
        ConnectToServer();
    }

    void Update()
    {
        // 每一幀都執行：讓目前的數值慢慢移動到目標數值 (插值)
        // 這樣即使網路訊號是瞬間跳變的，動畫也會看起來很滑順
        currentBreathVal = Mathf.Lerp(currentBreathVal, targetBreathVal, Time.deltaTime * smoothSpeed);

        // 將計算後的數值設定給 Animator
        if (dogAnimator != null)
        {
            dogAnimator.SetFloat(breathParameterName, currentBreathVal);
        }
    }

    void OnApplicationQuit()
    {
        CloseConnection();
    }

    // --- UI 按鈕呼叫功能 ---
    public void OnActivateButtonClicked()
    {
        SendCommand("ACTIVATE\n");
    }

    public void OnDeactivateButtonClicked()
    {
        SendCommand("DEACTIVATE\n");
    }

    // --- 連線功能 ---
    public void ConnectToServer()
    {
        try
        {
            // 如果已經連線就不重複連
            if (isConnected) return;

            client = new TcpClient();
            // 嘗試連線到樹莓派
            client.Connect(serverIp, serverPort);
            stream = client.GetStream();
            isConnected = true;
            Debug.Log("[CLIENT] Connected to Raspberry Pi");

            // 啟動一個背景執行緒專門負責接收資料，避免卡住畫面
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
            stream.Flush(); // 確保資料立刻送出
            Debug.Log("[CLIENT] Sent: " + cmd.Trim());
        }
        catch (Exception e)
        {
            Debug.LogError("[CLIENT] Send error: " + e.Message);
        }
    }

    // --- 接收迴圈 (在背景執行) ---
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
                    if (bytesRead <= 0)
                    {
                        Debug.Log("[CLIENT] Server disconnected");
                        break;
                    }

                    string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    
                    // 處理可能黏在一起的多行指令 (例如 "ANIM:INHALE\nANIM:EXHALE\n")
                    string[] lines = msg.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string line in lines)
                    {
                        string cleanLine = line.Trim();
                        if (string.IsNullOrEmpty(cleanLine)) continue;

                        Debug.Log("[CLIENT] Received: " + cleanLine);

                        // 解析指令並更新目標值
                        if (cleanLine == "ANIM:INHALE")
                        {
                            targetBreathVal = 1.0f; // 設定目標為吸氣
                        }
                        else if (cleanLine == "ANIM:EXHALE")
                        {
                            targetBreathVal = 0.0f; // 設定目標為吐氣
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            // 當我們主動斷線時，這邊會出現錯誤是正常的，不用太擔心
            if(isConnected)
                Debug.LogError("[CLIENT] Receive error: " + e.Message);
        }
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
            
            // 強制中止接收執行緒
            if (receiveThread != null && receiveThread.IsAlive)
                receiveThread.Abort();
            
            if (stream != null) stream.Close();
            if (client != null) client.Close();
        }
        catch (Exception) { }
    }
}