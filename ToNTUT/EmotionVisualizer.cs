using UnityEngine;
using TMPro;

public class EmotionVisualizer : MonoBehaviour
{
    [Header("UI 顯示")]
    public TextMeshProUGUI monitorText;

    [Header("資料來源")]
    public EmotionDetector detector;
    public EmotionManager manager;

    void Start()
    {
        if (detector == null) detector = FindObjectOfType<EmotionDetector>();
        if (manager == null) manager = FindObjectOfType<EmotionManager>();
    }

    void Update()
    {
        if (monitorText == null || detector == null || manager == null)
            return;

        // ===== 校正狀態 =====
        if (manager.currentEmotion == EmotionManager.EmotionState.Calibrating)
        {
            monitorText.text =
                $"<size=44><color=yellow><b>臉部校正中</b></color></size>\n\n" +
                $"請保持 <b>放鬆 / Neutral</b>\n" +
                $"不要說話、不做表情\n\n" +
                $"<size=24>（約 {detector.calibrationDuration:F1} 秒）</size>";
            return;
        }

        // ===== 正常顯示 =====
        string color = "white";
        switch (manager.currentEmotion)
        {
            case EmotionManager.EmotionState.Happy: color = "green"; break;
            case EmotionManager.EmotionState.Angry: color = "red"; break;
            case EmotionManager.EmotionState.Sad: color = "blue"; break;
            case EmotionManager.EmotionState.Surprised: color = "yellow"; break;
            case EmotionManager.EmotionState.Neutral: color = "white"; break;
        }

        monitorText.text =
            $"<size=36><b>Emotion Monitor</b></size>\n\n" +
            $"Current Emotion:\n" +
            $"<size=48><color={color}><b>{manager.currentEmotion}</b></color></size>\n" +
            $"------------------------\n" +
            $"Happy     : {detector.scoreHappy:F2}\n" +
            $"Angry     : {detector.scoreAngry:F2}\n" +
            $"Sad       : {detector.scoreSad:F2}\n" +
            $"Surprise  : {detector.scoreSurprise:F2}\n\n" +
            $"<size=20>Activity Threshold: {detector.activityThreshold:F2}</size>";
    }
}
