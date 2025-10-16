// AbilityStateColor.cs
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class AbilityStateColor : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Image targetImage;

    [Header("Cooldown (Blue)")]
    [Range(0f, 1f)]
    [SerializeField] private float cooldown01 = 0f;
    [Tooltip("Seconds the 'ready' state (blue 0..0.5) persists after cooldown reaches zero.")]
    [SerializeField] private float readyWindowSeconds = 0.25f;

    [Header("Charges (Green)")]
    [Tooltip("Treat charges like stacks: integer 0..7. Shader decodes >0.1 as 1.")]
    [SerializeField] private bool usesCharges = true;
    [Min(0)] [SerializeField] private int chargeCount = 0; // 0..7

    [Header("Stacks (Red)")]
    [Min(0)] [SerializeField] private int stacks = 0;      // 0..9

    private float _lastCooldownZeroedAt = float.NegativeInfinity;
    private float _prevCooldown01 = -1f;

    // --- Public API ---
    public void SetCooldown01(float value)
    {
        value = Mathf.Clamp01(value);
        if (_prevCooldown01 > 0f && value <= 0f)
            _lastCooldownZeroedAt = Application.isPlaying ? Time.time : 0f;
        cooldown01 = value;
        _prevCooldown01 = value;
        ApplyColor();
    }

    public void SetUsesCharges(bool value) { usesCharges = value; ApplyColor(); }
    public void SetChargeCount(int count) { chargeCount = Mathf.Clamp(count, 0, 7); ApplyColor(); }
    public void SetChargesNormalized(float normalized) { SetChargeCount(Mathf.RoundToInt(Mathf.Clamp01(normalized) * 7f)); }
    public void SetStacks(int value) { stacks = Mathf.Clamp(value, 0, 9); ApplyColor(); }

    private void Reset() { if (!targetImage) targetImage = GetComponent<Image>(); ApplyColor(); }
    private void Awake() { if (!targetImage) targetImage = GetComponent<Image>(); _prevCooldown01 = Mathf.Clamp01(cooldown01); }
    private void Update() { ApplyColor(); }

    private void OnValidate()
    {
        if (!targetImage) targetImage = GetComponent<Image>();
        cooldown01 = Mathf.Clamp01(cooldown01);
        chargeCount = Mathf.Clamp(chargeCount, 0, 7);
        stacks = Mathf.Clamp(stacks, 0, 9);

        if (_prevCooldown01 > 0f && Mathf.Approximately(cooldown01, 0f))
            _lastCooldownZeroedAt = 0f;

        _prevCooldown01 = cooldown01;
        ApplyColor();
    }

    private void ApplyColor()
    {
        if (!targetImage) return;

        float r = EncodeStacksToRed(stacks);
        float g = EncodeChargesToGreen(usesCharges, chargeCount);
        float b = EncodeCooldownToBlue(cooldown01);

        var c = targetImage.color;
        c.r = r; c.g = g; c.b = b; c.a = 1f;
        targetImage.color = c;
    }

    // --- Blue (cooldown) unchanged ---
    private float EncodeCooldownToBlue(float cd01)
    {
        cd01 = Mathf.Clamp01(cd01);
        bool inReadyWindow;

        if (Application.isPlaying)
            inReadyWindow = (cd01 <= 0f) && (Time.time - _lastCooldownZeroedAt <= readyWindowSeconds);
        else
        {
            inReadyWindow = (cd01 <= 0f) && Mathf.Approximately(_lastCooldownZeroedAt, 0f);
            if (inReadyWindow) _lastCooldownZeroedAt = float.NegativeInfinity;
        }

        if (inReadyWindow) return 0f;   // show "ready" in 0..0.5
        if (cd01 <= 0f) return 0.5f;    // no cooldown active baseline
        return Mathf.Lerp(0.5f, 1f, cd01);
    }

    // --- Green (charges) aligned to shader: bins start just above 0.1 ---
    //  usesCharges == false      -> 0.10 exact
    //  usesCharges == true, 0    -> 1.00 exact
    //  usesCharges == true, 1..7 -> 0.15, 0.25, ... 0.75  (midpoints of (0.1,0.2],...,(0.7,0.8])
    private float EncodeChargesToGreen(bool uses, int count)
    {
        if (!uses) return 0.10f;
        if (count <= 0) return 1.00f;

        int k = Mathf.Clamp(count, 1, 7);
        const float baseEdge = 0.1f;  // shader threshold for "1"
        const float step = 0.1f;      // one integer per +0.1
        return baseEdge + k * step - step * 0.5f; // midpoint
    }

    // --- Red (stacks) aligned to shader and avoids 1.0 ---
    //  0 -> 0.00 (null)
    //  1..9 -> 0.15, 0.25, ... 0.95  (midpoints; never 1.0 so no wrap)
    private float EncodeStacksToRed(int count)
    {
        if (count <= 0) return 0f;

        int k = Mathf.Clamp(count, 1, 9);
        const float baseEdge = 0.1f;
        const float step = 0.1f;
        return baseEdge + k * step - step * 0.5f; // midpoint
    }

    // Optional properties
    public float Cooldown01 { get => cooldown01; set => SetCooldown01(value); }
    public bool UsesCharges { get => usesCharges; set => SetUsesCharges(value); }
    public int ChargeCount { get => chargeCount; set => SetChargeCount(value); }
    public int Stacks { get => stacks; set => SetStacks(value); }
}
