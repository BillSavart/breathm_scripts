using UnityEngine;

public class EmotionDetector : MonoBehaviour
{
    [Header("必要組件")]
    public EmotionManager manager;
    public OVRFaceExpressions AvatarFace;

    [Header("校正設定")]
    public float calibrationDuration = 2.0f;
    private float calibrationTimer;
    private int calibrationFrameCount;
    private bool isCalibrated = false;

    [Header("平滑設定 (EMA)")]
    [Range(0.01f, 0.5f)]
    public float smoothing = 0.15f;

    [Header("靈敏度")]
    public float sensitivity = 2.4f;

    [Header("Neutral / 活動量門檻")]
    public float activityThreshold = 0.35f;

    [Header("眨眼抑制")]
    public float blinkThreshold = 0.55f;

    [Header("說話抑制")]
    public float talkJawThreshold = 0.55f;
    // ★降低：不再要求你瞪很大才算驚訝
    public float surpriseEyeMin = 0.25f;

    [Header("切換遲滯")]
    public int switchHoldFrames = 6;
    public int switchCooldownFrames = 10;

    // ===== baseline =====
    private float zero_smile, zero_cheek, zero_frown;
    private float zero_brow_lower, zero_jaw, zero_upper_lid;
    private float zero_lid_tightener, zero_outer_brow, zero_inner_brow;
    private float zero_lip_pressor, zero_eye_closed;

    // ===== smoothed raw =====
    private float s_smile, s_cheek, s_frown;
    private float s_brow_lower, s_jaw, s_upper_lid;
    private float s_lid_tightener, s_outer_brow, s_inner_brow;
    private float s_lip_pressor, s_eye_closed;

    // ===== features =====
    private float smile, cheek, frown;
    private float brow_lower, jaw, upper_lid;
    private float lid_tightener, outer_brow, inner_brow;
    private float lip_pressor, eye_closed;

    [Header("分數 (越高越像)")]
    public float scoreHappy, scoreSad, scoreAngry, scoreSurprise;

    // ===== switching =====
    private EmotionManager.EmotionState stableEmotion = EmotionManager.EmotionState.Neutral;
    private EmotionManager.EmotionState candidateEmotion = EmotionManager.EmotionState.Neutral;
    private int candidateCount = 0;
    private int cooldown = 0;

    void Start()
    {
        /// <summary>
        /// 初始化 EmotionDetector 組件。
        /// 設置管理器的當前情緒為校正狀態，並重置校正數據。
        /// </summary>
        if (manager != null) manager.currentEmotion = EmotionManager.EmotionState.Calibrating;
        ResetCalibration();
    }

    void Update()
    {
        /// <summary>
        /// 每幀更新情緒檢測邏輯。
        /// 檢查必要的組件是否存在，如果不存在則返回。
        /// 讀取並平滑原始面部表情數據。
        /// 如果尚未校正，執行校正過程。
        /// 應用基準線調整。
        /// 檢查眨眼抑制，如果眨眼超過閾值，保持穩定情緒。
        /// 計算活動量，如果低於閾值，設置為中性並更新動態基準線。
        /// 檢查講話抑制，判斷是否可能在說話。
        /// 計算情緒分數。
        /// 應用遲滯穩定機制。
        /// </summary>
        if (AvatarFace == null || manager == null) return;

        ReadAndSmoothRaw();

        if (!isCalibrated)
        {
            Calibrate();
            return;
        }

        ApplyBaseline();

        // 1) 眨眼：不更新分類
        if (eye_closed >= blinkThreshold)
        {
            manager.currentEmotion = stableEmotion;
            return;
        }

        // 2) 活動量 gating：沒表情就 Neutral + baseline 漂移修正
        float activity = Mathf.Max(
            smile, cheek, frown,
            brow_lower, jaw, upper_lid,
            lid_tightener, outer_brow, inner_brow,
            lip_pressor
        );

        if (activity < activityThreshold)
        {
            stableEmotion = EmotionManager.EmotionState.Neutral;
            manager.currentEmotion = EmotionManager.EmotionState.Neutral;
            UpdateDynamicBaseline();
            return;
        }

        // 3) 講話抑制：jaw 高但沒有驚訝眉眼 → 抑制 Sad/Surprise（避免跳動）
        bool surpriseEyeOK = Mathf.Max(upper_lid, outer_brow, inner_brow) >= surpriseEyeMin;
        bool isLikelyTalking = (jaw >= talkJawThreshold) && !surpriseEyeOK && (brow_lower < 0.60f);

        CalculateEmotionV4(isLikelyTalking);

        // 4) 遲滯穩定
        ApplyHysteresis();
    }

    // --------------------------------------------------
    void ReadAndSmoothRaw()
    {
        /// <summary>
        /// 讀取原始面部表情權重並應用指數移動平均 (EMA) 平滑。
        /// 從 OVRFaceExpressions 獲取各種面部表情的權重值，包括微笑、臉頰、皺眉等。
        /// 對每個表情應用 EMA 平滑，以減少噪聲和抖動。
        /// 平滑係數由 smoothing 參數控制。
        /// </summary>
        float raw_smile =
            (AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.LipCornerPullerL) +
             AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.LipCornerPullerR)) * 0.5f;

        float raw_cheek =
            (AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.CheekRaiserL) +
             AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.CheekRaiserR)) * 0.5f;

        float raw_frown =
            (AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.LipCornerDepressorL) +
             AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.LipCornerDepressorR)) * 0.5f;

        float raw_brow_lower =
            (AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.BrowLowererL) +
             AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.BrowLowererR)) * 0.5f;

        float raw_jaw = AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.JawDrop);

        float raw_upper_lid =
            (AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.UpperLidRaiserL) +
             AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.UpperLidRaiserR)) * 0.5f;

        float raw_lid_tightener =
            (AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.LidTightenerL) +
             AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.LidTightenerR)) * 0.5f;

        float raw_outer_brow =
            (AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.OuterBrowRaiserL) +
             AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.OuterBrowRaiserR)) * 0.5f;

        float raw_inner_brow =
            (AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.InnerBrowRaiserL) +
             AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.InnerBrowRaiserR)) * 0.5f;

        float raw_lip_pressor =
            (AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.LipPressorL) +
             AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.LipPressorR)) * 0.5f;

        float raw_eye_closed =
            (AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.EyesClosedL) +
             AvatarFace.GetWeight(OVRFaceExpressions.FaceExpression.EyesClosedR)) * 0.5f;

        // EMA
        s_smile = Mathf.Lerp(s_smile, raw_smile, smoothing);
        s_cheek = Mathf.Lerp(s_cheek, raw_cheek, smoothing);
        s_frown = Mathf.Lerp(s_frown, raw_frown, smoothing);
        s_brow_lower = Mathf.Lerp(s_brow_lower, raw_brow_lower, smoothing);
        s_jaw = Mathf.Lerp(s_jaw, raw_jaw, smoothing);
        s_upper_lid = Mathf.Lerp(s_upper_lid, raw_upper_lid, smoothing);
        s_lid_tightener = Mathf.Lerp(s_lid_tightener, raw_lid_tightener, smoothing);
        s_outer_brow = Mathf.Lerp(s_outer_brow, raw_outer_brow, smoothing);
        s_inner_brow = Mathf.Lerp(s_inner_brow, raw_inner_brow, smoothing);
        s_lip_pressor = Mathf.Lerp(s_lip_pressor, raw_lip_pressor, smoothing);
        s_eye_closed = Mathf.Lerp(s_eye_closed, raw_eye_closed, smoothing);
    }

    // --------------------------------------------------
    void Calibrate()
    {
        /// <summary>
        /// 執行面部表情校正過程。
        /// 在校正期間，累積平滑後的面部表情權重值。
        /// 當校正時間達到 calibrationDuration 時，計算平均基準線值。
        /// 設置 isCalibrated 為 true，並將情緒設置為 Neutral。
        /// </summary>
        calibrationTimer += Time.deltaTime;

        zero_smile += s_smile;
        zero_cheek += s_cheek;
        zero_frown += s_frown;
        zero_brow_lower += s_brow_lower;
        zero_jaw += s_jaw;
        zero_upper_lid += s_upper_lid;
        zero_lid_tightener += s_lid_tightener;
        zero_outer_brow += s_outer_brow;
        zero_inner_brow += s_inner_brow;
        zero_lip_pressor += s_lip_pressor;
        zero_eye_closed += s_eye_closed;

        calibrationFrameCount++;
        manager.currentEmotion = EmotionManager.EmotionState.Calibrating;

        if (calibrationTimer >= calibrationDuration)
        {
            float inv = 1f / Mathf.Max(1, calibrationFrameCount);
            zero_smile *= inv;
            zero_cheek *= inv;
            zero_frown *= inv;
            zero_brow_lower *= inv;
            zero_jaw *= inv;
            zero_upper_lid *= inv;
            zero_lid_tightener *= inv;
            zero_outer_brow *= inv;
            zero_inner_brow *= inv;
            zero_lip_pressor *= inv;
            zero_eye_closed *= inv;

            isCalibrated = true;
            stableEmotion = EmotionManager.EmotionState.Neutral;
            manager.currentEmotion = EmotionManager.EmotionState.Neutral;
        }
    }

    void ResetCalibration()
    {
        /// <summary>
        /// 重置校正數據。
        /// 將校正計時器、幀數計數器重置為 0，並設置 isCalibrated 為 false。
        /// </summary>
        calibrationTimer = 0;
        calibrationFrameCount = 0;
        isCalibrated = false;
    }

    // --------------------------------------------------
    void ApplyBaseline()
    {
        /// <summary>
        /// 應用基準線調整到平滑後的面部表情數據。
        /// 從平滑值中減去基準線值，應用靈敏度倍數，並確保結果不小於 0。
        /// 對於 lid_tightener，額外減去眼閉合的影響以避免眨眼干擾。
        /// </summary>
        smile = Mathf.Max(0, (s_smile - zero_smile) * sensitivity);
        cheek = Mathf.Max(0, (s_cheek - zero_cheek) * sensitivity);
        frown = Mathf.Max(0, (s_frown - zero_frown) * sensitivity);
        brow_lower = Mathf.Max(0, (s_brow_lower - zero_brow_lower) * sensitivity);
        jaw = Mathf.Max(0, (s_jaw - zero_jaw) * sensitivity);
        upper_lid = Mathf.Max(0, (s_upper_lid - zero_upper_lid) * sensitivity);
        outer_brow = Mathf.Max(0, (s_outer_brow - zero_outer_brow) * sensitivity);
        inner_brow = Mathf.Max(0, (s_inner_brow - zero_inner_brow) * sensitivity);
        lip_pressor = Mathf.Max(0, (s_lip_pressor - zero_lip_pressor) * sensitivity);
        eye_closed = Mathf.Max(0, (s_eye_closed - zero_eye_closed) * sensitivity);

        // LidTightener 做眨眼扣除
        float lidEff = (s_lid_tightener - zero_lid_tightener) * sensitivity;
        lidEff = Mathf.Max(0, lidEff - eye_closed * 0.9f);
        lid_tightener = lidEff;
    }

    // --------------------------------------------------
    // ★ V5：加權平均分數版 (Score Based, Higher is Better)
    void CalculateEmotionV4(bool isLikelyTalking)
    {
        /// <summary>
        /// 計算各情緒的分數，使用加權平均方法。
        /// 根據面部表情特徵計算 Happy、Sad、Angry、Surprise 的分數。
        /// 應用各種抑制和扣分邏輯以提高準確性。
        /// </summary>
        /// <param name="isLikelyTalking">是否可能在說話，用於抑制 Sad 和 Surprise。</param>
        // 常用組合
        float eyeSurprise = Mathf.Max(upper_lid, outer_brow, inner_brow); // 眉眼張開
        float crySignal = Mathf.Max(frown, inner_brow);                   // 哭/委屈
        bool strongSurprisePattern = (jaw > 0.65f) && (eyeSurprise > 0.22f); 

        // 😄 Happy：Cheek + Smile 是主力
        // 權重 (Feature, Weight, Threshold)
        scoreHappy = GetScore(
            (cheek, 1.0f, 0.20f),
            (smile, 0.8f, 0.20f),
            (lid_tightener, 0.4f, 0.15f)
        );

        // 反證據：扣分
        if (jaw > 0.60f) scoreHappy -= 0.30f;
        if (crySignal > 0.35f) scoreHappy -= 0.40f;

        // 😢 Sad：Frown + InnerBrow
        scoreSad = GetScore(
            (frown, 1.0f, 0.20f),
            (inner_brow, 0.8f, 0.15f)
        );

        // 避免與皺眉(Angry)混淆 -> 扣分
        if (brow_lower > 0.45f) scoreSad -= 0.30f;

        // 😡 Angry：BrowLower (主力) + LipPressor
        scoreAngry = GetScore(
            (brow_lower, 1.0f, 0.20f),
            (lip_pressor, 0.6f, 0.10f),
            (lid_tightener, 0.4f, 0.10f)
        );

        // ★ 笑臉排除：如果是笑臉，怒這項要扣爛
        float happySignal = Mathf.Max(cheek, smile);
        if (happySignal > 0.45f) scoreAngry -= 0.60f;

        // 驚訝排除：如果像驚訝，怒要扣分
        if (strongSurprisePattern) scoreAngry -= 0.60f;

        // 委屈眉：扣分
        if (inner_brow > 0.35f) scoreAngry -= 0.20f;

        // 😲 Surprise：Jaw + EyeSurprise
        scoreSurprise = GetScore(
            (jaw, 1.0f, 0.25f),
            (eyeSurprise, 0.8f, 0.15f)
        );

        // 強烈驚訝加分
        if (strongSurprisePattern) scoreSurprise += 0.20f;

        // 沒眉眼 -> 單純張嘴 -> 扣分
        if (eyeSurprise < 0.18f) scoreSurprise -= 0.40f;

        // 講話抑制：扣除 Sad & Surprise 分數
        if (isLikelyTalking)
        {
            scoreSad -= 0.30f;
            scoreSurprise -= 0.40f;
        }
    }

    // (Value, Weight, Threshold) -> Weighted Average (0~1)
    // 若 Value < Threshold，該項視為 0 (或不計分? 這裡採視為0降低平均)
    float GetScore(params (float val, float weight, float threshold)[] fs)
    {
        /// <summary>
        /// 計算加權平均分數。
        /// 對於每個 (值, 權重, 閾值) 元組，如果值低於閾值，則貢獻為 0。
        /// 返回 0 到 1 之間的平均分數。
        /// </summary>
        /// <param name="fs">參數數組，每個元素包含值、權重和閾值。</param>
        /// <returns>加權平均分數，範圍 0 到 1。</returns>
        float totalScore = 0f;
        float totalWeight = 0f;

        foreach (var item in fs)
        {
            float effVal = item.val;
            // 未達門檻：該特徵貢獻 0 分
            if (effVal < item.threshold) effVal = 0f;

            totalScore += effVal * item.weight;
            totalWeight += item.weight;
        }

        if (totalWeight <= 0.0001f) return 0f;
        
        // 結果 0~1 (視 Input 大小而定，Input 大多 0~1)
        float avg = totalScore / totalWeight;
        return Mathf.Clamp01(avg);
    }

    // --------------------------------------------------
    void ApplyHysteresis()
    {
        /// <summary>
        /// 應用遲滯穩定機制以避免情緒快速切換。
        /// 減少冷卻時間。
        /// 找到最高分數的情緒。
        /// 如果在冷卻期間，保持穩定情緒。
        /// 如果分數太低，設置為 Neutral。
        /// 如果候選情緒持續足夠幀數，切換到穩定情緒並設置冷卻。
        /// 更新管理器的當前情緒。
        /// </summary>
        if (cooldown > 0) cooldown--;

        // 找最高分
        float bestScore = 0.0f; 
        // 預設 Neutral (若分數都很低)
        EmotionManager.EmotionState bestE = EmotionManager.EmotionState.Neutral;

        // 設定一個基礎門檻，避免雜訊觸發? 
        // 但 Update 裡已經有 activityThreshold 把關 Neutral
        // 所以這裡直接比大小即可。
        // 不過若全都很小(且 activity 過了)，可能還是維持前一個比較好?
        // 暫定直接比大小
        
        // 比較 Happy
        if (scoreHappy > bestScore) { bestScore = scoreHappy; bestE = EmotionManager.EmotionState.Happy; }
        // 比較 Sad
        if (scoreSad > bestScore) { bestScore = scoreSad; bestE = EmotionManager.EmotionState.Sad; }
        // 比較 Angry
        if (scoreAngry > bestScore) { bestScore = scoreAngry; bestE = EmotionManager.EmotionState.Angry; }
        // 比較 Surprise
        if (scoreSurprise > bestScore) { bestScore = scoreSurprise; bestE = EmotionManager.EmotionState.Surprised; }

        if (cooldown > 0)
        {
            manager.currentEmotion = stableEmotion;
            return;
        }

        // 這裡可以加一個最低有效分數 (e.g. 0.1)，若連 0.1 都沒有就 Neutral
        if (bestScore < 0.1f) bestE = EmotionManager.EmotionState.Neutral;

        if (bestE == candidateEmotion) candidateCount++;
        else { candidateEmotion = bestE; candidateCount = 1; }

        if (candidateCount >= switchHoldFrames)
        {
            stableEmotion = candidateEmotion;
            cooldown = switchCooldownFrames;
            candidateCount = 0;
        }

        manager.currentEmotion = stableEmotion;
    }

    // --------------------------------------------------
    void UpdateDynamicBaseline()
    {
        /// <summary>
        /// 更新動態基準線以適應長期漂移。
        /// 使用低速率插值將基準線向當前平滑值靠近。
        /// 適用於所有面部表情特徵。
        /// </summary>
        float rate = 0.002f;
        zero_smile = Mathf.Lerp(zero_smile, s_smile, rate);
        zero_cheek = Mathf.Lerp(zero_cheek, s_cheek, rate);
        zero_frown = Mathf.Lerp(zero_frown, s_frown, rate);
        zero_brow_lower = Mathf.Lerp(zero_brow_lower, s_brow_lower, rate);
        zero_jaw = Mathf.Lerp(zero_jaw, s_jaw, rate);
        zero_upper_lid = Mathf.Lerp(zero_upper_lid, s_upper_lid, rate);
        zero_lid_tightener = Mathf.Lerp(zero_lid_tightener, s_lid_tightener, rate);
        zero_outer_brow = Mathf.Lerp(zero_outer_brow, s_outer_brow, rate);
        zero_inner_brow = Mathf.Lerp(zero_inner_brow, s_inner_brow, rate);
        zero_lip_pressor = Mathf.Lerp(zero_lip_pressor, s_lip_pressor, rate);
        zero_eye_closed = Mathf.Lerp(zero_eye_closed, s_eye_closed, rate);
    }
}
