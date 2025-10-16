using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Graphic))]
public class ShaderBar : MonoBehaviour
{
    [Header("Target (0..1)")]
    [Range(0f, 1f)] public float barValue = 1f;   // Set this anywhere; changes are detected each frame

    [Header("Smoothing")]
    [Min(0f)] public float baseSpeed = 12f;      // drives _BarValue
    [Min(0f)] public float secondarySpeed = 3f;  // drives _Bar2Value
    [Min(0f)] public float epsilonSnap = 0.001f;
    [Range(0.008f, 0.1f)] public float maxDt = 0.05f;
    public bool useUnscaledTime = true;

    [Header("Flashes (auto-trigger from barValue; decay while > 0)")]
    [Min(0f)] public float gainFlashFadePerSec = 1.5f;  // increase
    [Min(0f)] public float loseFlashFadePerSec = 2.5f;  // decrease (flicker)
    [Min(0f)] public float fullFlashFadePerSec = 1.0f;  // cross to full (linear)
    [Min(0f)] public float loseFlashFlickerHz = 7f;     // Lose is the flicker one
    [Range(0f,1f)] public float loseFlashFlickerAmp = 1f;

    [Header("Value Alpha (range map on TARGET barValue)")]
    public bool alphaInverse = false;
    [Range(0f,1f)] public float alphaMin = 0f;
    [Range(0f,1f)] public float alphaMax = 0.4f;

    [Header("Shader Parameter Names (must exist in shader)")]
    public string barParam          = "_BarValue";
    public string bar2Param         = "_Bar2Value";
    public string gainFlashParam    = "_GainFlash";           // increase
    public string fullFlashParam    = "_FullyFilledFlash";    // reaching 1 (linear)
    public string loseFlashParam    = "_LoseFlash";           // decrease (7 Hz flicker)
    public string valueAlphaParam   = "_ValueAlpha";

    [Header("Material")]
    public Material sourceMaterialOverride;

    // Exposed flash levels (decay while > 0)
    [Range(0f,1f)] public float gainFlash = 0f;
    [Range(0f,1f)] public float fullFlash = 0f;   // fully-filled
    [Range(0f,1f)] public float loseFlash = 0f;   // decrease (flicker)

    // Internals
    private Graphic _graphic;
    private Material _runtimeMat, _originalMat;
    private float _dispBase, _dispSecondary;

    // IDs & last pushed values
    private int _barID=-1, _bar2ID=-1, _gainID=-1, _fullID=-1, _loseID=-1, _alphaID=-1;
    private float _lastBar=float.NaN, _lastBar2=float.NaN, _lastGain=float.NaN, _lastFull=float.NaN, _lastLose=float.NaN, _lastAlpha=float.NaN;

    // Change detection & flicker phase
    private float _prevTarget01 = -1f;  // initialize to invalid so first frame triggers detection
    private float _loseFlickerTime = 0f;

    void Awake() { _graphic = GetComponent<Graphic>(); }

    void OnEnable()
    {
        if (_graphic == null) _graphic = GetComponent<Graphic>();
        _originalMat = _graphic ? _graphic.material : null;

        EnsureRuntimeMaterial();
        CacheIDs();

        barValue = Mathf.Clamp01(barValue);
        _dispBase = _dispSecondary = barValue;

        // Force initial push
        PushAll(force:true);

        // Start change detector at current value
        _prevTarget01 = barValue;
        _loseFlickerTime = 0f;
    }

    void OnDisable()
    {
        if (_graphic && _originalMat) _graphic.material = _originalMat;
    }

    void OnDestroy()
    {
        if (_runtimeMat != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(_runtimeMat);
            else Destroy(_runtimeMat);
#else
            Destroy(_runtimeMat);
#endif
            _runtimeMat = null;
        }
    }

    void OnCanvasHierarchyChanged() => ReapplyRuntimeMaterial();

    // Public API (optional use)
    public void SetTarget01(float v) => barValue = Mathf.Clamp01(v);
    public void AddToTarget01(float d) => SetTarget01(barValue + d);

    void LateUpdate()
    {
        if (_runtimeMat == null || _graphic == null) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (dt <= 0f) return;
        if (dt > maxDt) dt = maxDt;

        // --- Detect target changes every frame (works even if barValue set directly) ---
        float clamped = Mathf.Clamp01(barValue);
        if (!Mathf.Approximately(clamped, _prevTarget01))
        {
            float delta = clamped - _prevTarget01;
            if (delta > 0f) { gainFlash = 1f; }                 // increase
            else            { loseFlash = 1f; _loseFlickerTime = 0f; } // decrease (start flicker)

            if (_prevTarget01 < 1f && clamped >= 1f)            // crossing to full
            {
                fullFlash = 1f;  // linear decay
            }
            _prevTarget01 = clamped;
        }
        barValue = clamped;

        // --- Smooth bars toward target ---
        _dispBase      = SmoothSnap01(_dispBase, barValue, baseSpeed, dt, epsilonSnap);
        _dispSecondary = SmoothSnap01(_dispSecondary, barValue, secondarySpeed, dt, epsilonSnap);

        // --- Flashes: decay while > 0 ---
        if (gainFlash > 0f && gainFlashFadePerSec > 0f)
            gainFlash = Mathf.Max(0f, gainFlash - gainFlashFadePerSec * dt);

        if (fullFlash > 0f && fullFlashFadePerSec > 0f)
            fullFlash = Mathf.Max(0f, fullFlash - fullFlashFadePerSec * dt);

        float loseOut = 0f;
        if (loseFlash > 0f)
        {
            _loseFlickerTime += dt;
            float flicker = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * loseFlashFlickerHz * _loseFlickerTime);
            loseOut = loseFlash * loseFlashFlickerAmp * flicker;
            if (loseFlashFadePerSec > 0f)
                loseFlash = Mathf.Max(0f, loseFlash - loseFlashFadePerSec * dt);
        }

        // Alpha from TARGET barValue
        float alpha = ComputeAlpha(barValue, alphaMin, alphaMax, alphaInverse);

        // --- Push if changed ---
        PushIfChanged(_barID,  _dispBase,      ref _lastBar);
        PushIfChanged(_bar2ID, _dispSecondary, ref _lastBar2);
        PushIfChanged(_gainID, gainFlash,      ref _lastGain);
        PushIfChanged(_fullID, fullFlash,      ref _lastFull);  // FullyFilled (linear)
        PushIfChanged(_loseID, loseOut,        ref _lastLose);  // Lose (7 Hz flicker)
        PushIfChanged(_alphaID,alpha,          ref _lastAlpha);
    }

    // --- Helpers ---
    private float SmoothSnap01(float current, float target, float speed, float dt, float eps)
    {
        if (speed > 0f) current += (target - current) * speed * dt;
        if (Mathf.Abs(target - current) <= eps) current = target;
        return Mathf.Clamp01(current);
    }

    private float ComputeAlpha(float v, float min, float max, bool inverse)
    {
        float denom = Mathf.Max(1e-6f, max - min);
        float t = Mathf.Clamp01((v - min) / denom);
        return inverse ? (1f - t) : t;
    }

    private void CacheIDs()
    {
        _barID   = string.IsNullOrEmpty(barParam)        ? -1 : Shader.PropertyToID(barParam);
        _bar2ID  = string.IsNullOrEmpty(bar2Param)       ? -1 : Shader.PropertyToID(bar2Param);
        _gainID  = string.IsNullOrEmpty(gainFlashParam)  ? -1 : Shader.PropertyToID(gainFlashParam);
        _fullID  = string.IsNullOrEmpty(fullFlashParam)  ? -1 : Shader.PropertyToID(fullFlashParam);
        _loseID  = string.IsNullOrEmpty(loseFlashParam)  ? -1 : Shader.PropertyToID(loseFlashParam);
        _alphaID = string.IsNullOrEmpty(valueAlphaParam) ? -1 : Shader.PropertyToID(valueAlphaParam);
    }

    private void PushAll(bool force)
    {
        if (_runtimeMat == null) return;

        float alpha = ComputeAlpha(barValue, alphaMin, alphaMax, alphaInverse);

        if (_barID   >= 0) _runtimeMat.SetFloat(_barID,   _dispBase);
        if (_bar2ID  >= 0) _runtimeMat.SetFloat(_bar2ID,  _dispSecondary);
        if (_gainID  >= 0) _runtimeMat.SetFloat(_gainID,  gainFlash);
        if (_fullID  >= 0) _runtimeMat.SetFloat(_fullID,  fullFlash);
        if (_loseID  >= 0) _runtimeMat.SetFloat(_loseID,  0f);
        if (_alphaID >= 0) _runtimeMat.SetFloat(_alphaID, alpha);

        _lastBar = _dispBase; _lastBar2 = _dispSecondary;
        _lastGain = gainFlash; _lastFull = fullFlash; _lastLose = 0f; _lastAlpha = alpha;
    }

    private void PushIfChanged(int id, float value, ref float last)
    {
        if (id < 0) return;
        if (!Mathf.Approximately(value, last))
        {
            _runtimeMat.SetFloat(id, value);
            last = value;
        }
    }

    private void EnsureRuntimeMaterial()
    {
        if (_runtimeMat != null) { ReapplyRuntimeMaterial(); return; }

        Material src = sourceMaterialOverride ? sourceMaterialOverride : (_graphic ? _graphic.material : null);
        if (src == null)
        {
            Debug.LogWarning($"{nameof(ShaderBar)}: No source material; using UI/Default.");
            src = new Material(Shader.Find("UI/Default"));
        }
        _runtimeMat = new Material(src) { name = $"{src.name} (Runtime ShaderBar Instance)" };
        if (_graphic) _graphic.material = _runtimeMat;
    }

    private void ReapplyRuntimeMaterial()
    {
        if (_graphic && _runtimeMat && _graphic.material != _runtimeMat)
            _graphic.material = _runtimeMat;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        barValue = Mathf.Clamp01(barValue);
        gainFlash = Mathf.Clamp01(gainFlash);
        fullFlash = Mathf.Clamp01(fullFlash);
        loseFlash = Mathf.Clamp01(loseFlash);
        if (!Application.isPlaying) return;
        CacheIDs();
    }
#endif

    public float DisplayBase01 => _dispBase;
    public float DisplaySecondary01 => _dispSecondary;
}
