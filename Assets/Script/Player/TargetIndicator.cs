using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 锁定目标指示圈（UI 版）：Screen Space - Overlay Canvas 渲染，永在最上层。
/// 生命周期由 TargetingSystem 管理：SetTarget 显示/换目标，Hide 隐藏。
/// 单例式复用同一个 UI 节点——不重复创建，未锁定时完全隐藏（不在屏幕任何位置显示）。
/// 强调动画：每次锁定瞬间 5× 大圆 0.3s 缩到目标大小 → 锁定期间持续可见（不淡出）。
/// </summary>
public class TargetIndicator : MonoBehaviour
{
    private static TargetIndicator instance;

    [Header("尺寸")]
    [Tooltip("圆圈对应的世界直径（米），近大远小")]
    [SerializeField] private float worldDiameter = 0.636f;

    [Header("强调动画")]
    [Tooltip("锁定瞬间起始放大倍数")]
    [SerializeField] private float emphasisScale = 5f;
    [Tooltip("从放大缩到目标大小的时长（秒）")]
    [SerializeField] private float emphasisDuration = 0.3f;

    private Transform target;
    private Image image;
    private Canvas canvas;
    private Camera mainCam;
    private RectTransform rect;

    private bool emphasising;
    private float emphasisTimer;
    private float currentScale = 1f;
    private bool visible;

    /// <summary>
    /// 获取单例指示圈（懒创建）：首次调用时建 Canvas 节点，之后复用。
    /// </summary>
    public static TargetIndicator GetOrCreate()
    {
        if (instance != null) return instance;
        var go = new GameObject("TargetIndicator", typeof(TargetIndicator));
        instance = go.GetComponent<TargetIndicator>();
        return instance;
    }

    /// <summary>
    /// 显示并绑定目标（每次锁定/换目标调用）。
    /// </summary>
    public void Show(Transform t)
    {
        target = t;
        emphasising = true;
        emphasisTimer = 0f;
        currentScale = emphasisScale;
        visible = true;
        if (image != null) image.enabled = true;
        gameObject.SetActive(true);
    }

    /// <summary>
    /// 隐藏指示圈（解锁/丢目标时调用）。图标不显示在屏幕任何位置。
    /// </summary>
    public void Hide()
    {
        target = null;
        visible = false;
        if (image != null) image.enabled = false;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        // 找/建 Overlay Canvas（复用现有 UI Canvas，没有则新建）
        canvas = null;
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                canvas = c;
                break;
            }
        }
        if (canvas == null)
        {
            var go = new GameObject("LockIndicatorCanvas", typeof(Canvas));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        var node = new GameObject("TargetIndicatorImage", typeof(Image));
        node.transform.SetParent(canvas.transform, false);
        image = node.GetComponent<Image>();
        rect = node.GetComponent<RectTransform>();
        // 锚点钉在左下角，anchoredPosition == UI 坐标（原点左下）
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);

        var sprite = Resources.Load<Sprite>("TargetCircle_Yellow");
        image.sprite = sprite;
        image.raycastTarget = false;
        // 荧光感：暖黄高亮
        image.color = new Color(1f, 0.92f, 0.35f, 0.95f);

        // 初始隐藏：未锁定时不渲染
        image.enabled = false;

        // 图标定位移到渲染前最后时刻：CinemachineBrain 在 LateUpdate 内更新相机姿态，
        // 普通脚本的 LateUpdate 与其顺序不确定——若先于 Brain 执行，图标用上一帧相机姿态
        // 计算，相机快速旋转时图标在屏幕上滞后甩动（抖动感）。onBeforeRender 在渲染前
        // 最终时刻触发，此时相机姿态已定，图标与画面完全同步。
        Application.onBeforeRender += OnBeforeRenderUpdate;
    }

    private void OnDestroy()
    {
        Application.onBeforeRender -= OnBeforeRenderUpdate;
    }

    private void OnBeforeRenderUpdate()
    {
        UpdateIndicator();
    }

    private void LateUpdate()
    {
        // 位置/朝向更新已移至 OnBeforeRenderUpdate（渲染前最终时刻，与 CinemachineBrain 同步）
        // 生命周期管理（Hide/Show）由 TargetingSystem 调用
    }

    private void UpdateIndicator()
    {
        if (!visible || target == null || image == null) return;

        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null || rect == null) return;

        // 锚点世界坐标 → 屏幕坐标
        Vector3 screenPos = mainCam.WorldToScreenPoint(target.position);
        if (screenPos.z <= 0)
        {
            image.enabled = false; // 目标在背后
            return;
        }
        image.enabled = true;

        // Overlay Canvas + CanvasScaler：UI 坐标系 = 屏幕像素 / scaleFactor
        rect.anchoredPosition = new Vector2(screenPos.x, screenPos.y) / canvas.scaleFactor;

        // 强调动画：5× → 目标大小（0.3s ease-out）
        if (emphasising)
        {
            emphasisTimer += Time.deltaTime;
            float t = Mathf.Clamp01(emphasisTimer / emphasisDuration);
            float e = 1f - (1f - t) * (1f - t) * (1f - t);
            currentScale = Mathf.Lerp(emphasisScale, 1f, e);
            if (t >= 1f) emphasising = false;
        }
        else
        {
            currentScale = 1f;
        }

        // 锁定期间图标持续存在（不淡出）——仅 Hide()（解锁/丢目标）时消失。
        // 原 3s 后淡出逻辑已移除（用户需求：锁定图标持续可见）。
        float alpha = 0.95f;
        var col = image.color;
        col.a = alpha;
        image.color = col;

        // 尺寸：世界直径换屏幕像素（近大远小）
        float dist = Vector3.Distance(mainCam.transform.position, target.position);
        float focal = mainCam.pixelHeight / (2f * mainCam.fieldOfView * Mathf.Deg2Rad);
        float px = worldDiameter / Mathf.Max(dist, 0.5f) * focal * currentScale / canvas.scaleFactor;
        rect.sizeDelta = new Vector2(px, px);
    }
}
