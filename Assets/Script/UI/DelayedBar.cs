using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 延迟衰减条（虚条）：原条被扣时瞬间缩短，虚条立刻补上被扣部分，
/// 停止扣减 delaySeconds 秒后虚条从末端开始每秒平滑消退 fadeRate。
/// 原条恢复时盖过虚条（恢复部分不算受损，虚条末端跟随原条末端）。
///
/// 实现：虚条 = 一个 Image（Ghost Fill），直接控制其 anchorMax.x 表现宽度，
/// 不用第二个 Slider（同 GO 双 Slider / Slider 初始化时机问题会导致虚条不渲染）。
/// 挂载：条 GO（targetSlider = 原条 Slider），Awake 时创建 Ghost Fill 子物体。
/// </summary>
public class DelayedBar : MonoBehaviour
{
    [Header("虚条配置")]
    [Tooltip("虚条颜色（浅色半透明）")]
    [SerializeField] private Color ghostColor = new Color(1f, 0.6f, 0.6f, 0.5f);
    [Tooltip("停止扣减后延迟开始消退（秒）")]
    [SerializeField] private float delaySeconds = 1f;
    [Tooltip("虚条每秒消退量")]
    [SerializeField] private float fadeRate = 20f;

    [Tooltip("原条 Slider（本组件所在物体）")]
    [SerializeField] private Slider targetSlider;

    // 虚条 Image（Awake 动态创建）
    private RectTransform ghostFillRt;
    // 虚条末端值（0~max）
    private float ghostValue;
    // 最近一次扣减时间（重置消退延迟）
    private float lastDamageTime = -999f;

    /// <summary>
    /// 外部配置（PlayerUIManager 调用，值来自 PlayerConfig.xlsx PlayerAttribute）。
    /// </summary>
    public void Configure(float delay, float rate)
    {
        delaySeconds = delay;
        fadeRate = rate;
    }

    /// <summary>
    /// 设置虚条颜色（PlayerUIManager 在 AddComponent 后调用——Awake 已用默认色创建 Image，
    /// 需同时更新字段和已创建的 Image）。
    /// </summary>
    public void ApplyGhostColor(Color color)
    {
        ghostColor = color;
        if (ghostFillRt != null)
        {
            var img = ghostFillRt.GetComponent<Image>();
            if (img != null) img.color = color;
        }
    }

    private void Awake()
    {
        if (targetSlider == null) targetSlider = GetComponent<Slider>();
        if (targetSlider == null) return;

        var rt = GetComponent<RectTransform>();

        // 虚条层：Ghost Fill Area（全拉伸）→ Ghost Fill（Image）
        // 插到 Background 之后、原条 Fill Area 之前（渲染在原条下方）
        int insertIndex = 1;
        for (int i = 0; i < rt.childCount; i++)
        {
            if (rt.GetChild(i).name == "Fill Area") { insertIndex = i; break; }
        }

        var ghostAreaGo = new GameObject("Ghost Fill Area", typeof(RectTransform));
        ghostAreaGo.transform.SetParent(rt, false);
        ghostAreaGo.transform.SetSiblingIndex(insertIndex);
        var ghostAreaRt = ghostAreaGo.GetComponent<RectTransform>();
        ghostAreaRt.anchorMin = Vector2.zero;
        ghostAreaRt.anchorMax = Vector2.one;
        ghostAreaRt.offsetMin = Vector2.zero;
        ghostAreaRt.offsetMax = Vector2.zero;

        var ghostGo = new GameObject("Ghost Fill", typeof(RectTransform));
        ghostGo.transform.SetParent(ghostAreaGo.transform, false);
        ghostFillRt = ghostGo.GetComponent<RectTransform>();
        ghostFillRt.anchorMin = Vector2.zero;
        ghostFillRt.anchorMax = Vector2.one; // 初始全宽（满），由代码控制 anchorMax.x
        ghostFillRt.pivot = new Vector2(0.5f, 0.5f);
        ghostFillRt.offsetMin = Vector2.zero;
        ghostFillRt.offsetMax = Vector2.zero;
        var ghostImg = ghostGo.AddComponent<Image>();
        ghostImg.color = ghostColor;
        ghostImg.raycastTarget = false;

        ghostValue = targetSlider.value;
    }

    /// <summary>
    /// 属性变化回调（PlayerUIManager 调用）。
    /// </summary>
    public void SetCurrentValue(float current, float change)
    {
        if (targetSlider == null || ghostFillRt == null) return;

        // 原条始终显示当前值（瞬间）
        targetSlider.value = current;

        float max = targetSlider.maxValue;
        if (max <= 0f) max = 1f;

        if (change < 0f)
        {
            // 扣减：虚条补到"扣减前原条末端"（= current - change），与之前虚条连在一起
            float damagedEnd = current - change;
            ghostValue = Mathf.Max(ghostValue, damagedEnd);
            lastDamageTime = Time.time;
        }
        else if (current >= ghostValue)
        {
            // 恢复：原条盖过虚条 → 虚条末端跟随原条末端
            ghostValue = current;
        }

        UpdateGhostFill(max);
    }

    private void Update()
    {
        if (targetSlider == null || ghostFillRt == null) return;

        float max = targetSlider.maxValue;
        if (max <= 0f) max = 1f;
        float current = targetSlider.value;

        // 1 秒无扣减后，虚条从末端每秒消退 fadeRate，直到缩回原条末尾
        if (Time.time - lastDamageTime >= delaySeconds)
        {
            if (ghostValue > current)
            {
                ghostValue = Mathf.Max(current, ghostValue - fadeRate * Time.deltaTime);
            }
        }

        UpdateGhostFill(max);
    }

    /// <summary>
    /// 用 anchorMax.x 表现虚条末端（0~max → 0~1）。
    /// </summary>
    private void UpdateGhostFill(float max)
    {
        float ratio = Mathf.Clamp01(ghostValue / max);
        // 只改 anchorMax.x（水平宽度），y 保持全拉伸
        ghostFillRt.anchorMax = new Vector2(ratio, 1f);
    }
}
