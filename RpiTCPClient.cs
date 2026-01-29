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

    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private bool isConnected = false;

    void Start()
    {
        ConnectToServer();
    }

    void OnApplicationQuit()
    {
        CloseConnection();
    }

    public void OnActivateButtonClicked()
    {
        SendCommand("ACTIVATE\n");
    }

    public void OnDeactivateButtonClicked()
    {
        SendCommand("DEACTIVATE\n");
    }

    public void ConnectToServer()
    {
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
