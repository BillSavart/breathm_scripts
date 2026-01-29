using UnityEngine;

public class EmotionManager : MonoBehaviour
{
    // 系統唯一情緒狀態定義
    public enum EmotionState
    {
        Calibrating,
        Neutral,
        Happy,
        Sad,
        Angry,
        Surprised
    }

    [Header("當前情緒狀態")]
    public EmotionState currentEmotion = EmotionState.Calibrating;
}
