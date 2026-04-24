using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DogExpressionController : MonoBehaviour
{
    [SerializeField] Animator dogAnimator;
    [SerializeField] EmotionManager emotionManager;
    [SerializeField] Transform headRigTrans;
    [SerializeField] AudioClip[] expressionSound;
    private AudioSource audioSource;
    private int expressionIndex = 0;
    public enum DogExpression
    {
        netural,
        panting,
        shocked,
        laughing,
        barking,
        eyeClosed,
        Confused
    }
    // Start is called before the first frame update
    void Start()
    {
        audioSource = GetComponent<AudioSource>();
    }
    void OnEnable()
    {
        StartCoroutine(BreathingAnimation());
        StartCoroutine(DogExpressionUpdate());
    }
    // Update is called once per frame
    void Update()
    {
    
    }

    private IEnumerator BreathingAnimation()
    {
        while(true)
        {
            if (RpiTcpClient.Instance == null || !RpiTcpClient.Instance.isActiveAndEnabled || !RpiTcpClient.isConnected)
            {
                float breath = Mathf.Sin(Time.time) * 0.5f + 0.5f;
                dogAnimator.SetFloat("BreathProgress", breath);
            }
            yield return null;
        }
    }
    private IEnumerator DogExpressionUpdate()
    {
        while (true)
        {
            if(emotionManager.currentEmotion == EmotionManager.EmotionState.Calibrating)
            {
                expressionIndex = (int)DogExpression.eyeClosed;
            }
            else if(emotionManager.currentEmotion == EmotionManager.EmotionState.Neutral)
            {
                expressionIndex = (int)DogExpression.netural;
            }
            else if (emotionManager.currentEmotion == EmotionManager.EmotionState.Happy)
            {
                expressionIndex = (int)DogExpression.panting;
            }
            else if (emotionManager.currentEmotion == EmotionManager.EmotionState.Sad)
            {
                expressionIndex = (int)DogExpression.shocked;
            }
            else if (emotionManager.currentEmotion == EmotionManager.EmotionState.Angry)
            {
                expressionIndex = (int)DogExpression.barking;
            }
            else if (emotionManager.currentEmotion == EmotionManager.EmotionState.Surprised)
            {
                expressionIndex = (int)DogExpression.Confused;
            }
            dogAnimator.SetInteger("expression", expressionIndex);
            if(expressionSound[expressionIndex] != null)
            {
                audioSource.clip = expressionSound[expressionIndex];
            }
            yield return new WaitForSeconds(1f);
        }
    }
    public void PlaySound()
    {
        audioSource.Play();
    }
    void OnDisable()
    {
        StopAllCoroutines();
        expressionIndex = (int)DogExpression.netural;
        dogAnimator.SetInteger("expression", expressionIndex);
        dogAnimator.SetFloat("BreathProgress", 0.5f);
    }
    void OnDestroy()
    {
        StopAllCoroutines();
    }
}
