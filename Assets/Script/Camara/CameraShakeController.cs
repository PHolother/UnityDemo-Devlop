using Cinemachine;
using UnityEngine;

/// <summary>
/// 命中镜头摇晃控制器
/// 通过修改 FreeLook 各 Rig 的 Composer TrackedObjectOffset 实现轻微摇晃
/// 使用 Perlin 噪声生成自然抖动，摇晃结束后平滑回正
/// 挂载位置：Player Camara
/// </summary>
public class CameraShakeController : MonoBehaviour
{
    [Header("摇晃参数")]
    [Tooltip("摇晃持续时间（秒）")]
    [SerializeField] private float shakeDuration = 0.25f;

    [Tooltip("摇晃幅度（偏移量，单位：米）")]
    [SerializeField] private float shakeIntensity = 0.15f;

    [Tooltip("摇晃频率（噪声采样速度）")]
    [SerializeField] private float shakeFrequency = 30f;

    [Tooltip("摇晃衰减曲线\nX轴：时间进度0-1\nY轴：剩余强度比例0-1\n推荐：起始1→末期0的递减曲线")]
    [SerializeField] private AnimationCurve shakeDecay;

    private CinemachineFreeLook freeLook;
    private CinemachineComposer[] composers;

    private Vector3[] baseOffsets;

    private float shakeTimer;
    private float currentDuration;
    private float currentIntensity;
    private float noiseSeed;

    private void Start()
    {
        freeLook = GetComponent<CinemachineFreeLook>();
        if (freeLook == null)
        {
            freeLook = GameObject.Find("Player Camara")?.GetComponent<CinemachineFreeLook>();
        }

        if (freeLook != null)
        {
            composers = new CinemachineComposer[3];
            baseOffsets = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                var rig = freeLook.GetRig(i);
                if (rig != null)
                {
                    composers[i] = rig.GetCinemachineComponent<CinemachineComposer>();
                    if (composers[i] != null)
                        baseOffsets[i] = composers[i].m_TrackedObjectOffset; // Awake 缓存=干净基准
                }
            }
        }

        if (shakeDecay == null || shakeDecay.length == 0)
        {
            shakeDecay = new AnimationCurve(
                new Keyframe(0f, 1f, -2f, -2f),
                new Keyframe(1f, 0f, 0f, 0f)
            );
        }
    }

    /// <summary>
    /// 触发镜头摇晃
    /// 震动期间再次命中：重置计时但保留噪声相位（noiseSeed 不重掷），
    /// 避免连续命中时噪声相位跳变造成视角高频撕裂感。
    /// </summary>
    public void TriggerShake(float intensityMultiplier = 1f)
    {
        if (composers == null) return;

        currentDuration = shakeDuration;
        currentIntensity = shakeIntensity * intensityMultiplier;
        shakeTimer = 0f;
        // 连续命中（震动尚未结束时再触发）保留相位平滑接续；全新震动才重掷种子
        if (shakeTimer >= currentDuration)
            noiseSeed = Random.Range(0f, 1000f);
    }

    private void Update()
    {
        if (composers == null || shakeTimer >= currentDuration) return;

        shakeTimer += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(shakeTimer / currentDuration);
        float decay = shakeDecay.Evaluate(progress);
        float amplitude = currentIntensity * decay;

        // Y 向幅度减半：垂直抖动比水平更易引起观感不适（视角"点头"感），水平保留打击感
        float noiseX = (Mathf.PerlinNoise(noiseSeed, shakeTimer * shakeFrequency * 0.01f) - 0.5f) * 2f;
        float noiseY = (Mathf.PerlinNoise(noiseSeed + 100f, shakeTimer * shakeFrequency * 0.01f) - 0.5f) * 2f;

        Vector3 shakeOffset = new Vector3(noiseX * amplitude, noiseY * amplitude * 0.4f, 0f);

        // 震动渲染：基准 offset（Awake 时缓存的干净值，Y 轴动态不影响它）+ Perlin 噪声
        for (int i = 0; i < 3; i++)
        {
            if (composers[i] != null)
                composers[i].m_TrackedObjectOffset = baseOffsets[i] + shakeOffset;
        }
    }
}
