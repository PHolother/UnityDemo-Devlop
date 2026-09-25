using Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCameraReset : MonoBehaviour
{
    private CinemachineFreeLook freeLook;
    private Transform playerTransform;
    private TargetingSystem targetingSystem;
    private Camera mainCam;

    private bool isResetting;
    private float resetTimer;
    private float startX, startY, targetX;

    [SerializeField] private float targetY = 0.5f;
    [SerializeField] private float resetSpeed = 8f;
    [SerializeField] private InputActionReference lookAction;

    [Header("鼠标输入")]
    [Tooltip("鼠标视角低通滤波系数（越大越跟手，越小越平滑。原始 delta 高频噪声会变成视角抖动）")]
    [SerializeField] private float lookSmooth = 20f;
    [Tooltip("判定玩家正在转视角的输入阈值（用平滑后的值判定，免疫原始噪声）")]
    [SerializeField] private float turnThreshold = 999f;

    [Header("锁定取景（单一修正器）")]
    [Tooltip("诊断开关：完全禁用锁定取景修正（排查抖动源用）")]
    [SerializeField] private bool enableFraming = true;
    [Tooltip("屏幕坐标平滑系数（越大越平滑）")]
    [SerializeField] private float screenSmooth = 12f;
    // 垂直取景修正已移除（m_YAxis 联动导致视角突然向下弹回，垂直交还玩家鼠标控制）

    [Header("锁定范围矩形（敌人不远离屏幕中心）")]
    [Tooltip("启用锁定范围钳制")]
    [SerializeField] private bool enableHardClamp = true;
    [Tooltip("范围矩形半宽（相对半屏宽，0.25=屏幕中心左右各 1/4 屏宽）")]
    [SerializeField] private float clampWidth = 0.25f;
    [Tooltip("范围矩形半高（相对半屏高）")]
    [SerializeField] private float clampHeight = 0.25f;
    [Tooltip("越界拉回角速度（度/秒）。连续低速拉回（无冷却无脉冲，避免突变）")]
    [SerializeField] private float clampSpeed = 500f;
    [Tooltip("玩家手动转视角后，BOSS 回到此比例内即恢复钳制（相对半屏，大于 clampWidth/Height 形成滞回）")]
    [SerializeField] private float reenterWidth = 0.4f;
    [Tooltip("垂直方向恢复阈值")]
    [SerializeField] private float reenterHeight = 0.4f;
    [Tooltip("锁定时 Y 轴（俯仰）下限：0=相机贴地，0.35=相机高度约 0.9m。防鼠标上滑把相机压到玩家脚下")]
    [SerializeField] private float yMinLocked = 0.35f;
    [Tooltip("锁定时 Y 轴（俯仰）上限：1=正上方（顶 rig radius=0，万向锁区），0.85=限制抬头最大角度防巨幅旋转")]
    [SerializeField] private float yMaxLocked = 0.85f;
    [Tooltip("手动转视角停手后，取景修正恢复前的延迟（秒），让手动输入消散")]
    [SerializeField] private float manualReleaseDelay = 0.3f;
    [Tooltip("目标点渐进跟随速度（越大目标点越快移到边界内收点）")]
    [SerializeField] private float targetBlendSpeed = 50f;

    [Header("调试框")]
    [Tooltip("显示锁定范围调试矩形（Game 视图中心：红=实际生效范围，绿=观察框）")]
    [SerializeField] private bool showDebugFrame = true;
    [Tooltip("调试矩形半宽（相对半屏宽，独立于 clampWidth，用于规划范围）")]
    [SerializeField] private float debugFrameWidth = 0.25f;
    [Tooltip("调试矩形半高（相对半屏高）")]
    [SerializeField] private float debugFrameHeight = 0.25f;

    private Vector2 smoothedScreen;
    private Vector2 smoothedLook; // 鼠标输入低通缓存（供取景修正判定转视角）
    private bool framingInitialized;
    private Transform lastFramedTarget;
    private bool manualOverride; // 玩家手动转视角时的钳制挂起标志
    private float manualOverrideUntil; // 挂起解除时间（停手后延迟恢复）
    private float framingTargetX; // 当前取景目标点（平滑渐进）
    private CameraFollowProxy followProxy;

    private void Start()
    {
        freeLook = GetComponent<CinemachineFreeLook>();
        playerTransform = GameObject.FindGameObjectWithTag("Player").transform;
        targetingSystem = playerTransform.GetComponent<TargetingSystem>();
        if (freeLook.Follow != null)
            followProxy = freeLook.Follow.GetComponent<CameraFollowProxy>();

        // 启用鼠标 Look 输入：原本由 CinemachineInputProvider(AutoEnableInputs) 启用，
        // 禁用 InputProvider 后需自行启用，否则 ReadValue 恒 0、鼠标无法转视角。
        if (lookAction != null && lookAction.action != null)
            lookAction.action.Enable();

        // 进 Play 时初始化俯仰轴到 targetY：避免从上次会话序列化值(如0.8)平滑收敛
        // 造成进 Play 瞬间 0.5 秒的相机大幅俯仰翻转（用户感知为抖动/俯冲）。
        freeLook.m_YAxis.Value = targetY;
    }

    void Update()
    {
        if (targetingSystem == null)
            GetMiddleButton();
        HandleCameraControl();
    }

    private void LateUpdate()
    {
        if (isResetting) return;
        if (targetingSystem == null || !targetingSystem.HasTarget) return;

        // 取景修正始终工作（不再因 Proxy 滞后暂停——Proxy 滞后本身不影响修正器读数，
        // 修正器读的是 BOSS 投影位置而非 Proxy 位置，Proxy 滞后导致的投影漂移
        // 正是需要修正的内容，暂停反而让偏离无人管理）
        bool proxyMoving = false;
        if (!enableFraming) return; // 诊断开关：排查抖动源

        // 单一修正器：手动优先 → 计算目标点 → P 控制拉向目标
        ApplyFraming(proxyMoving);

        // A2：锁定时 Y 轴钳下限（防鼠标上滑把相机压到玩家脚下），
        // 但 BOSS 投影完全出屏时豁免（玩家自由调俯仰找回 BOSS）
        ApplyYawClamp();
    }

    /// <summary>
    /// 锁定时的 Y 轴（俯仰）下限钳制：
    /// 鼠标上滑（delta.y 为正 + Invert=1）会把 Y 轴推到 0（底部 rig height=0）→ 相机贴地只看到玩家胯部。
    /// 锁定时把 m_YAxis 钳在 yMinLocked（0.35，相机高度约 0.9m，可低视角但不贴地）。
    /// BOSS 投影完全出屏（|rawX|>1 或 |rawY|>1）时豁免——玩家自由调俯仰找回 BOSS。
    /// </summary>
    private void ApplyYawClamp()
    {
        if (mainCam == null)
            mainCam = Camera.main;
        if (mainCam == null) return;

        Vector3 enemyPos = targetingSystem.LockPoint != null
            ? targetingSystem.LockPoint.position
            : targetingSystem.CurrentTarget.position;
        Vector3 screenPos = mainCam.WorldToScreenPoint(enemyPos);
        bool onScreen = screenPos.z > 0
            && Mathf.Abs((screenPos.x / Screen.width) * 2f - 1f) <= 1f
            && Mathf.Abs((screenPos.y / Screen.height) * 2f - 1f) <= 1f;

        if (onScreen && freeLook.m_YAxis.Value < yMinLocked)
            freeLook.m_YAxis.Value = yMinLocked;
    }

    /// <summary>
    /// 锁定取景单一修正器：
    /// 目标点动态：BOSS 在矩形内=屏幕中心；越界=该轴边界内收点。
    /// 三个防抖核心：
    /// 1) 手动优先：玩家转视角时完全让位，且停手后 0.3s 恢复延迟（让手动输入充分消散，
    ///    消除"停手瞬间修正器全量接管"的回摆大抖）；
    /// 2) 目标点本身平滑过渡：BOSS 从左侧出界扫到右侧出界时，targetX 从 -0.2 渐进到 +0.2
    ///    （而非瞬间跳变），P 控制器不会产生方向突变的修正；
    /// 3) 目标偏差驱动：修正量与到目标的偏差成正比，无偏差无修正。
    /// 只做水平修正（垂直归玩家鼠标）。
    /// </summary>
    private void ApplyFraming(bool proxyMoving)
    {
        if (mainCam == null)
            mainCam = Camera.main;
        if (mainCam == null) return;

        Vector3 enemyPos = targetingSystem.LockPoint != null
            ? targetingSystem.LockPoint.position
            : targetingSystem.CurrentTarget.position;
        Vector3 screenPos = mainCam.WorldToScreenPoint(enemyPos);
        if (screenPos.z <= 0) return;

        // 归一化到 [-1, 1]，中心为 0
        float rawX = (screenPos.x / Screen.width) * 2f - 1f;
        float rawY = (screenPos.y / Screen.height) * 2f - 1f;

        // 玩家正在转视角：修正完全让位（手动优先）。
        // 用平滑后的输入判定（免疫原始噪声）；停手后 0.3s 内仍挂起（输入消散期）
        bool playerTurning = smoothedLook.magnitude > turnThreshold;
        if (playerTurning)
        {
            manualOverride = true;
            manualOverrideUntil = Time.time + manualReleaseDelay;
        }
        if (manualOverride)
        {
            smoothedScreen = new Vector2(rawX, rawY); // 挂起期间持续追踪，恢复时不突跳
            if (Time.time < manualOverrideUntil)
                return;
            // 消散期结束：若 BOSS 已回到 reenter 矩形内，直接恢复；否则保持挂起直到进入
            if (Mathf.Abs(rawX) > reenterWidth || Mathf.Abs(rawY) > reenterHeight)
                return;
            manualOverride = false;
        }

        // 指数平滑，消除帧间抖动；首次锁定或换目标时用 raw 直接初始化（不突跳）
        if (!framingInitialized || targetingSystem.CurrentTarget != lastFramedTarget)
        {
            smoothedScreen = new Vector2(rawX, rawY);
            framingInitialized = true;
            lastFramedTarget = targetingSystem.CurrentTarget;
        }
        float alpha = 1f - Mathf.Exp(-screenSmooth * Time.deltaTime);
        smoothedScreen = Vector2.Lerp(smoothedScreen, new Vector2(rawX, rawY), alpha);

        // —— 计算目标点（目标点本身平滑过渡，防方向突变）——
        // BOSS 在矩形内：目标 = 屏幕中心（0）
        // BOSS 越出矩形：目标 = 该轴边界内收 margin
        // targetX 每帧向"理想目标"Lerp（targetBlendSpeed），快速横扫时目标渐进跟随而非跳变
        const float margin = 0.05f;
        float idealTargetX = 0f;
        if (enableHardClamp && Mathf.Abs(smoothedScreen.x) > clampWidth)
            idealTargetX = Mathf.Sign(smoothedScreen.x) * (clampWidth - margin);
        float blendA = 1f - Mathf.Exp(-targetBlendSpeed * Time.deltaTime);
        framingTargetX = Mathf.Lerp(framingTargetX, idealTargetX, blendA);

        // 距离目标点的偏差（P 控制器输入）
        float xError = smoothedScreen.x - framingTargetX;

        // 轴级死区：xError 过零变号时（BOSS 投影围绕目标点呼吸），±0.06°/帧的交替修正
        // 表现为高频小幅抖动（向后走时 BOSS 投影持续向中心收敛，rawX 围绕目标反复穿越）。
        // 轴级死区让过零附近完全不修正，越界远离后修正量平滑恢复。
        float xDead = 0.05f;
        if (Mathf.Abs(xError) < xDead) return;

        if (Mathf.Abs(xError) < 0.01f) return;

        // 跑动中（Proxy 在动）速度减半，避免与 Proxy 移动打架
        float speed = clampSpeed * (proxyMoving ? 0.5f : 1f);
        // 修正量随偏差连续变化，且在死区边缘从 0 平滑起坡（避免死区边界突变）
        float ramp = Mathf.Clamp01((Mathf.Abs(xError) - xDead) / xDead);
        float xCorrection = xError * 2f * speed * Time.deltaTime * ramp;
        // 单帧限幅：Editor 卡顿帧防瞬转。允许较大单帧修正——越界强力拉回要在 0.2s 内完成，
        // 限幅太小（旧 2.5°）会拖慢拉回速度；8° 在 60fps 下单帧转 8° 视觉仍可接受（强拉场景）。
        xCorrection = Mathf.Clamp(xCorrection, -8f, 8f);
        freeLook.m_XAxis.Value += xCorrection;
    }

    private void GetMiddleButton()
    {
        if (Mouse.current?.middleButton.wasPressedThisFrame == true)
        {
            StartResetCamera();
        }
    }

    private void StartResetCamera()
    {
        var proxy = freeLook.Follow != null ? freeLook.Follow.GetComponent<CameraFollowProxy>() : null;
        if (proxy != null)
            proxy.SnapToPlayer();

        targetX = CalculateTargetX();
        isResetting = true;
        resetTimer = 0f;
        startX = freeLook.m_XAxis.Value;
        startY = freeLook.m_YAxis.Value;
    }

    private void UpdateResetCamera()
    {
        resetTimer += Time.deltaTime * resetSpeed;

        freeLook.m_XAxis.Value = Mathf.Lerp(startX, targetX, resetTimer);
        freeLook.m_YAxis.Value = Mathf.Lerp(startY, targetY, resetTimer);

        if (resetTimer >= 1f)
        {
            isResetting = false;
            freeLook.m_XAxis.Value = targetX;
            freeLook.m_YAxis.Value = targetY;
        }
    }

    private void HandleCameraControl()
    {
        if (!isResetting)
        {
            // 鼠标输入由 CinemachineInputProvider 接管（legacy 轴名 + InputProvider 机制，
            // 项目 activeInputHandler=1 纯新 InputSystem，m_InputAxisValue 在此模式下无效）。
            // 这里只做两件事：
            // 1) 低通追踪 smoothedLook——供取景修正(ApplyFraming)判定"玩家正在转视角"
            // 2) 锁定钳制：锁定时 m_YAxis 钳在 [yMinLocked, yMaxLocked]（防贴地/万向锁）
            //    用低通值判定是否正在转视角，避免与 Cinemachine 内部每帧竞争。
            var lookDelta = lookAction.action.ReadValue<Vector2>();
            float alpha = 1f - Mathf.Exp(-lookSmooth * Time.deltaTime);
            smoothedLook = Vector2.Lerp(smoothedLook, lookDelta, alpha);

            bool turning = smoothedLook.sqrMagnitude > 0.0025f; // 阈值 0.05
            if (turning)
                freeLook.m_YAxis.Value = Mathf.Clamp(freeLook.m_YAxis.Value, yMinLocked, yMaxLocked);
        }
        else
        {
            UpdateResetCamera();
        }
    }

    public void ResetCamera()
    {
        StartResetCamera();
    }

    /// <summary>
    /// 调试用：当前平滑后的鼠标垂直输入（探针读取）。
    /// </summary>
    public float GetSmoothedLookY() => smoothedLook.y;

    private float CalculateTargetX()
    {
        var playerBackDirection = -playerTransform.forward;
        playerBackDirection.y = 0;
        playerBackDirection.Normalize();

        var cameraDirection = freeLook.transform.position - playerTransform.position;
        cameraDirection.y = 0;
        cameraDirection.Normalize();

        var deltaAngle = Vector3.SignedAngle(cameraDirection, playerBackDirection, Vector3.up);

        var tempAngle = freeLook.m_XAxis.Value + deltaAngle;

        while (tempAngle > 180) tempAngle -= 360;
        while (tempAngle < -180) tempAngle += 360;

        return tempAngle;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 调试框：Game 视图屏幕中心画锁定范围矩形。
    /// 红框 = 硬钳制实际生效范围（clampWidth/Height）；
    /// 绿框 = 观察框（debugFrameWidth/Height，手动调参数规划范围）。
    /// 仅编辑器 Play 模式显示。
    /// </summary>
    private void OnGUI()
    {
        if (!showDebugFrame || !Application.isPlaying) return;

        float cx = Screen.width / 2f;
        float cy = Screen.height / 2f;
        float halfW = Screen.width / 2f;
        float halfH = Screen.height / 2f;

        // 红框：实际生效的钳制范围
        drawRectOutline(cx, cy, clampWidth * halfW, clampHeight * halfH, new Color(1f, 0.25f, 0.2f, 0.85f));
        // 绿框：观察/规划用
        drawRectOutline(cx, cy, debugFrameWidth * halfW, debugFrameHeight * halfH, new Color(0.3f, 1f, 0.4f, 0.85f));
        // 中心十字
        drawRectFilled(cx - 1, cy - 10, 2, 20, new Color(1f, 1f, 1f, 0.6f));
        drawRectFilled(cx - 10, cy - 1, 20, 2, new Color(1f, 1f, 1f, 0.6f));
    }

    private static Texture2D whiteTex;
    private static Texture2D WhiteTex
    {
        get
        {
            if (whiteTex == null)
            {
                whiteTex = new Texture2D(1, 1);
                whiteTex.SetPixel(0, 0, Color.white);
                whiteTex.Apply();
            }
            return whiteTex;
        }
    }

    private static void drawRectOutline(float cx, float cy, float halfW, float halfH, Color color)
    {
        drawRectFilled(cx - halfW, cy - halfH, halfW * 2f, 2f, color);          // 上
        drawRectFilled(cx - halfW, cy + halfH - 2f, halfW * 2f, 2f, color);     // 下
        drawRectFilled(cx - halfW, cy - halfH, 2f, halfH * 2f, color);          // 左
        drawRectFilled(cx + halfW - 2f, cy - halfH, 2f, halfH * 2f, color);     // 右
    }

    private static void drawRectFilled(float x, float y, float w, float h, Color color)
    {
        var prevColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(x, y, w, h), WhiteTex);
        GUI.color = prevColor;
    }
#endif
}
