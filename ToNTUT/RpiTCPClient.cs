using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Oculus.VR;

public class RpiTcpClient : MonoBehaviour
{
    [Header("Raspberry Pi Settings")]
    public string serverIp = "172.20.10.4"; 
    public int serverPort = 5005;

    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private bool isConnected = false;
    private bool isActivated = false;

    void Start()
    {
        /// 初始化時調用，負責啟動 TCP 客戶端連接。
        /// 行為:
        /// - 調用 ConnectToServer() 建立與 Raspberry Pi 的 TCP 連接。
        /// - 如果連接成功，設置 isConnected 為 true，並啟動接收線程。
        /// - 記錄連接狀態到 Unity Debug Log。
        ConnectToServer();
    }

    void Update()
    {
        /// 每幀調用，檢查 Oculus 控制器輸入以切換激活狀態。
        /// 行為:
        /// - 使用 OVRInput.GetDown(OVRInput.Button.Two) 檢測右手控制器 B 鍵按下。
        /// - 如果按下，檢查 isActivated 狀態：
        ///   - 如果已激活，調用 OnDeactivateButtonClicked() 並設置 isActivated 為 false。
        ///   - 如果未激活，調用 OnActivateButtonClicked() 並設置 isActivated 為 true。
        /// - 實現按鍵切換功能，避免重複發送相同命令。
        if (OVRInput.GetDown(OVRInput.Button.Two))
        {
            if (isActivated)
            {
                OnDeactivateButtonClicked();
                isActivated = false;
            }
            else
            {
                OnActivateButtonClicked();
                isActivated = true;
            }
        }
    }

    void OnApplicationQuit()
    {
        /// 應用退出時調用，負責清理 TCP 連接。
        /// 行為:
        /// - 調用 CloseConnection() 關閉 socket 和線程。
        /// - 確保資源正確釋放，避免記憶體洩漏。
        CloseConnection();
    }

    public void OnActivateButtonClicked()
    {
        /// 激活按鈕事件處理函數，發送 "ACTIVATE" 命令到伺服器。
        /// 行為:
        /// - 調用 SendCommand("ACTIVATE\n") 通過 TCP 發送激活命令。
        /// - 命令用於啟動 Raspberry Pi 上的呼吸控制腳本。
        SendCommand("ACTIVATE\n");
    }

    public void OnDeactivateButtonClicked()
    {
        /// 停用按鈕事件處理函數，發送 "DEACTIVATE" 命令到伺服器。
        /// 行為:
        /// - 調用 SendCommand("DEACTIVATE\n") 通過 TCP 發送停用命令。
        /// - 命令用於停止 Raspberry Pi 上的呼吸控制腳本。
        SendCommand("DEACTIVATE\n");
    }

    public void ConnectToServer()
    {
        /// 建立與 Raspberry Pi TCP 伺服器的連接。
        /// 行為:
        /// - 創建 TcpClient 實例。
        /// - 嘗試連接到 serverIp 和 serverPort。
        /// - 如果成功，獲取 NetworkStream，設置 isConnected 為 true，記錄成功訊息。
        /// - 啟動 receiveThread 線程調用 ReceiveLoop() 以接收伺服器數據。
        /// - 如果失敗，記錄錯誤訊息。
        try
        {
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
        /// 發送命令字串到 TCP 伺服器。
        /// 參數:
        /// - cmd: 要發送的命令字串（例如 "ACTIVATE\n"）。
        /// 行為:
        /// - 檢查連接狀態，如果未連接則返回。
        /// - 將命令編碼為 UTF-8 字節。
        /// - 通過 stream.Write() 發送數據。
        /// - 調用 stream.Flush() 確保數據立即發送。
        /// - 記錄發送的命令到 Debug Log。
        /// - 如果發送失敗，記錄錯誤。
        if (!isConnected || stream == null) return;

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(cmd);
            stream.Write(data, 0, data.Length);
            stream.Flush();
            Debug.Log("[CLIENT] Sent: " + cmd.Trim());
        }
        catch (Exception e)
        {
            Debug.LogError("[CLIENT] Send error: " + e.Message);
        }
    }

    private void ReceiveLoop()
    {
        /// 接收線程函數，持續接收來自伺服器的數據。
        /// 行為:
        /// - 初始化 1024 字節緩衝區。
        /// - 在循環中調用 stream.Read() 接收數據。
        /// - 如果接收到數據，將字節解碼為 UTF-8 字串，記錄到 Debug Log。
        /// - 如果接收到 0 字節，表示伺服器斷開，記錄訊息並退出。
        /// - 如果發生異常，記錄錯誤並設置 isConnected 為 false。
        byte[] buffer = new byte[1024];
        try
        {
            while (isConnected)
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0)
                {
                    Debug.Log("[CLIENT] Server disconnected");
                    break;
                }
                string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Debug.Log("[CLIENT] Received: " + msg.Trim());
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[CLIENT] Receive error: " + e.Message);
        }
        finally
        {
            isConnected = false;
        }
    }

    private void CloseConnection()
    {
        /// 關閉 TCP 連接和相關資源。
        /// 行為:
        /// - 設置 isConnected 為 false。
        /// - 如果 receiveThread 正在運行，調用 Abort() 終止。
        /// - 關閉 stream 和 client。
        /// - 忽略異常以避免退出時錯誤。
        try
        {
            isConnected = false;
            if (receiveThread != null && receiveThread.IsAlive)
                receiveThread.Abort();
            if (stream != null) stream.Close();
            if (client != null) client.Close();
        }
        catch (Exception) { }
    }
}