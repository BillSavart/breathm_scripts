using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class ControllerInputManager : MonoBehaviour
{
    [SerializeField] private PillowTracker pillowTracker;
    [SerializeField] private DogExpressionController dogExpressionController;
    [SerializeField] private EmotionDetector emotionDetector;
    [SerializeField] private EmotionManager emotionManager;
    [SerializeField] private TextChanger insTextChanger;
    [SerializeField] private TextMeshPro textMeshPro;
    private Coroutine rpiStatusCoroutine;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
        if(OVRInput.GetDown(OVRInput.RawButton.A) && PassthroughController.Instance != null)
        {
            Debug.Log("[INPUT] A Button Pressed - Toggling Passthrough");
            PassthroughController.Instance.TogglePassthrough();
            
        }
        if(OVRInput.GetDown(OVRInput.RawButton.B) && pillowTracker != null)
        {
            Debug.Log("[INPUT] B Button Pressed - Toggling Pillow Tracker");
            pillowTracker.enabled = !pillowTracker.enabled;
            insTextChanger.ChangeText();
            CancelInvoke();
            textMeshPro.text = pillowTracker.enabled ? "Pillow Tracker: ON" : "Pillow Tracker: OFF";
            Invoke("clearText", 2f);
        }
        if(OVRInput.GetDown(OVRInput.RawButton.X) && dogExpressionController != null)
        {
            Debug.Log("[INPUT] X Button Pressed - Toggling Dog Expression Controller");
            dogExpressionController.enabled = !dogExpressionController.enabled;
            if(dogExpressionController.enabled)
            {
                emotionManager.currentEmotion = EmotionManager.EmotionState.Calibrating;
                emotionDetector.ResetCalibration();
            }
            CancelInvoke();
            textMeshPro.text = dogExpressionController.enabled ? "Dog Expression Controller: ON" : "Dog Expression Controller: OFF";
            Invoke("clearText", 2f);
        }
        if(OVRInput.GetDown(OVRInput.RawButton.Y) && RpiTcpClient.Instance != null)
        {   
            CancelInvoke();
            Debug.Log("[INPUT] Y Button Pressed - Connecting to Raspberry Pi");
            RpiTcpClient rpiClient = RpiTcpClient.Instance;
            if (RpiTcpClient.isConnected)
            {
                rpiClient.CloseConnection();
                textMeshPro.text = "Disconnected from Raspberry Pi";
                Invoke("clearText", 2f);
            }
            else if (rpiClient.IsConnecting)
            {
                textMeshPro.text = "Still connecting to Raspberry Pi...";
                Invoke("clearText", 2f);
            }
            else
            {
                textMeshPro.text = "Connecting to Raspberry Pi...";
                rpiClient.ConnectToServer();

                if (rpiStatusCoroutine != null)
                    StopCoroutine(rpiStatusCoroutine);

                rpiStatusCoroutine = StartCoroutine(UpdateRpiConnectionText(rpiClient));
            }
        }
    }

    private IEnumerator UpdateRpiConnectionText(RpiTcpClient rpiClient)
    {
        while (rpiClient != null && rpiClient.IsConnecting)
        {
            yield return null;
        }

        if (rpiClient != null && RpiTcpClient.isConnected)
        {
            textMeshPro.text = "Connected to Raspberry Pi!";
        }
        else if (rpiClient != null)
        {
            textMeshPro.text = "Failed to connect: " + rpiClient.LastStatusMessage;
        }
        else
        {
            textMeshPro.text = "Failed to connect to Raspberry Pi.";
        }

        Invoke("clearText", 2f);
        rpiStatusCoroutine = null;
    }

    private void clearText()
    {
        textMeshPro.text = "";
    }
}
