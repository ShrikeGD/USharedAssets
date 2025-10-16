using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;


[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class ActionCooldownShader : MonoBehaviour
{
    [Header("Image")]
    [SerializeField] private Image targetImage;

    [Header("Cooldown (Blue 0.5→1)")]
    [Range(0f, 1f)]
    [SerializeField] private float cooldown01 = 0f;
    [Tooltip("Seconds the 'ready' state persists after cooldown reaches zero. Alpha pulses 0.5..1.")]
    [SerializeField] private float readyWindowSeconds = 0.25f;

    [Header("Charges (Green)")]
    [Tooltip("Treat charges like stacks: integer 0..7. Shader decodes >0.1 as 1.")]
    [SerializeField] private bool usesCharges = true;
    [Min(0)] [SerializeField] private int chargeCount = 0; // 0..7
    [SerializeField] private Image chargeRechargeFillImage;     // optional radial bar

    [Header("Stacks (Red)")]
    [Min(0)] [SerializeField] private int stacks = 0;      // raw (may exceed 9)
    [SerializeField] private bool showTextWhenStacksExceedNine = true;
    [SerializeField] private TextMeshProUGUI stacksOverflowText; // optional SDF text for >9
    [SerializeField] private string overflowFormat = "{0}";

    // Runtime state
    private float _lastCooldownZeroedAt = float.NegativeInfinity;
    private float _prevCooldown01 = -1f;

    // Duration timer (drives blue 0..0.5)
    private bool _durationActive;
    private float _durationStartTime;
    private float _durationTotalSeconds;
    private float _durationProgress01; // 0..1

    // Charge-recharge normalized 0..1 (for optional UI ring)
    private float _chargeRecharge01;

    // Event: duration finished (for ActionBarFieldUI to play finish FX)
    public event Action OnDurationFinished;

    // ----------------- Public API -----------------

    public void SetCooldown01(float value)
    {
        value = Mathf.Clamp01(value);
        if (_prevCooldown01 > 0f && value <= 0f)
            _lastCooldownZeroedAt = Application.isPlaying ? Time.time : 0f;
        cooldown01 = value;
        _prevCooldown01 = value;
        ApplyVisuals();
    }

    public void SetUsesCharges(bool value) { usesCharges = value; ApplyVisuals(); }
    public void SetChargeCount(int count) { chargeCount = Mathf.Clamp(count, 0, 7); ApplyVisuals(); }
    public void SetChargesNormalized(float normalized) { SetChargeCount(Mathf.RoundToInt(Mathf.Clamp01(normalized) * 7f)); }

    public void SetStacks(int value) { stacks = Mathf.Max(0, value); ApplyVisuals(); }

    // Charge recharge 0..1 (UI may update this every frame)
    public void SetChargeRecharge01(float value)
    {
        _chargeRecharge01 = Mathf.Clamp01(value);
        if (chargeRechargeFillImage) chargeRechargeFillImage.fillAmount = _chargeRecharge01;
    }

    // Duration control (drives blue 0..0.5 and raises OnDurationFinished)
    public void StartDuration(float seconds)
    {
        seconds = Mathf.Max(0.0001f, seconds);
        _durationActive = true;
        _durationTotalSeconds = seconds;
        _durationStartTime = Application.isPlaying ? Time.time : 0f;
        _durationProgress01 = 0f;
        ApplyVisuals();
    }

    public void StopDuration()
    {
        _durationActive = false;
        _durationProgress01 = 0f;
        ApplyVisuals();
    }

    // Optional properties
    public float Cooldown01 { get => cooldown01; set => SetCooldown01(value); }
    public bool UsesCharges { get => usesCharges; set => SetUsesCharges(value); }
    public int ChargeCount { get => chargeCount; set => SetChargeCount(value); }
    public int Stacks { get => stacks; set => SetStacks(value); }
    public float ReadyWindowSeconds { get => readyWindowSeconds; set => readyWindowSeconds = Mathf.Max(0f, value); }

    // ----------------- Unity -----------------

    private void Reset()
    {
        if (!targetImage) targetImage = GetComponent<Image>();
        ApplyVisuals();
    }

    private void Awake()
    {
        if (!targetImage) targetImage = GetComponent<Image>();
        _prevCooldown01 = Mathf.Clamp01(cooldown01);
    }

    private void Update()
    {
        // Advance duration if active
        if (_durationActive)
        {
            float now = Application.isPlaying ? Time.time : 0f;
            float elapsed = Mathf.Max(0f, now - _durationStartTime);
            float prev = _durationProgress01;
            _durationProgress01 = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, _durationTotalSeconds));
            if (!Mathf.Approximately(prev, _durationProgress01))
                ApplyVisuals();

            if (_durationProgress01 >= 1f)
            {
                _durationActive = false;
                _durationProgress01 = 0f;
                ApplyVisuals();
                OnDurationFinished?.Invoke();
            }
        }
        // Recharge fill is fed via SetChargeRecharge01; nothing to tick here.
    }

    private void OnValidate()
    {
        if (!targetImage) targetImage = GetComponent<Image>();
        cooldown01 = Mathf.Clamp01(cooldown01);
        chargeCount = Mathf.Clamp(chargeCount, 0, 7);
        stacks = Mathf.Max(0, stacks);
        readyWindowSeconds = Mathf.Max(0f, readyWindowSeconds);

        if (_prevCooldown01 > 0f && Mathf.Approximately(cooldown01, 0f))
            _lastCooldownZeroedAt = 0f;

        _prevCooldown01 = cooldown01;
        if (chargeRechargeFillImage) chargeRechargeFillImage.fillAmount = Mathf.Clamp01(_chargeRecharge01);
        ApplyVisuals();
    }

    // ----------------- Visual Encoding -----------------

    private void ApplyVisuals()
    {
        if (!targetImage) return;

        // Red: stacks (cap 9 for shader encoding). If >9 and text assigned, show text.
        int stacksForShader = Mathf.Min(stacks, 9);
        float r = EncodeStacksToRed(stacksForShader);

        // Green: charges (with overrides you requested)
        float g = EncodeChargesToGreen(
            usesCharges,
            chargeCount,
            durationActive: _durationActive
        );

        // Blue: 0..0.5 = duration, 0.5..1 = cooldown
        float b = EncodeBlue(durationActive: _durationActive, duration01: _durationProgress01, cooldown01: cooldown01);

        // Alpha: ready window pulses 0.5..1, else 1
        float a = EncodeAlphaReadyPulse(cooldown01);

        var c = targetImage.color;
        c.r = r; c.g = g; c.b = b; c.a = a;
        targetImage.color = c;

        // Overflow stacks text
        if (stacksOverflowText)
        {
            bool showOverflow = showTextWhenStacksExceedNine && stacks > 9;
            if (stacksOverflowText.gameObject.activeSelf != showOverflow)
                stacksOverflowText.gameObject.SetActive(showOverflow);

            if (showOverflow)
                stacksOverflowText.text = string.Format(overflowFormat, stacks);
        }
    }

    private float EncodeBlue(bool durationActive, float duration01, float cooldown01)
    {
        if (durationActive)
            return Mathf.Lerp(0f, 0.5f, Mathf.Clamp01(duration01)); // 0 full → 0.5 empty (activation)
        if (cooldown01 > 0f)
            return Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(cooldown01)); // 0.5 idle → 1 cooldown
        return 0.5f; // idle baseline
    }

    private float EncodeAlphaReadyPulse(float cd01)
    {
        bool inReadyWindow;

        if (Application.isPlaying)
            inReadyWindow = (Mathf.Clamp01(cd01) <= 0f) && (Time.time - _lastCooldownZeroedAt <= readyWindowSeconds);
        else
        {
            inReadyWindow = (Mathf.Clamp01(cd01) <= 0f) && Mathf.Approximately(_lastCooldownZeroedAt, 0f);
            if (inReadyWindow) _lastCooldownZeroedAt = float.NegativeInfinity; // one-shot in edit
        }

        if (!inReadyWindow) return 1f;

        // Pulse alpha 0.5..1
        float t = Application.isPlaying ? Time.time : 0f;
        float wave = Mathf.PingPong(t * 6f, 1f);
        return Mathf.Lerp(0.5f, 1f, wave);
    }

    // Green channel bins for charges, with requested overrides
    private float EncodeChargesToGreen(bool uses, int count, bool durationActive)
    {
        // While duration is running, suppress charge visuals entirely
        if (durationActive)
            return 0.0f;

        // When charges are unused, show 0.0 (not 0.10)
        if (!uses)
            return 0.0f;

        // 0 charges -> 1.00 (the "no charges" state)
        if (count <= 0)
            return 1.00f;

        // 1..7 -> 0.15, 0.25, ... 0.75 (midpoints)
        int k = Mathf.Clamp(count, 1, 7);
        const float baseEdge = 0.1f;
        const float step = 0.1f;
        return baseEdge + k * step - step * 0.5f;
    }

    // Red channel bins for stacks (1..9) as midpoints
    private float EncodeStacksToRed(int count)
    {
        if (count <= 0) return 0f;

        int k = Mathf.Clamp(count, 1, 9);
        const float baseEdge = 0.1f;
        const float step = 0.1f;
        return baseEdge + k * step - step * 0.5f;
    }
}
