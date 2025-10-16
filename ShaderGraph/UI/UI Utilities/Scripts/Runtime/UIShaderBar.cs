using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Graphic))]
public class UIShaderBar : MonoBehaviour
{
    [Header("Target (0..1)")]
    [Range(0f, 1f)] public float barValue = 1f;   // Set from anywhere; changes are auto-detected

    [Header("Smoothing")]
    [Min(0f)] public float baseSpeed = 12f;       // drives R (main bar)
    [Min(0f)] public float secondarySpeed = 3f;   // drives G (lag/damage bar)
    [Min(0f)] public float epsilonSnap = 0.001f;
    [Range(0.008f, 0.1f)] public float maxDt = 0.05f;
    public bool useUnscaledTime = true;

    [Header("Blue Divider Encoding")]
    [Tooltip("How many fixed bins Blue encodes. Keep this equal to the multiply you use in the shader (e.g., 40).")]
    [Min(1)] public int blueBins = 40; // shader multiplies by 40 → keep this 40

    // Internals
    private Graphic _graphic;
    private float _dispBase, _dispSecondary;

    private void Awake()
    {
        _graphic = GetComponent<Graphic>();
    }

    private void OnEnable()
    {
        if (_graphic == null) _graphic = GetComponent<Graphic>();

        barValue = Mathf.Clamp01(barValue);
        _dispBase = _dispSecondary = barValue;

        PushColor();
    }

    // Public API
    public void SetTarget01(float v) => barValue = Mathf.Clamp01(v);
    public void AddToTarget01(float d) => SetTarget01(barValue + d);

    private void LateUpdate()
    {
        if (_graphic == null) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (dt <= 0f) return;
        if (dt > maxDt) dt = maxDt;

        // Smooth R/G toward target (visual smoothing)
        barValue       = Mathf.Clamp01(barValue);
        _dispBase      = SmoothSnap01(_dispBase,      barValue, baseSpeed,      dt, epsilonSnap);
        _dispSecondary = SmoothSnap01(_dispSecondary, barValue, secondarySpeed, dt, epsilonSnap);

        // Blue = divider index ONLY, computed from the RAW TARGET (unsmoothed) and fixed bin count.
        float bLane = EncodeBlueDivider(barValue, blueBins);

        _graphic.color = new Color(_dispBase, _dispSecondary, bLane, 1f); // A unused
    }

    private void PushColor()
    {
        if (_graphic == null) return;
        float bLane = EncodeBlueDivider(barValue, blueBins);
        _graphic.color = new Color(_dispBase, _dispSecondary, bLane, 1f);
    }

    // Helpers
    private static float SmoothSnap01(float current, float target, float speed, float dt, float eps)
    {
        if (speed > 0f) current += (target - current) * speed * dt;
        if (Mathf.Abs(target - current) <= eps) current = target;
        return Mathf.Clamp01(current);
    }

    // Encode blue as the MIDPOINT of the current fixed bin so shader floor(b*blueBins) is stable.
    // bin = floor(target * blueBins) in 0..blueBins-1
    // B = (bin + 0.5) / blueBins  (never lands exactly on edges)
    private static float EncodeBlueDivider(float target01, int bins)
    {
        bins = Mathf.Max(1, bins);
        float t = Mathf.Clamp01(target01);
        float bin = Mathf.Floor(t * bins);          // 0..bins-1
        return (bin + 0.5f) / bins;                 // midpoint
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        barValue = Mathf.Clamp01(barValue);
        blueBins = Mathf.Max(1, blueBins);

        if (!Application.isPlaying)
        {
            if (_graphic == null) _graphic = GetComponent<Graphic>();
            _dispBase = _dispSecondary = barValue;
            PushColor();
        }
    }
#endif

    // Optional read-onlys for debug
    public float DisplayBase01      => _dispBase;
    public float DisplaySecondary01 => _dispSecondary;
}
