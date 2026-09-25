using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 相机跟随代理 — 实现速度感知的水平位置滞后
/// Proxy 仅在 XZ 平面滞后，Y 轴直接同步玩家高度
/// FreeLook.Follow 和 LookAt 均设为此 GameObject
/// </summary>
public class CameraFollowProxy : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("玩家 Transform（自动通过 Tag 查找）")]
    [SerializeField] private Transform playerTransform;

    [Tooltip("移动输入 Action（检测玩家是否在移动）")]
    [SerializeField] private InputActionReference moveAction;

    [Header("滞后参数")]

    [Tooltip("判定玩家停止移动的输入阈值")]
    [Range(0f, 0.5f)]
    [SerializeField] private float moveDeadzone = 0.1f;

    [Tooltip("单帧位移超过此值判定为瞬移/闪避，直接吸附")]
    [SerializeField] private float teleportThreshold = 3f;

    private Vector3 lastPlayerXZ;
    private Transform cameraTransform;
    private Vector3 lastCamFwd;

    private void Start()
    {
        if (playerTransform == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                playerTransform = player.transform;
        }

        if (moveAction != null && moveAction.action != null)
            moveAction.action.Enable();

        if (playerTransform != null)
        {
            transform.position = playerTransform.position;
            lastPlayerXZ = Flatten(playerTransform.position);
        }

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        if (cameraTransform != null)
        {
            lastCamFwd = Flatten(cameraTransform.forward);
            // proxySideWorld 语义为"相对玩家的侧向偏移"，静止无偏移 → 初始化为 0
            proxySideWorld = 0f;
        }
    }

    private void Update()
    {
        UpdateProxyPosition();
    }

    // 原为 LateUpdate：与 CinemachineBrain 的 LateUpdate 同阶段，执行顺序不确定——
    // Brain 若先执行，相机永远取上一帧的 Proxy 位置=高频抖动（锁定取景修正叠加后更明显）。
    // 挪到 Update：每帧时序固定为 Update(Proxy 移动) → LateUpdate(Brain 读最新位置)。
    private void UpdateProxyPosition()
    {
        if (playerTransform == null) return;

        // 玩家位置（Proxy 与 Brain 都读同一位置；振荡根因已定位为
        // CinemachineCollider 把玩家当障碍物瞬移相机，非 root motion 时序）
        Vector3 currentPlayerXZ = Flatten(playerTransform.position);

        // 瞬移/闪避检测
        float frameMove = Vector3.Distance(currentPlayerXZ, lastPlayerXZ);
        if (frameMove > teleportThreshold)
        {
            SnapToPlayer();
            lastPlayerXZ = currentPlayerXZ;
            return;
        }

        bool isMoving = IsPlayerMoving();

        // 玩家帧位移（低通平滑：消除地面碰撞/鼠标残余的高频分量，这是之前向后走抖动的根源之一）
        Vector3 rawDelta = currentPlayerXZ - Flatten(new Vector3(lastPlayerXZ.x, 0, lastPlayerXZ.z));
        float deltaAlpha = 1f - Mathf.Exp(-deltaSmooth * Time.deltaTime);
        smoothedDelta = Vector2.Lerp(smoothedDelta, new Vector2(rawDelta.x, rawDelta.y), deltaAlpha);
        Vector3 playerDelta = new Vector3(smoothedDelta.x, 0, smoothedDelta.y);

        if (cameraTransform == null)
        {
            if (Camera.main != null) cameraTransform = Camera.main.transform;
            else return;
        }

        // ===== 分轴传导模型 v4：2D 偏移向量 =====
        // offsetVec 是相对相机朝向的 2D 偏移（X=侧向，Y=前后）。
        // 世界偏移 = 相机右×侧向 + 相机后×前后。
        //
        // 前后轴（Y）：
        //   - 后走：偏移增长（玩家在相机前，远离屏幕中心）到 offsetHoldMax
        //   - 前冲：偏移快速收缩到 0 → Proxy=player → 相机紧贴玩家 → 玩家比例不变
        //   - 停止：recenterSpeed 缓慢回正
        // 侧向轴（X）：
        //   - 按下 A/D（侧移输入）：S 曲线平滑建立（sideBuildTime 0.2s 抵达边缘），
        //     方向与玩家移动方向相反（Proxy 偏移 = 玩家屏幕反向偏移）——
        //     玩家向左走 → Proxy 向右偏 → 玩家显示在屏幕左侧 ✓
        //   - 反向切换（A→D）：一次 S 曲线跨中心到对侧（sideCrossTime 0.3s）
        //   - 未按 A/D（纯前进/后退/停止）：easeOut 回正（先快后慢，recenterTime）——
        //     前进/后退不立刻清侧移，按比例依然远离屏幕（位移残值不参与判定，输入才是门控）
        //
        // 相机朝向变化时（取景修正/手动转视角），offsetVec 跟随旋转——
        // 保持"相对相机"的语义，否则相机一转偏移方向突变 = Proxy 跳变。
        Vector3 camRight = Flatten(cameraTransform.right).normalized;
        Vector3 camBack = Flatten(-cameraTransform.forward).normalized;

        // 偏移向量跟随相机旋转（XZ 平面增量旋转）
        Vector3 camFwdNow = Flatten(cameraTransform.forward);
        if (lastCamFwd.sqrMagnitude > 0.001f && camFwdNow.sqrMagnitude > 0.001f)
        {
            Quaternion rotDelta = Quaternion.FromToRotation(lastCamFwd.normalized, camFwdNow.normalized);
            Vector3 rotated = rotDelta * new Vector3(offsetVec.x, 0f, offsetVec.y);
            offsetVec = new Vector2(rotated.x, rotated.z);
        }
        lastCamFwd = camFwdNow;

        float sideComponent = Vector3.Dot(playerDelta, camRight);   // 侧向分量（+右 -左）
        float fwdComponent = Vector3.Dot(playerDelta, -camBack);    // 前后分量（+前 -后）

        // 侧移输入判定：用玩家实际按下的水平输入（A/D），而不是位移分量。
        // 位移分量（smoothedDelta）是低通后的，侧移停下后残留左/右分量数帧，
        // 此时按 W 前进会被误判为"侧移主导"→ 侧向偏移不清除 → 前进也偏左/右（本 BUG）。
        // 输入方向是离散的：按了 A/D 才算侧移，只按 W/S 则侧向偏移立即清除。
        Vector2 moveInput = moveAction != null && moveAction.action != null
            ? moveAction.action.ReadValue<Vector2>()
            : Vector2.zero;
        bool sideInput = Mathf.Abs(moveInput.x) > moveDeadzone;
        bool fwdInput = Mathf.Abs(moveInput.y) > moveDeadzone;

        // 前后偏移：后走增长、前冲快速收缩、停止回正
        if (fwdComponent < 0f) // 向相机方向移动（后走）
            offsetVec.y = Mathf.Min(offsetVec.y - fwdComponent, offsetHoldMax);
        else if (isMoving) // 前冲/前进：快速收缩到 0（Proxy=player 紧贴，比例不变）
            offsetVec.y = Mathf.MoveTowards(offsetVec.y, 0f, offsetShrinkSpeedMoving * Time.deltaTime);
        else // 停止：缓慢回正
            offsetVec.y = Mathf.MoveTowards(offsetVec.y, 0f, recenterSpeed * Time.deltaTime);

        // 侧向偏移：仅按下 A/D 时建立（方向取反：Proxy 偏移 = 玩家屏幕反向偏移）；
        // 未按 A/D 时（含前进/后退移动中）一律回正——
        // 玩家按前进/后退时侧移偏移不立刻清除，而是按比例依然远离屏幕、
        // 以与一般侧移一致的速度慢慢回正
        // 实现：S 曲线时间插值（smoothstep）——起止零速、中间平滑加减速，
        // 无匀速 MoveTowards 的速度阶跃（突进感来源）。每次目标变化重开运动段。
        if (sideInput)
        {
            // 玩家向左走（输入 x<0）→ Proxy 向右偏（offsetVec.x>0）→ 玩家在屏幕左侧 ✓
            float sideDir = moveInput.x > 0f ? -1f : 1f; // 玩家右走→偏移为负（玩家屏幕右偏）
            float target = sideDir * maxSideOffset;

            // 首次按下：记录按下瞬间的偏移起点（判断本次是否反向跨中心）
            if (!sideInputPrev)
                sidePressStartX = offsetVec.x;

            // 从起点到目标的距离决定时长：
            //   - 跨中心（|target - sidePressStartX| > maxSideOffset，即反向）→ sideCrossTime
            //   - 同向/初始（≤ maxSideOffset）→ sideBuildTime
            float totalDist = Mathf.Abs(target - sidePressStartX);
            float duration = totalDist > maxSideOffset ? sideCrossTime : sideBuildTime;
            bool targetChanged = Mathf.Abs(sideMoveTargetX - target) > 0.001f;

            if (!sideInputPrev || targetChanged)
                StartSideMove(target, duration);
            UpdateSideMove();
        }
        else
        {
            // 回正：两段式——停止侧移瞬间立刻开始，先快速回正（无延迟），
            // 到"总路程 65% 处"（基点）后转缓慢回正到 0。
            // 特殊判定：松开侧移后 sideFwdTransitionWindow(0.2s) 内按了前/后键
            // （左→左前→前的顺滑过渡），回正全程用慢速——不快速拉回镜头。
            if (sideInputPrev)
            {
                sideRecenterBase = Mathf.Abs(offsetVec.x); // 刚停止，记录基准
                sideReleaseTime = Time.time;
            }
            bool fwdTransition = Time.time - sideReleaseTime <= sideFwdTransitionWindow && fwdInput;
            float pivot = sideRecenterBase * 0.65f; // 基点 = 总路程 65%
            if (fwdTransition)
            {
                // 侧移→前/后衔接：全程慢速回正（顺滑过渡，不快速拉回）
                offsetVec.x = Mathf.MoveTowards(offsetVec.x, 0f, recenterSpeed * Time.deltaTime);
            }
            else if (Mathf.Abs(offsetVec.x) > pivot)
                offsetVec.x = Mathf.MoveTowards(offsetVec.x, 0f, recenterFastSpeed * Time.deltaTime);
            else
                offsetVec.x = Mathf.MoveTowards(offsetVec.x, 0f, recenterSpeed * Time.deltaTime);
        }
        sideInputPrev = sideInput;

        // 侧向慢追：平滑的是"相对玩家的侧向偏移"，而非相机坐标系下的绝对侧向坐标。
        // 旧实现 proxySideWorld 追 targetSide=Dot(targetXZ, camRight)（含玩家世界坐标的绝对投影），
        // 只旋转视角（玩家静止、offsetVec≈0）时 camRight 随之旋转，该绝对投影会在
        // ±|玩家世界坐标| 区间大幅摆动，低通量跟不上 → Proxy 绕着玩家甩动、再被 maxClampDist
        // 拽回 → 表现为玩家静止旋转视角时的"碰撞感/卡顿"。
        // 改为追相对侧向偏移 offsetVec.x 后：单独旋转相机（玩家不动）不改变被平滑量 → 旋转无抖；
        // 玩家侧移时相对偏移仍以 sideProxySmooth 慢速建立 → 屏幕滑边观感不变。
        // 前后向保持直接跟随（相机跟紧玩家前进/后退不出画）。
        float targetSideRel = offsetVec.x;
        float sideAlpha = 1f - Mathf.Exp(-(sideInput ? sideProxySmooth : sideProxyRecenterSmooth) * Time.deltaTime);
        proxySideWorld = Mathf.Lerp(proxySideWorld, targetSideRel, sideAlpha);
        Vector3 newProxyXZ = currentPlayerXZ + camRight * proxySideWorld + camBack * offsetVec.y;

        // 全方向钳制：Proxy 与玩家的水平距离永不超过 maxClampDist
        Vector3 finalOffset = newProxyXZ - currentPlayerXZ;
        if (finalOffset.magnitude > maxClampDist)
            newProxyXZ = currentPlayerXZ + finalOffset.normalized * maxClampDist;

        float newY = SmoothProxyY(playerTransform.position.y);
        transform.position = new Vector3(newProxyXZ.x, newY, newProxyXZ.z);

        lastPlayerXZ = currentPlayerXZ;
    }

    [Header("分轴传导模型")]
    [Tooltip("全方向钳制距离（米）：Proxy 与玩家水平距离的上限")]
    [SerializeField] private float maxClampDist = 0.8f;
    [Tooltip("玩家帧位移低通系数（越大越紧跟原始移动，越小越平滑。8=充分消除向后走残余高频）")]
    [SerializeField] private float deltaSmooth = 12f;
    [Tooltip("停止移动后镜头缓慢回正的速度（越大回正越快）")]
    [SerializeField] private float recenterSpeed = 0.5f;

    private Vector2 smoothedDelta; // 平滑后的玩家帧位移

    // 兼容旧接口（IsProxyMoving 供取景修正暂停判定）

    [Header("统一偏移模型")]
    [Tooltip("后退偏移上限（米）：向后跑时人物偏离屏幕中心的最大距离")]
    [SerializeField] private float offsetHoldMax = 0.6f;
    [Tooltip("前冲偏移收缩速度（米/秒）：前冲时 Proxy 快速贴回玩家，比例不变")]
    [SerializeField] private float offsetShrinkSpeedMoving = 10f;
    [Tooltip("侧向偏移上限（米）：侧移时人物偏离屏幕中心的最大距离")]
    [SerializeField] private float maxSideOffset = 0.6f;
    [Tooltip("侧移建立时长（秒）：按下 A/D 后，S 曲线平滑抵达同侧边缘（起止零速，无突进）")]
    [SerializeField] private float sideBuildTime = 0.2f;
    [Tooltip("反向切换时长（秒）：从一侧突然转向另一侧时，一次 S 曲线平滑跨过中心到对侧边缘")]
    [SerializeField] private float sideCrossTime = 0.3f;
    [Tooltip("停止侧移后回正时长（秒）：easeOut 先快后慢回到中心（无两段速度突变）")]
    [SerializeField] private float recenterTime = 0.6f;
    [Tooltip("侧向偏移单帧最大变化（米/帧）：硬上限钳制，任何分支/重启都不产生瞬间跳变（突进感根源）")]
    [SerializeField] private float maxSideDeltaPerFrame = 0.03f;
    [Tooltip("侧向 Proxy 慢速平滑系数：相机横移缓慢平滑追玩家侧移（越小越慢；玩家屏幕偏移仍快——玩家快滑到侧边，相机缓慢横移）")]
    [SerializeField] private float sideProxySmooth = 2f;
    [Tooltip("回正时侧向 Proxy 平滑系数：设高值让 Proxy 直接跟随偏移回正（无'延迟一会才回正'；回正节奏由两段式 MoveTowards 控制）")]
    [SerializeField] private float sideProxyRecenterSmooth = 30f;
    [Tooltip("侧移→前/后衔接窗口（秒）：松开侧移后此时间内按了前/后键，回正全程用慢速（左→左前→前顺滑过渡，不快速拉回镜头）")]
    [SerializeField] private float sideFwdTransitionWindow = 0.2f;
    [Tooltip("回正快段速度（米/秒）：保留字段（旧两段式已弃用，仅作参考）")]
    [SerializeField] private float recenterFastSpeed = 2f;

    private Vector2 offsetVec; // 当前偏移（X=侧向，Y=前后，相对相机朝向）

    // 侧向平滑运动段（时间插值）：记录起点/终点/开始时间/时长/曲线类型
    private float sideMoveStartX;
    private float sideMoveTargetX;
    private float sideMoveStartTime;
    private float sideMoveDuration;
    private bool sideMoveEaseOut; // true=easeOut(回正) false=smoothstep(建立/反向)
    private bool sideMoving; // 是否处于侧向运动段（建立/反向/回正）
    private bool sideInputPrev;
    // 按下侧移瞬间的偏移起点（判断本次是否反向跨中心 → 决定时长）
    private float sidePressStartX;
    // 两段式回正：停止侧移时的偏移基准（基点 = 基准 × 65%）
    private float sideRecenterBase;
    // 最近一次松开侧移的时间（侧移→前/后衔接判定用）
    private float sideReleaseTime = -999f;
    // Proxy 相对玩家的侧向偏移（沿 camRight 轴）：慢速平滑追 offsetVec.x。
    // 语义为"相对"而非"绝对投影"——相机旋转不改变此量，消除静止转视角抖动。
    private float proxySideWorld;

    [Header("Y 平滑")]
    [Tooltip("Proxy Y 平滑系数（越大越紧跟玩家高度）。平滑吸收 root motion 的 Y 沉降/回弹（闪避起身下蹲等），消除镜头垂直突变")]
    [SerializeField] private float ySmoothFactor = 15f;
    [Tooltip("玩家 Y 相对锚点偏离超过此值（米）判定为 root motion 大幅 Y 波动（闪避/冲刺下蹲起身），冻结 Proxy Y")]
    [SerializeField] private float yFreezeThreshold = 0.04f;
    [Tooltip("冻结解除后 Proxy Y 限速恢复的速度（米/秒），避免冻结结束瞬间跳变")]
    [SerializeField] private float yFreezeRecoverSpeed = 2f;
    private float smoothedProxyY;
    private bool yFrozen;
    private bool yAnchorSet;
    private float yAnchor;

    /// <summary>
    /// 平滑后的 Proxy Y：硬同步玩家 Y 会把 root motion 的 Y 沉降
    /// （闪避→冲刺过渡动画 RootT.y 从 -0.055 下蹲回升到 0.55）直接传导为镜头垂直突变。
    /// 锚点偏差冻结：玩家 Y 偏离锚点（波动前基准）超过 yFreezeThreshold 时冻结 Proxy Y，
    /// 视角保持水平不向下；玩家回到锚点附近后以 yFreezeRecoverSpeed 限速恢复（不跳变）。
    /// 测量波动本身而非动画状态/速度窗口——覆盖闪避+冲刺全周期，无窗口空洞。
    /// </summary>
    private float SmoothProxyY(float playerY)
    {
        if (smoothedProxyY == 0f && playerY != 0f)
        {
            smoothedProxyY = playerY; // 首次初始化
            yAnchor = playerY;
            yAnchorSet = true;
            return playerY;
        }

        if (!yFrozen)
        {
            // 玩家偏离锚点超过阈值 → root motion 大幅波动（闪避/冲刺下蹲）→ 冻结
            if (yAnchorSet && Mathf.Abs(playerY - yAnchor) > yFreezeThreshold)
            {
                yFrozen = true;
                return smoothedProxyY;
            }

            // 正常跟随：锚点跟随玩家（长期高度变化如上下坡仍能跟上）
            if (yAnchorSet)
                yAnchor = Mathf.Lerp(yAnchor, playerY, 1f - Mathf.Exp(-1.5f * Time.deltaTime));

            float a = 1f - Mathf.Exp(-ySmoothFactor * Time.deltaTime);
            smoothedProxyY = Mathf.Lerp(smoothedProxyY, playerY, a);
            return smoothedProxyY;
        }

        // 冻结期间：玩家回到锚点附近（波动前基准站高）→ 解除冻结
        // （此时 smoothedProxyY 与 playerY 接近，后续 Lerp 无猛拉）
        if (yAnchorSet && Mathf.Abs(playerY - yAnchor) < yFreezeThreshold * 0.5f)
        {
            yFrozen = false;
            smoothedProxyY = playerY; // 锚点=当前位置，无缝交接正常跟随
            return smoothedProxyY;
        }

        // 玩家仍偏离（起身过程中）：限速微调（防止长时间冻结后高度差过大）
        smoothedProxyY = Mathf.MoveTowards(smoothedProxyY, playerY, yFreezeRecoverSpeed * Time.deltaTime);
        return smoothedProxyY;
    }

    /// <summary>
    /// 启动一段侧向平滑运动：从当前偏移到 target，时长 duration。
    /// smoothstep（起止零速）用于建立/反向——无突进感；
    /// easeOut（先快后慢）用于回正——停下后快速靠近、接近中心缓慢（无两段速度突变）。
    /// </summary>
    private void StartSideMove(float target, float duration, bool easeOut = false)
    {
        sideMoveStartX = offsetVec.x;
        sideMoveTargetX = target;
        sideMoveStartTime = Time.time;
        sideMoveDuration = Mathf.Max(duration, 0.001f);
        sideMoveEaseOut = easeOut;
        sideMoving = true;
    }

    /// <summary>
    /// 推进侧向平滑运动段：按 smoothstep / easeOut 插值当前偏移，
    /// 并钳制单帧变化（硬上限）——任何分支/重启都不会产生瞬间跳变（突进感根源）。
    /// </summary>
    private void UpdateSideMove()
    {
        if (!sideMoving) return;
        float t = (Time.time - sideMoveStartTime) / sideMoveDuration;
        t = Mathf.Clamp01(t);
        float s;
        if (sideMoveEaseOut)
            s = 1f - (1f - t) * (1f - t); // easeOut：先快后慢
        else
            s = t * t * (3f - 2f * t);     // smoothstep：起止零速
        float newX = Mathf.Lerp(sideMoveStartX, sideMoveTargetX, s);

        // 单帧钳制：偏移每帧变化不超过 maxSideDeltaPerFrame（米/帧）
        float delta = newX - offsetVec.x;
        float maxDelta = maxSideDeltaPerFrame;
        if (Mathf.Abs(delta) > maxDelta)
            newX = offsetVec.x + Mathf.Sign(delta) * maxDelta;
        offsetVec.x = newX;

        if (t >= 1f)
            sideMoving = false; // 到达终点，停止运动段
    }

    public void SnapToPlayer()
    {
        if (playerTransform == null) return;
        transform.position = playerTransform.position;
    }

    /// <summary>
    /// Proxy 是否正在水平追赶玩家（滞后未收敛）——取景修正等系统以此暂停，避免双反馈抖动。
    /// </summary>
    public bool IsProxyMoving
    {
        get
        {
            if (playerTransform == null) return false;
            Vector3 proxyXZ = Flatten(transform.position);
            Vector3 playerXZ = Flatten(playerTransform.position);
            return Vector3.Distance(proxyXZ, playerXZ) > 0.05f;
        }
    }

    private bool IsPlayerMoving()
    {
        if (moveAction == null || moveAction.action == null) return false;
        Vector2 input = moveAction.action.ReadValue<Vector2>();
        return input.magnitude > moveDeadzone;
    }

    /// <summary>
    /// 玩家是否正在朝相机方向移动（世界位移方向与相机 forward 点积 < -0.3）。
    /// 用实际世界位移（playerDelta）而非输入方向判定——混合输入时输入方向波动，
    /// 世界位移才是玩家真实的移动趋势。
    /// </summary>
    private bool IsPlayerMovingTowardCamera(Vector3 playerDelta)
    {
        if (playerDelta.sqrMagnitude < 0.0001f) return false;
        if (cameraTransform == null) return false;
        Vector3 camFwd = Flatten(cameraTransform.forward).normalized;
        Vector3 moveDir = playerDelta.normalized;
        return Vector3.Dot(moveDir, camFwd) < -0.3f;
    }

    private Vector3 GetMoveDirectionXZ()
    {
        if (cameraTransform == null) return Vector3.zero;
        Vector3 fwd = Flatten(cameraTransform.forward).normalized;
        Vector3 right = Flatten(cameraTransform.right).normalized;
        Vector2 input = moveAction != null && moveAction.action != null
            ? moveAction.action.ReadValue<Vector2>()
            : Vector2.zero;
        return (fwd * input.y + right * input.x).normalized;
    }

    private static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
