using UnityEngine;
using System.Collections;
using Script.Base.BattleAttribute;

/// <summary>
/// BOSS 行为树决策核心（配合 BossAI.controller 使用；Test.controller 不受影响）。
/// 决策仅在慢步窗口（连段间隙）进行，tick 驱动：
/// 1. 距离档位选择连段（远>7m 突刺/跳攻；中4-7m 跳攻/蓄力；近2-4m 普攻/蓄力；贴脸<2m 龙卷/挑劈）
/// 2. 冷却锁：每套连段独立冷却，强制轮换，杜绝连刷同一招
/// 3. 惩罚系统：被玩家连击 3 刀 → 强制防御
/// 4. 攻击锁定：发起攻击前硬转向对准玩家（攻前对准保证基本命中；距离不适导致的落空是正常的）
/// 接口全部复用现有脚本：comboSelect(新参数) / AttackIndex / doSpike / doDefense /
/// doWhirlwind+doWhirlChain(龙卷链专用触发器：入边+链尾，免疫决策窗口清参)。
/// </summary>
public class BossAIController : MonoBehaviour
{
    [Header("决策参数")]
    [Tooltip("决策 tick 间隔（秒）")]
    [SerializeField] private float tickInterval = 0.2f;
    [Tooltip("远距离阈值（米）：超过此距离快步接近，进入此范围才考虑攻击（侦测范围较大，减少玩家后撤导致的反复接近）")]
    [SerializeField] private float farDistance = 8f;
    [Tooltip("中距离阈值（米）：此距离内优先跳攻/蓄力/突刺")]
    [SerializeField] private float midDistance = 4.5f;
    [Tooltip("贴脸阈值（米）：低于此距离优先龙卷")]
    [SerializeField] private float closeDistance = 2f;
    [Tooltip("面向容差（度）：慢步中与玩家夹角小于此值才允许发起攻击，否则先转身")]
    [SerializeField] private float faceAngleTolerance = 30f;
    [Tooltip("慢步窗口中的转身速度")]
    [SerializeField] private float turnToPlayerSpeed = 9f;
    [Tooltip("慢步窗口中朝玩家接近的移动速度（米/秒）：root motion 位移由脚本接管后，慢步靠此速度维持战斗距离")]
    [SerializeField] private float approachSpeed = 2.5f;
    [Tooltip("目标战斗距离（米）：慢步时朝此距离移动，避免被攻击位移推远后失去索敌")]
    [SerializeField] private float desiredCombatDistance = 4.5f;
    [Tooltip("攻击瞄准偏置角（度）：突刺/蓄力上挑出招瞬间向玩家一侧偏转此角度，\n把剑挥轨迹对准玩家（解决出剑点在身体一侧导致正对时从玩家侧边掠过）。\n偏置不宜过大——保留玩家走位/闪避空间")]
    [SerializeField] private float attackAimOffset = 12f;
    [Tooltip("强力瞄准角度阈值（度）：启用强力瞄准的动画，出招瞬间若与玩家夹角 ≤ 此值\n则瞬间硬转对准玩家（无偏置，精确锁定），保证直线突进命中。\n仅对启用强瞄的动画生效（目前只有 SpikeAttack11），其它动画不受影响")]
    [SerializeField] private float strongAimAngle = 45f;
    [Tooltip("强瞄过渡时长（秒）：强力瞄准时在此时长内平滑转向玩家（不突兀），\n期间抑制动画 RootQ 旋转，Slerp 独占转向")]
    [SerializeField] private float spikeAimDuration = 0.6f;

    [Header("调试")]
    [Tooltip("在 Console 输出每次决策")]
    [SerializeField] private bool debugDecision;
    [Tooltip("在 Console 输出转身追踪过程")]
    [SerializeField] private bool debugFacing;
    [Tooltip("DEBUG：只发 R 手蓄力下砸（FocusEnergyFromIdleR / FocusR_Attack02），用于命中率排查")]
    [SerializeField] private bool debugFocusR;
    [Tooltip("DEBUG：开启调试招式前，BOSS 开局先释放一次突刺")]
    [SerializeField] private bool debugSpikeFirst = true;
    // 调试状态机：0=待开局突刺 1=开局突刺已放/进入调试循环 2=下一招放R 3=下一招放L
    private int debugState;
    [Tooltip("DEBUG：只发 Pick&Slashx2 + 钢铁旋风（龙卷链），用于旋风冻结排查")]
    [SerializeField] private bool debugSteelWind;
    [Tooltip("DEBUG：勾选后 BOSS 开局被拉到玩家面前 6m（测试站位）")]
    [SerializeField] private bool debugScene;
    [Tooltip("DEBUG：招式展示模式——BOSS 依次循环展示所有招式（固定机位录制用）。位移招式播完快步跑回原点再转身")]
    [SerializeField] private bool debugShowcase;
    [Tooltip("DEBUG：showcase 招式顺序（依次循环）")]
    [SerializeField] private string[] showcaseSkillOrder = { "combo1", "spike", "jump", "whirl", "focus", "defense" };

    [Header("冷却（秒）")]
    [SerializeField] private float combo1Cooldown = 5f;
    [SerializeField] private float spikeCooldown = 8f;
    [SerializeField] private float jumpAttackCooldown = 12f;
    [SerializeField] private float whirlwindCooldown = 12f;
    [SerializeField] private float focusEnergyCooldown = 15f;
    [SerializeField] private float defenseCooldown = 6f;

    [Header("惩罚系统")]
    [Tooltip("玩家连续命中几次后 BOSS 强制进入防御")]
    [SerializeField] private int punishedHits = 3;
    [Tooltip("惩罚计数的重置时间（秒）：超过此时间未被命中则清零")]
    [SerializeField] private float punishmentResetTime = 5f;

    [Header("随机轮换")]
    [Tooltip("combo1 允许连续出现的最大次数（之后强制换其它招式，直到别的招出现才重置）")]
    [SerializeField] private int combo1MaxStreak = 2;
    [Tooltip("combo1 连续计数（运行期）")]
    [SerializeField] private int combo1Streak;

    // ===== Showcase 展示模式运行期状态 =====
    private int showcaseIndex;      // 当前展示的招式索引
    private int showcasePhase;      // 0=未进战/等待 1=招式播放中 2=等播完 3=跑回原点
    private Vector3 showcaseOrigin; // 原点（进战前位置）
    private float showcaseOriginRotY; // 原点朝向（进战前）
    private bool showcaseInitialized;
    private float showcaseWaitTimer; // 招式播完回慢步后的小停顿
    private bool showcaseLastSkillMoves; // 上一个展示的招式是否位移（播完需回原点）
    private int showcaseSubStep;   // 招式内部子步骤（spike 两次突刺+回身斩、defense 演示）
    private string showcaseCurSkill; // 当前展示的招式名
    // Showcase 相机缓存
    private bool showcaseCamChecked;
    private GameObject showcaseCam;
    private GameObject playerCamGo;
    private Cinemachine.CinemachineFreeLook playerCamFreeLook;
    private GameObject proxyGo;

    private Animator animator;
    private Transform player;

    private int comboSelectHash;
    private int attackIndexHash;
    private int doSpikeHash;
    private int doDefenseHash;
    private int doWhirlwindHash;
    private int doWhirlChainHash;

    private static readonly int SlowWalkLeftHash = Animator.StringToHash("SlowWalk_Left");
    private static readonly int SlowWalkRightHash = Animator.StringToHash("SlowWalk_Right");
    private static readonly int IdleHash = Animator.StringToHash("Idle");
    private static readonly int EquipAndTurnHash = Animator.StringToHash("EquipAndTurnToPlayer");

    // 蓄力/待机蓄力后的出招状态：出招瞬间需要强制转向玩家
    private static readonly int FocusEnergyAttack01Hash = Animator.StringToHash("FocusEnergy_Attack01");
    private static bool focusProbeEnabled = true; // 蓄力上挑探针开关（临时）
    private static readonly int FocusRAttack02Hash = Animator.StringToHash("FocusR_Attack02");
    private static readonly int FocusRAttack08Hash = Animator.StringToHash("FocusR_Attack08");

    // 龙卷链双上挑起手状态：进入瞬间布防链尾触发器 doWhirlChain
    private static readonly int PickAndSlashHash = Animator.StringToHash("Pick&Slashx2");
    private static readonly int WhirlStartHash = Animator.StringToHash("Whirl_Start");
    private static readonly int WhirlLoopHash = Animator.StringToHash("Whirl_Loop");
    private static readonly int WhirlEndHash = Animator.StringToHash("Whirl_End");

    // 普攻连段四态（combo1）
    private static readonly int Combo1Attack01Hash = Animator.StringToHash("Combo1_Attack01");
    private static readonly int Combo1Attack02Hash = Animator.StringToHash("Combo1_Attack02");
    private static readonly int Combo1Attack03Hash = Animator.StringToHash("Combo1_Attack03");
    private static readonly int Combo1Attack08Hash = Animator.StringToHash("Combo1_Attack08");

    // 突刺状态：位移放开钳制，保证突刺实际位移 > 突刺侦测距离（防止无限突刺）
    private static readonly int SpikeAttack11Hash = Animator.StringToHash("SpikeAttack11");
    private static readonly int TurnBackExecution2Hash = Animator.StringToHash("TurnBackExecution2");

    // 快步前进接近状态（SlowWalk ssm 内，comboSelect==2 触发）：起手 + 循环跑
    private static readonly int ApproachStartHash = Animator.StringToHash("ApproachStart");
    private static readonly int ApproachLoopHash = Animator.StringToHash("ApproachLoop");
    private static readonly int ApproachHash = Animator.StringToHash("Approach"); // 兼容旧名

    // 状态切换检测（用于"进入某状态瞬间"的强制转向）
    private int prevStateHash;

    // 连段冷却表
    private float cdCombo1;
    private float cdSpike;
    private float cdJump;
    private float cdWhirl;
    private float cdFocus;
    private float cdDefense;

    // 招式数据表（权重序列/减冷却条件）
    private BossSkillTable skillTable;
    private int sequenceIndex; // 当前序列槽位（0 起）
    private bool sequenceInited;
    private bool firstAttackPending; // 开局必突刺待发（进战后接近到突刺范围触发一次）

    // 慢步环绕：沿玩家切向随机左右移动（保持距离，不直线冲向玩家）
    private int circleDir = 1;      // 环绕方向（1=左 -1=右）
    private float circleSwitchTimer; // 换边计时

    // 空档兜底：连续无法出招超过 fallbackDelay 秒 → 强制触发兜底（不耗冷却）
    private float idleTimer; // 空档计时（有招可用时清零）

    // 惩罚计数
    private int playerHitStreak;
    private float lastHitTime;
    private Health bossHealth;

    // 转身追踪日志用
    private float lastLoggedAngle;

    // root motion 接管：本套动画全为 *Root 位移动画，若不接管会被动画曲线推着漂移+下坠，
    // 干扰 AI 面向与决策（表现为"进战不面向、攻击只向前、复读跳劈"）。
    // 方案：保留 root motion 位移（攻击手感），但旋转由脚本控制（决策窗口强制面向玩家），
    // 并钳制每帧位移幅度，防止大位移动画把 BOSS 推出战斗范围。
    [Header("Root Motion 接管")]
    [Tooltip("决策窗口（慢步/待机）每帧允许的最大位移（米），防止慢步/待机动画位移把 BOSS 推走")]
    [SerializeField] private float decisionMaxMovePerSec = 2f;
    [Tooltip("攻击连段每帧允许的最大位移（米），防止大位移攻击把 BOSS 推出战斗范围")]
    [SerializeField] private float attackMaxMovePerSec = 4f;
    [Tooltip("突刺连段每帧允许的最大位移（米）：突刺突进距离大，能跨越大侦测范围命中")]
    [SerializeField] private float spikeMaxMovePerSec = 9f;
    [Tooltip("快步接近（跑步）每帧允许的最大位移（米）")]
    [SerializeField] private float approachMaxMovePerSec = 5f;

    private float tickTimer;

    // 龙卷链采用双触发器（免疫决策窗口清参的时序竞态）：
    // doWhirlwind = 发招入边（慢步/ApproachLoop → Pick&Slashx2，即时出边同帧消费）；
    // doWhirlChain = 链尾边（Pick&Slashx2 播到 0.99 → SteelWhirlwind），在进入
    // Pick&Slashx2 的瞬间布防（见 CheckAttackStartTurn），消费前无其他过渡可误触。
    // 不再用 comboSelect=9 传链：整数参数跨过渡残留正是"旋风↔双上挑自持循环"的根源。

    /// <summary>
    /// 接管 root motion：攻击连段保留位移（受限幅）与动画旋转；
    /// 快步接近（Approach）应用跑步位移（受限幅）但旋转由脚本控制；
    /// 慢步/待机不应用 root motion 位移/旋转——实际移动与转向由脚本驱动。
    /// Unity 检测到 OnAnimatorMove 后不再自动应用根运动，改由这里手动处理。
    /// 垂直方向（y）不应用动画位移——上挑/跳攻类 clip 的 root y 曲线会累积把 BOSS 顶离地面；
    /// y 由 GravityScript 重力模块负责（CC.Move 驱动，保持贴地）。
    /// 位移统一走 CharacterController.Move：让 CC 的地面检测/碰撞生效（transform.position 直改会绕过）。
    /// </summary>
    private void OnAnimatorMove()
    {
        if (animator == null) return;

        int cur = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;

        // 快步接近：应用跑步 root motion 位移（接近用），旋转由脚本转向控制
        // Showcase 跑回原点（phase 3）时：脚本已驱动位移，跳过 root motion 避免叠加
        if (cur == ApproachStartHash || cur == ApproachLoopHash || cur == ApproachHash)
        {
            if (debugShowcase && showcasePhase == 3)
            {
                return; // 位移由 MoveBackToOrigin 的 MoveViaController 驱动
            }
            Vector3 delta = animator.deltaPosition;
            delta.y = 0f; // 垂直分量交给重力模块
            float maxPerFrame = approachMaxMovePerSec * Time.deltaTime;
            if (delta.magnitude > maxPerFrame && maxPerFrame > 0f)
            {
                delta = delta.normalized * maxPerFrame;
            }
            MoveViaController(delta);
            return;
        }

        if (IsInDecisionWindow())
        {
            // 决策窗口（慢步/待机）：位移/旋转全由脚本控制，root motion 仅作视觉（不应用）
            return;
        }

        // 攻击连段：位移保留（钳制每帧幅度，防止大位移攻击把 BOSS 推出战斗范围）。
        // 位移上限从技能表读（BossSkills 各技能组，未配置用 SkillBase 默认）。
        Vector3 atkDelta = animator.deltaPosition;
        atkDelta.y = 0f; // 垂直分量交给重力模块
        float maxMove = GetMaxMoveForState(cur);
        float maxPerFrame2 = maxMove * Time.deltaTime;
        if (atkDelta.magnitude > maxPerFrame2 && maxPerFrame2 > 0f)
        {
            atkDelta = atkDelta.normalized * maxPerFrame2;
        }

        // 突刺位移方向 = 进入突刺时锁定的冲刺方向（覆盖动画位移方向，保留幅度）：
        // 冲刺方向在进入 SpikeAttack11 瞬间锁定一次（见 CheckAttackStartTurn），
        // 之后不再跟随玩家——穿过玩家后方向不反转，直线突进完整走完。
        // 若未锁定（异常进入），回退为动画自身方向。
        if (cur == SpikeAttack11Hash)
        {
            Vector3 dir = spikeLungeDir;
            if (dir.sqrMagnitude > 0.0001f)
            {
                // 收招段处理：动画冲刺峰值后自带回撤位移（deltaPosition 沿冲刺方向为负）。
                // 若应用会把 BOSS 往玩家方向拉回（重叠→碰撞恢复→被弹出）。
                if (Vector3.Dot(animator.deltaPosition, dir) < 0f)
                {
                    // 收招回撤帧：不应用回撤位移。
                    // 若此刻与玩家重叠（临界），主动沿冲刺方向继续平移离开玩家模型范围——
                    // 预期"收招一瞬间重叠就立刻继续向前穿出"。
                    if (PlayerRef != null)
                    {
                        Vector3 toPlayer = PlayerRef.position - transform.position;
                        toPlayer.y = 0f;
                        if (toPlayer.magnitude < 1.2f)
                        {
                            atkDelta = dir * (spikeMaxMovePerSec * 0.5f * Time.deltaTime);
                        }
                        else
                        {
                            atkDelta = Vector3.zero;
                        }
                    }
                    else
                    {
                        atkDelta = Vector3.zero;
                    }
                }
                else
                {
                    atkDelta = dir * atkDelta.magnitude;
                }
            }
        }
        MoveViaController(atkDelta);

        // 攻击连段旋转：保留动画旋转（回身斩等招式依赖动画朝向）。
        // 强瞄期间跳过：动画自带 RootQ 旋转会与脚本 Slerp 叠加导致转头过快/突兀，
        // 抑制后强瞄转向时长严格生效（见 suppressAnimRotation）。
        if (!suppressAnimRotation)
        {
            transform.rotation *= animator.deltaRotation;
        }
    }

    /// <summary>
    /// 按当前 Animator 状态取该技能的位移上限（来自 BossSkills 技能表）。
    /// 技能未配置位移上限时用 SkillBase 默认值；无表时回退旧字段。
    /// </summary>
    private float GetMaxMoveForState(int cur)
    {
        if (skillTable != null)
        {
            string skillId = StateToSkillId(cur);
            if (skillId != null && skillTable.skills.TryGetValue(skillId, out var def))
            {
                return def.maxMove > 0f ? def.maxMove : skillTable.skillDefaultMaxMove;
            }
        }
        // 兜底：旧字段
        return (cur == SpikeAttack11Hash || cur == TurnBackExecution2Hash) ? spikeMaxMovePerSec : attackMaxMovePerSec;
    }

    /// <summary>
    /// Animator 状态 → 技能 ID 映射（供位移上限/回身斩查询）。
    /// </summary>
    private string StateToSkillId(int cur)
    {
        if (cur == SpikeAttack11Hash || cur == TurnBackExecution2Hash) return "spike";
        if (cur == Combo1Attack01Hash || cur == Combo1Attack02Hash || cur == Combo1Attack03Hash || cur == Combo1Attack08Hash) return "combo1";
        if (cur == WhirlStartHash || cur == WhirlLoopHash || cur == WhirlEndHash || cur == PickAndSlashHash) return "whirl";
        if (cur == FocusEnergyAttack01Hash || cur == FocusRAttack02Hash || cur == FocusRAttack08Hash) return "focus";
        return null;
    }

    /// <summary>
    /// 走 CharacterController 应用水平位移（触发地面检测/碰撞推挤）；
    /// 无 CC 时退回 transform 直改。
    /// </summary>
    private void MoveViaController(Vector3 delta)
    {
        var cc = GetComponent<CharacterController>();
        if (cc != null && cc.enabled) cc.Move(delta);
        else transform.position += delta;
    }

    // ===== 突刺穿体（临时调试，验证后保留/清理）=====
    // 阻挡链完整清单（玩家"像路障"被推着同步后退的根源）：
    // 1) BOSS CC vs 玩家 CC：capsule sweep 互撞（Default 层碰撞开）
    // 2) BOSS BlockerCapsule：fitToHurtbox=True → Play 时动态创建实体胶囊
    //    （非 trigger + kinematic Rigidbody，贴合 Hurtbox 半径 0.51）→ 与玩家 CC 物理推挤
    // 3) 玩家 BlockerCapsule：同样动态创建，贴合玩家 Hurtbox（半径 0.145）
    // 4) BOSS CharacterBlocker.BlockCharacters()：CC.Move 把 BOSS 推回保持 minDistance=2
    // 之前方案只禁 BOSS CC + Blocker 组件，漏了已创建的 BlockerCapsule 实体胶囊（禁用组件不删胶囊）
    private bool spikePassThrough;
    private Script.Base.Hitbox.CharacterBlocker bossBlocker;
    private Script.Base.Hitbox.CharacterBlocker playerBlocker;
    private System.Collections.Generic.List<Collider> ignoredPairs;
    // 突刺冲刺方向：进入 SpikeAttack11 瞬间锁定一次，穿过后不反转（根治抽搐）
    private Vector3 spikeLungeDir;
    // 强瞄期间抑制动画 root 旋转：AimAtPlayer 强瞄协程置 true，
    // OnAnimatorMove 跳过 deltaRotation 叠加——强瞄的 Slerp 是唯一转向源，
    // 保证转向时长严格生效（否则动画自带 RootQ 与脚本转向叠加，转头远超设定时长且突兀）。
    public bool suppressAnimRotation;
    // 突刺起步强瞄协程引用：进入 SpikeAttack11 瞬间启动（平滑精确对准 + 同步冲刺方向）
    private Coroutine spikeAimCoroutine;

    /// <summary>
    /// 突刺穿体：进入 SpikeAttack11 时彻底移除所有阻挡源，离开立即恢复。
    /// 1) 禁用 BOSS CC → MoveViaController 走 transform 直改（无视玩家 CC sweep）
    /// 2) 禁用双方 CharacterBlocker 组件（停 Update：BlockCharacters + FitBlockerToHurtbox）
    /// 3) 禁用双方已创建的 BlockerCapsule 实体胶囊（禁用组件不会删胶囊，必须显式关）
    /// 4) Physics.IgnoreCollision 切断玩家 CC 与 BOSS 全部实体 collider（双保险）
    /// </summary>
    private void SetSpikePassThrough(bool enabled)
    {
        if (spikePassThrough == enabled) return;
        spikePassThrough = enabled;
        var myCC = GetComponent<CharacterController>();

        if (bossBlocker == null) bossBlocker = GetComponent<Script.Base.Hitbox.CharacterBlocker>();
        if (playerBlocker == null && PlayerRef != null) playerBlocker = PlayerRef.GetComponent<Script.Base.Hitbox.CharacterBlocker>();
        var playerCC = PlayerRef != null ? PlayerRef.GetComponent<CharacterController>() : null;

        if (enabled)
        {
            if (myCC != null) myCC.enabled = false;
            if (bossBlocker != null) bossBlocker.enabled = false;
            if (playerBlocker != null) playerBlocker.enabled = false;

            // 禁用双方 BlockerCapsule 实体胶囊（fitToHurtbox 动态创建的）
            if (bossBlocker != null) SetBlockerCapsuleActive(bossBlocker.gameObject, false);
            if (playerBlocker != null) SetBlockerCapsuleActive(playerBlocker.gameObject, false);

            // 双保险：切断玩家 CC 与 BOSS 全部实体 collider 的物理碰撞
            ignoredPairs = new System.Collections.Generic.List<Collider>();
            if (playerCC != null)
            {
                foreach (var col in GetComponentsInChildren<Collider>(true))
                {
                    if (col is CharacterController) continue; // BOSS CC 已禁用，无需 ignore
                    if (!col.enabled) continue;
                    Physics.IgnoreCollision(playerCC, col, true);
                    ignoredPairs.Add(col);
                }
            }
        }
        else
        {
            // 离开突刺：停止起步强瞄协程（若仍在跑），复位旋转抑制标记，
            // 避免异常状态切换后 suppressAnimRotation 残留导致后续招式 root 旋转全被抑制。
            if (spikeAimCoroutine != null)
            {
                StopCoroutine(spikeAimCoroutine);
                spikeAimCoroutine = null;
            }
            suppressAnimRotation = false;

            // 恢复碰撞前先处理"突刺结束与玩家重合"的临界情况：
            // 若 BOSS 与玩家过近（深度重叠），沿突刺方向继续穿过到玩家背后安全距离，
            // 再恢复碰撞——避免碰撞恢复瞬间把 BOSS 弹飞/顶到玩家身上。
            // 此时 CC 仍禁用，用 transform 直改（穿体语义延续）。
            SeparateFromPlayerOnRestore();

            if (myCC != null) myCC.enabled = true;
            if (bossBlocker != null) bossBlocker.enabled = true;
            if (playerBlocker != null) playerBlocker.enabled = true;

            if (bossBlocker != null) SetBlockerCapsuleActive(bossBlocker.gameObject, true);
            if (playerBlocker != null) SetBlockerCapsuleActive(playerBlocker.gameObject, true);

            if (ignoredPairs != null)
            {
                if (playerCC != null)
                    foreach (var col in ignoredPairs) Physics.IgnoreCollision(playerCC, col, false);
                ignoredPairs = null;
            }
        }
    }

    /// <summary>
    /// 突刺结束恢复碰撞前：若与玩家深度重叠（临界情况），沿突刺方向直接穿到玩家背后安全距离，
    /// 避免碰撞恢复瞬间弹飞/顶到玩家身上。视觉上是"穿过去了"，到背后不明显。
    /// 方向用突刺锁定方向（spikeLungeDir）；若无，回退为远离玩家方向。
    /// </summary>
    private void SeparateFromPlayerOnRestore()
    {
        if (PlayerRef == null) return;
        float safeDist = 1.2f; // CC 半径和(0.6) + 余量，足够不重叠

        Vector3 toPlayer = PlayerRef.position - transform.position;
        toPlayer.y = 0f;
        float dist = toPlayer.magnitude;
        if (dist >= safeDist) return; // 已安全，无需处理

        // 方向：优先突刺锁定方向（穿过玩家到背后）；若无效用远离玩家方向兜底
        Vector3 dir = spikeLungeDir;
        if (dir.sqrMagnitude < 0.0001f)
        {
            if (toPlayer.sqrMagnitude < 0.0001f) return; // 完全重合且无方向，无法处理
            dir = -toPlayer.normalized; // 远离玩家
        }

        // 穿到玩家背后：目标位置 = 玩家位置 沿突刺方向再推 safeDist
        Vector3 targetPos = PlayerRef.position + dir * safeDist;
        targetPos.y = transform.position.y;
        transform.position = targetPos;
    }

    /// <summary>
    /// 找到物体下的 BlockerCapsule 实体胶囊并设置 active。
    /// </summary>
    private void SetBlockerCapsuleActive(GameObject root, bool active)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "BlockerCapsule")
            {
                t.gameObject.SetActive(active);
            }
        }
    }

    private void Awake()
    {
        animator = GetComponent<Animator>();
        bossHealth = GetComponentInChildren<Health>();
        comboSelectHash = Animator.StringToHash("comboSelect");
        attackIndexHash = Animator.StringToHash("AttackIndex");
        doSpikeHash = Animator.StringToHash("doSpike");
        doDefenseHash = Animator.StringToHash("doDefense");
        doWhirlwindHash = Animator.StringToHash("doWhirlwind");
        doWhirlChainHash = Animator.StringToHash("doWhirlChain");

        // 加载招式数据表（权重序列/减冷却条件）
        skillTable = new BossSkillTable();
        skillTable.LoadAll();
        firstAttackPending = skillTable.openingSpike;
        SyncTurnBackFromSkillTable();
    }

    /// <summary>
    /// 把 spike 技能组的回身斩参数同步给 IfSlashBackAfterAttack（子攻击归属技能表）。
    /// </summary>
    private void SyncTurnBackFromSkillTable()
    {
        var slashBack = GetComponent<IfSlashBackAfterAttack>();
        if (slashBack != null) slashBack.ApplySpikeSkillTable(skillTable);
    }

    /// <summary>
    /// 重新加载招式数据表（编辑器菜单 Window/Refresh Config XLSX 调用）。
    /// 供策划改完 xlsx 的招式/序列/条件/冷却 Sheet 后热刷新。
    /// </summary>
    public void ReloadSkillTable()
    {
        if (skillTable == null) skillTable = new BossSkillTable();
        skillTable.LoadAll();
        sequenceIndex = 0;
        sequenceInited = false;
        // 开局必突刺开关（配表 BossAI sheet）：进战后第一次出手必定突刺
        firstAttackPending = skillTable.openingSpike;
        SyncTurnBackFromSkillTable();
        if (debugDecision)
        {
            Debug.Log($"[BossAI] 招式表已重载：{skillTable.skills.Count} 招式, {skillTable.sequence.Count} 槽, {skillTable.cooldownRules.Count} 冷却规则, 开局必刺={firstAttackPending}");
        }
    }

    private void Start()
    {
        if (bossHealth != null)
        {
            bossHealth.OnHealthChanged.AddListener(OnBossDamaged);
        }

        // DEBUG 场景：勾选后 BOSS 开局被拉到玩家面前 6m（测试站位）
        if (debugScene && PlayerRef != null)
        {
            Vector3 toPlayer = PlayerRef.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.01f)
            {
                Vector3 targetPos = PlayerRef.position - toPlayer.normalized * 6f;
                targetPos.y = transform.position.y;
                transform.position = targetPos;
            }
        }

        // 临时探针：钢铁旋风冻结诊断（验证后删除）
        whirlProbeInit = true;
        whirlProbeLog = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "Temp", "whirl_probe_log.txt");
        System.IO.File.WriteAllText(whirlProbeLog, "# t state norm pos rotY speed timeScale" + System.Environment.NewLine);
    }

    // ===== 临时探针：钢铁旋风冻结诊断（验证后删除）=====
    private string whirlProbeLog;
    private bool whirlProbeInit;
    private float whirlProbeLast;
    private void WhirlProbeTick()
    {
        if (animator == null || !Application.isPlaying) return;
        if (Time.time - whirlProbeLast < 0.016f) return;
        whirlProbeLast = Time.time;
        var st = animator.GetCurrentAnimatorStateInfo(0);
        int h = st.shortNameHash;
        if (h != WhirlStartHash && h != WhirlLoopHash && h != WhirlEndHash) return;
        string s = h == WhirlStartHash ? "WStart" : h == WhirlLoopHash ? "WLoop" : "WEnd";
        string line = "t=" + Time.time.ToString("0.000")
            + " st=" + s
            + " norm=" + st.normalizedTime.ToString("0.000")
            + " pos=" + transform.position.x.ToString("0.00") + "," + transform.position.z.ToString("0.00")
            + " rotY=" + transform.eulerAngles.y.ToString("0.0")
            + " dRot=" + animator.deltaRotation.eulerAngles.y.ToString("0.00")
            + " ts=" + Time.timeScale.ToString("0.00")
            + " tr=" + (animator.IsInTransition(0) ? "1" : "0");
        System.IO.File.AppendAllText(whirlProbeLog, line + System.Environment.NewLine);
    }
    // ===== 探针结束 =====


    /// <summary>
    /// 懒加载玩家引用：玩家可能初始处于禁用状态（Start 时 FindGameObjectWithTag 拿不到），
    /// 因此在需要时才获取。
    /// </summary>
    private Transform PlayerRef
    {
        get
        {
            if (player == null)
            {
                var go = GameObject.FindGameObjectWithTag("Player");
                if (go != null) player = go.transform;
            }
            return player;
        }
    }


    private void OnDestroy()
    {
        if (bossHealth != null) bossHealth.OnHealthChanged.RemoveListener(OnBossDamaged);
        if (Time.timeScale != 1f) Time.timeScale = 1f; // 兜底（与防御控制器共同维护）
        // 兜底：销毁时若突刺穿体仍生效，恢复碰撞（防止销毁/重载后残留）
        SetSpikePassThrough(false);
    }

    /// <summary>
    /// 减冷却条件检测（每帧调用）：条件由假变真（边沿触发）时，对应招式冷却一次性减少。
    /// 条件/阈值/减量全部来自 BossCooldown + BossConditions 配表。
    /// </summary>
    private void PollCooldownConditions()
    {
        if (skillTable == null || skillTable.cooldownRules.Count == 0) return;

        // 收集当前状态值
        int hits = playerHitStreak;
        float hpRatio = bossHealth != null ? (float)bossHealth.GetCurrentHealth() / Mathf.Max(1, bossHealth.maxHealth) : 1f;
        float dist = PlayerRef != null ? Vector3.Distance(transform.position, PlayerRef.position) : 999f;
        float playerHpRatio = 1f;
        if (PlayerRef != null)
        {
            var playerHealth = PlayerRef.GetComponentInChildren<Health>();
            if (playerHealth != null)
                playerHpRatio = (float)playerHealth.GetCurrentHealth() / Mathf.Max(1, playerHealth.maxHealth);
        }

        var triggers = skillTable.PollCooldownTriggers(hits, hpRatio, dist, playerHpRatio);
        foreach (var t in triggers)
        {
            ReduceSkillCooldown(t.Key, t.Value);
        }
    }

    /// <summary>
    /// 按招式ID减冷却（配表驱动：BossSkills.csv 里的招式ID ↔ 冷却字段映射）。
    /// </summary>
    private void ReduceSkillCooldown(string skillId, float amount)
    {
        switch (skillId)
        {
            case "combo1": cdCombo1 = Mathf.Max(0, cdCombo1 - amount); break;
            case "spike": cdSpike = Mathf.Max(0, cdSpike - amount); break;
            case "jump": cdJump = Mathf.Max(0, cdJump - amount); break;
            case "whirl": cdWhirl = Mathf.Max(0, cdWhirl - amount); break;
            case "focus": cdFocus = Mathf.Max(0, cdFocus - amount); break;
            case "defense": cdDefense = Mathf.Max(0, cdDefense - amount); break;
            default: return;
        }
        if (debugDecision)
        {
            Debug.Log($"[BossAI] 条件达成减冷却 {skillId} -{amount}s");
        }
    }

    private void Update()
    {
        // 冷却推进
        float dt = Time.deltaTime;
        cdCombo1 = Mathf.Max(0, cdCombo1 - dt);
        cdSpike = Mathf.Max(0, cdSpike - dt);

        // 临时探针：钢铁旋风冻结诊断（验证后删除）
        WhirlProbeTick();
        cdJump = Mathf.Max(0, cdJump - dt);
        cdWhirl = Mathf.Max(0, cdWhirl - dt);
        cdFocus = Mathf.Max(0, cdFocus - dt);
        cdDefense = Mathf.Max(0, cdDefense - dt);

        // Showcase 展示模式：绕过正常决策，依次循环展示招式
        if (debugShowcase)
        {
            UpdateShowcase();
            return;
        }
        // showcase 关闭：恢复玩家相机（若之前切换过）
        if (showcaseCamChecked)
        {
            RestorePlayerCamera();
        }

        // 减冷却条件检测（边沿触发）：条件由假变真瞬间，对应招式冷却一次性减少
        // 减冷却条件检测（边沿触发）：条件由假变真瞬间，对应招式冷却一次性减少
        PollCooldownConditions();

        // 惩罚计数重置
        if (playerHitStreak > 0 && Time.time - lastHitTime > punishmentResetTime)
        {
            playerHitStreak = 0;
        }

        // 始终面向玩家：非攻击连段（决策窗口：慢步/待机/进战，及快步接近中）每帧持续转向玩家，
        // 攻击连段期间锁视角（朝向交给动画/连段逻辑）。
        if ((IsInDecisionWindow() || IsApproaching()) && PlayerRef != null)
        {
            TurnTowardPlayer(dt);
        }

        // 慢步环绕移动：绕玩家随机左右移动（不直线冲向玩家——一旦冲向玩家就是要攻击了）。
        // 仅限"真正的慢步状态"（SlowWalk_Left/Right）——进战前待机(Idle/Equip)不环绕。
        // 慢步动画本身无 root 位移，实际移动靠脚本驱动；速度=approachSpeed（环绕=慢走）。
        if (IsSlowWalking() && PlayerRef != null)
        {
            CircleAroundPlayer(dt);
        }

        // 间隔后攻击起手转向：蓄力/待机蓄力等"先蓄力再出招"的连段，
        // 出招瞬间强制转向玩家（蓄力期间玩家走位躲闪，出招瞬间对准保证命中）。
        CheckAttackStartTurn();

        // 决策 tick
        tickTimer += dt;
        if (tickTimer < tickInterval) return;
        tickTimer = 0f;

        if (!IsInDecisionWindow()) return;

        // 面向检查：未面向玩家（夹角 > faceAngleTolerance）时不发招，
        // 继续等每帧转向转到位（连段期间方向锁死，不在此列）。
        // 未面向时：慢步/待机中清 comboSelect/AttackIndex 防残留复读；
        // 快步接近中不清——ApproachStart 起手段没有攻击出边，发招参数必须存活
        // 到 ApproachLoop 落地才被消费，这里清掉会导致"跑步冻到 2.5s 兜底"。
        if (!IsFacingPlayer(out float angle))
        {
            if (!IsApproaching())
            {
                animator.SetInteger(comboSelectHash, 0);
                animator.SetInteger(attackIndexHash, 0);
            }
            if (debugFacing && angle > lastLoggedAngle + 20f)
            {
                lastLoggedAngle = angle;
                Debug.Log($"[BossAI] 转身追玩家中... 夹角 {angle:0}°");
            }
            return;
        }
        if (debugFacing && lastLoggedAngle > 0f)
        {
            Debug.Log("[BossAI] 已面向玩家，恢复决策");
            lastLoggedAngle = 0f;
        }

        // 接近完全由 Decide 驱动：dist>farDistance 时 StartApproach（跑步动画快步接近），
        // 进入攻击范围（<=farDistance）后立刻发招。不在 Update 无条件拉近（避免贴脸才攻击）。
        Decide();
    }

    // ===== Showcase 展示模式（固定机位录制用）=====
    /// <summary>
    /// 招式展示模式：BOSS 依次循环展示 showcaseSkillOrder 里的招式。
    /// 流程：进战模拟（拉玩家触发 noticePlayer）→ 决策窗口依次触发招式 →
    /// 位移类招式播完快步跑回原点 → 转身还原 → 下一个招式。
    /// </summary>
    private void UpdateShowcase()
    {
        if (PlayerRef == null) return;

        // 相机切换：showcase 启用 ShowcaseCamera + 禁用玩家相机（FreeLook 不再驱动主相机）
        EnsureShowcaseCamera();

        // 记录原点（进战前位置/朝向）
        if (!showcaseInitialized)
        {
            showcaseInitialized = true;
            showcaseOrigin = transform.position;
            showcaseOriginRotY = transform.eulerAngles.y;
            // 展示模式：恢复 BOSS 血量并复活（避免此前死亡状态干扰展示）
            var hp = GetComponentInChildren<Script.Base.BattleAttribute.Health>(true);
            if (hp != null && hp.IsDead())
            {
                hp.RestoreHealth();
                Debug.Log("[BossAI][Showcase] BOSS 复活并满血");
            }
        }

        float dist = Vector3.Distance(transform.position, PlayerRef.position);

        // 进战模拟：未进战（>30m）时把玩家拉到 BOSS 面前 8m，触发 noticePlayer
        if (dist > 30f)
        {
            Vector3 toBoss = (transform.position - PlayerRef.position);
            toBoss.y = 0f;
            if (toBoss.sqrMagnitude > 0.0001f)
            {
                Vector3 playerPos = transform.position - toBoss.normalized * 8f;
                playerPos.y = PlayerRef.position.y;
                PlayerRef.position = playerPos;
            }
            return; // 等 noticePlayer 触发进战（距离 <30）
        }

        // 进战后：决策窗口（慢步/待机）触发招式
        if (showcasePhase == 0 && IsInDecisionWindow())
        {
            showcasePhase = 1;
            showcaseWaitTimer = 0.5f;
        }

        if (showcasePhase == 1 && IsInDecisionWindow())
        {
            // 等待小停顿（让上一个招式的收招/镜头稳定）
            showcaseWaitTimer -= Time.deltaTime;
            if (showcaseWaitTimer <= 0f)
            {
                // spike 子流程：第 1 段回原位后（substep==1），继续第 2 段（玩家身后再突刺）
                if (showcaseCurSkill == "spike" && showcaseSubStep == 1)
                {
                    MovePlayerBehind();
                    showcaseWaitTimer = 0.6f;
                    showcaseSubStep = 2;
                    StartSpike();
                    showcasePhase = 2;
                }
                else
                {
                    AdvanceShowcaseSkill();
                    showcasePhase = 2; // 等招式播完回慢步
                }
            }
        }
        else if (showcasePhase == 2 && IsInDecisionWindow())
        {
            // 招式播完回到慢步/待机：处理特殊招式子流程（spike 两次突刺+回身斩、defense 演示）
            if (HandleShowcaseSubStep())
            {
                // 子流程还有后续 → 继续（phase 已内部切换）
            }
            else
            {
                showcasePhase = 3; // 一律回原位
            }
        }
        else if (showcasePhase == 3)
        {
            // 跑回原点（脚本移动 + 跑步动画视觉）
            if (MoveBackToOrigin())
            {
                // 回到原点，转身还原初始朝向
                transform.rotation = Quaternion.Euler(0f, showcaseOriginRotY, 0f);
                showcaseWaitTimer = 0.5f;
                showcasePhase = 1;
            }
        }
    }

    /// <summary>
    /// 触发当前 showcase 招式（无视冷却）。所有招式播完统一回原位（phase 3）。
    /// </summary>
    private void AdvanceShowcaseSkill()
    {
        if (showcaseSkillOrder == null || showcaseSkillOrder.Length == 0) return;
        string skill = showcaseSkillOrder[showcaseIndex % showcaseSkillOrder.Length];
        showcaseIndex++;
        showcaseCurSkill = skill;
        showcaseSubStep = 0;

        switch (skill)
        {
            case "combo1": StartCombo1(); break;
            case "spike": StartSpike(); break;
            case "jump": StartJumpAttack(); break;
            case "whirl": StartWhirlwind(); break;
            case "focus": StartFocusR(); break;
            case "defense": StartShowcaseDefense(); break;
            default: StartCombo1(); break;
        }
        Debug.Log($"[BossAI][Showcase] 展示招式: {skill} (#{showcaseIndex})");
    }

    /// <summary>
    /// 启动防御展示：交给 BossDefenseController 演示（举盾→3次格挡→反击）。
    /// </summary>
    private void StartShowcaseDefense()
    {
        var defense = GetComponent<BossDefenseController>();
        if (defense != null)
        {
            defense.StartShowcaseDemo();
        }
        else
        {
            StartDefense();
        }
    }

    /// <summary>
    /// 处理特殊招式的子流程（招式播完回慢步时调用）。
    /// 返回 true=子流程还有后续（继续展示，phase 已切换）；false=招式完成（回原位）。
    /// spike：突刺 → 回原位 → 玩家移身后再突刺 → 回身斩 → 回原位
    /// defense：举盾 → 放下 → 再举盾 → 模拟 3 次被攻击 → 反击
    /// </summary>
    private bool HandleShowcaseSubStep()
    {
        if (showcaseCurSkill == "spike")
        {
            showcaseSubStep++;
            if (showcaseSubStep == 1)
            {
                // 第 1 段突刺播完：回原位（phase 3），回原位后 phase 1 继续第 2 段
                return false;
            }
            if (showcaseSubStep == 2)
            {
                // 第 2 段突刺播完（玩家已移身后）：回身斩应由 OnSpikeEnd 触发。
                // 等回身斩播完（回慢步）→ 回原位完成整个 spike 展示
                return false;
            }
            return false;
        }

        if (showcaseCurSkill == "defense")
        {
            // 防御演示：BossDefenseController 自动走"举盾→3次格挡→反击→背面→收盾"。
            // 演示仍在运行（boss 在防御/反击流程中）→ 保持等待，不推进；
            // 演示完成（debugTestMode 复位）→ 回原位。
            var defense = GetComponent<BossDefenseController>();
            if (defense != null && defense.IsShowcaseDemoRunning())
            {
                return true; // 仍在演示中，保持 phase 2（子流程未完成）
            }
            return false; // 演示完成 → 回原位
        }

        // 普通招式：完成回原位
        return false;
    }

    /// <summary>
    /// 把玩家瞬移到 BOSS 身后 2m（模拟突刺穿过后的站位，触发回身斩）。
    /// </summary>
    private void MovePlayerBehind()
    {
        if (PlayerRef == null) return;
        Vector3 behind = transform.position - transform.forward * 2f;
        behind.y = PlayerRef.position.y;
        PlayerRef.position = behind;
        Debug.Log("[BossAI][Showcase] 玩家移到 BOSS 身后，等待回身斩派生");
    }

    /// <summary>
    /// 跑回原点（脚本水平移动）。返回是否已到达。
    /// 播 Approach 跑步动画作视觉（OnAnimatorMove 的 Approach 分支在 showcase 下
    /// 用脚本位移替代 root motion——见 OnAnimatorMove 处理）。
    /// </summary>
    private bool MoveBackToOrigin()
    {
        Vector3 toOrigin = showcaseOrigin - transform.position;
        toOrigin.y = 0f;
        float distToOrigin = toOrigin.magnitude;

        if (distToOrigin < 0.3f)
        {
            // 已到达：恢复慢步
            animator.SetInteger(comboSelectHash, 0);
            return true;
        }

        // 播跑步动画（Approach）
        animator.SetInteger(comboSelectHash, 2);
        // 朝原点移动（速度接近 approachMaxMovePerSec）
        Vector3 moveDir = toOrigin / distToOrigin;
        Vector3 delta = moveDir * (approachMaxMovePerSec * Time.deltaTime);
        MoveViaController(delta);
        // 转向移动方向
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), 8f * Time.deltaTime);
        return false;
    }

    /// <summary>
    /// Showcase 相机切换：启用 ShowcaseCamera，禁用玩家 FreeLook（主相机不再被驱动）。
    /// 用缓存引用避免每帧 Find。showcase 关闭时恢复（由 Update 里 debugShowcase=false 分支处理）。
    /// </summary>
    private void EnsureShowcaseCamera()
    {
        if (showcaseCamChecked) return;
        showcaseCamChecked = true;

        showcaseCam = GameObject.Find("ShowcaseCamera");
        playerCamGo = GameObject.Find("Player Camara");
        playerCamFreeLook = playerCamGo != null ? playerCamGo.GetComponent<Cinemachine.CinemachineFreeLook>() : null;
        proxyGo = GameObject.Find("CameraFollowProxy");

        if (showcaseCam != null)
        {
            var cam = showcaseCam.GetComponent<Camera>();
            if (cam != null) cam.enabled = true;
        }
        if (playerCamFreeLook != null) playerCamFreeLook.enabled = false;
        if (proxyGo != null) proxyGo.SetActive(false);
        // PlayerCameraReset 在 Player Camara 上，FreeLook 禁用后它也不该工作
        var reset = playerCamGo != null ? playerCamGo.GetComponent<PlayerCameraReset>() : null;
        if (reset != null) reset.enabled = false;
    }

    /// <summary>
    /// 恢复玩家相机（showcase 关闭时由 Update 调用）。
    /// </summary>
    private void RestorePlayerCamera()
    {
        if (!showcaseCamChecked) return;
        showcaseCamChecked = false;

        if (showcaseCam != null)
        {
            var cam = showcaseCam.GetComponent<Camera>();
            if (cam != null) cam.enabled = false;
        }
        if (playerCamFreeLook != null) playerCamFreeLook.enabled = true;
        if (proxyGo != null) proxyGo.SetActive(true);
        var reset = playerCamGo != null ? playerCamGo.GetComponent<PlayerCameraReset>() : null;
        if (reset != null) reset.enabled = true;
    }

    /// <summary>
    /// 攻击瞄准偏置：仅蓄力上挑（FocusEnergy_Attack01）期间每帧钉朝向——这个动画的
    /// 出剑点在身体一侧，正对玩家时剑从玩家侧边掠过（命中率低）。偏置方向由玩家
    /// 相对 BOSS 的横向偏移决定。动画旋转在 OnAnimatorMove 应用，此处 LateUpdate
    /// 最后校正，保证最终朝向为瞄准方向（动画动作不受影响）。
    /// 突刺（SpikeAttack11）不在此列：瞄准改为进入状态瞬间一次性锁定（CheckAttackStartTurn），
    /// 穿过后不再每帧拉回（根治穿后抽搐）。
    /// </summary>
    private void LateUpdate()
    {
        if (PlayerRef == null || animator == null) return;
        int cur = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        if (cur != FocusEnergyAttack01Hash) return;

        Vector3 toPlayer = PlayerRef.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f) return;

        // 玩家在 BOSS 的哪一侧：叉积 Y 分量 >0 = 玩家在左侧，<0 = 右侧
        Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
        float side = Vector3.Cross(fwd, toPlayer.normalized).y;
        // 玩家在右侧（side<0）→ 右转 +偏置；左侧 → 左转 -偏置
        float offsetSign = side < 0f ? 1f : -1f;
        Quaternion aim = Quaternion.LookRotation(toPlayer.normalized);
        aim *= Quaternion.Euler(0f, offsetSign * attackAimOffset, 0f);
        transform.rotation = aim;

        // ===== 蓄力上挑命中探针（临时）：记录剑/玩家/BOSS朝向 =====
        if (cur == FocusEnergyAttack01Hash && focusProbeEnabled)
        {
            try
            {
                Transform weaponR = null;
                foreach (var t in transform.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Weapon_r") { weaponR = t; break; }
                Transform weaponL = null;
                foreach (var t in transform.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Weapon_l") { weaponL = t; break; }
                string dir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "Temp");
                string path = System.IO.Path.Combine(dir, "focus_probe_log.txt");
                string line = "t=" + Time.time.ToString("0.000")
                    + " bossPos=" + transform.position.ToString("0.00")
                    + " bossFwd=" + fwd.ToString("0.00")
                    + " playerPos=" + PlayerRef.position.ToString("0.00")
                    + " weaponR=" + (weaponR != null ? weaponR.position.ToString("0.00") + "fwd=" + weaponR.forward.ToString("0.00") : "null")
                    + " weaponL=" + (weaponL != null ? weaponL.position.ToString("0.00") : "null");
                System.IO.File.AppendAllText(path, line + System.Environment.NewLine);
            }
            catch (System.Exception) { }
        }
        // ===== 探针结束 =====
    }

    /// <summary>
    /// 是否已面向玩家（水平夹角在容差内）。
    /// </summary>
    private bool IsFacingPlayer(out float angle)
    {
        angle = 180f;
        if (PlayerRef == null) return false;
        Vector3 toPlayer = PlayerRef.position - transform.position;
        toPlayer.y = 0;
        if (toPlayer.sqrMagnitude < 0.0001f) { angle = 0f; return true; }
        Vector3 forward = transform.forward;
        forward.y = 0;
        forward.Normalize();
        angle = Vector3.Angle(forward, toPlayer.normalized);
        return angle <= faceAngleTolerance;
    }

    /// <summary>
    /// 慢步窗口中的转身：实时朝玩家当前位置 Slerp（玩家移动则持续追踪）。
    /// </summary>
    private void TurnTowardPlayer(float dt)
    {
        if (PlayerRef == null) return;
        Vector3 dir = PlayerRef.position - transform.position;
        dir.y = 0;
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(dir.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, turnToPlayerSpeed * dt);
    }

    /// <summary>
    /// 状态切换检测：1) 蓄力/待机后出招瞬间强制面向玩家（出招前玩家走位躲闪，
    /// 出招瞬间对准保证命中）；2) 进入龙卷链起手 Pick&Slashx2 的瞬间布防链尾
    /// 触发器 doWhirlChain——触发器带锁存，此刻设置只会在 0.99 链尾边消费，
    /// 期间无其他过渡可误触，播完即进 SteelWhirlwind。
    /// </summary>
    private void CheckAttackStartTurn()
    {
        if (animator == null || PlayerRef == null) return;
        int cur = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        if (cur == prevStateHash) return;
        prevStateHash = cur;

        // 突刺穿体开关：进入 SpikeAttack11 移除全部阻挡源（可穿体触发回身斩）；
        // 离开突刺状态立即全部恢复。
        SetSpikePassThrough(cur == SpikeAttack11Hash);

        // 进入突刺瞬间：锁定冲刺方向 + 启动强力瞄准（平滑）。
        // 冲刺方向锁一次后不再跟随玩家——穿过玩家后不反转（根治穿后抽搐）。
        // 强力瞄准 = 精确对准玩家（无偏置，直线突进保证命中）+ 0.6s 平滑过渡（不突兀），
        // 期间抑制动画 RootQ 旋转（强瞄的 Slerp 独占转向，时长严格生效）。
        // 夹角 > strongAimAngle（默认45°）时不强瞄（避免大角度突兀转头），
        // 用当前朝向直接冲刺（方向已在进入瞬间锁定）。
        if (cur == SpikeAttack11Hash)
        {
            Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
            if (fwd.sqrMagnitude > 0.0001f)
            {
                spikeLungeDir = fwd.normalized;
            }
            StartSpikeAim();
        }

        if (cur == FocusEnergyAttack01Hash || cur == FocusRAttack02Hash || cur == FocusRAttack08Hash)
        {
            Vector3 dir = PlayerRef.position - transform.position;
            dir.y = 0;
            if (dir.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(dir.normalized);
            }
        }
        else if (cur == PickAndSlashHash)
        {
            animator.ResetTrigger(doWhirlChainHash);
            animator.SetTrigger(doWhirlChainHash);
        }
        else if (cur == SlowWalkLeftHash || cur == SlowWalkRightHash)
        {
            // 落地慢步（任一连段/接近收尾回到慢步）：清整数参数残留 + 重置预锁存触发器。
            // 特别是接近 2.5s 兜底落地时残留的 comboSelect=2，会经即时出边瞬间
            // 重进 ApproachStart 形成"跑步→跑步"循环；触发器若在非决策上下文
            // （过渡中/连段中）被设下而未消费，落地时会自发招——一并重置。
            // 决策窗口内发招的触发器同帧即被消费，此处重置不会误杀合法意图。
            animator.SetInteger(comboSelectHash, 0);
            animator.SetInteger(attackIndexHash, 0);
            animator.ResetTrigger(doSpikeHash);
            animator.ResetTrigger(doDefenseHash);
            animator.ResetTrigger(doWhirlwindHash);
            animator.ResetTrigger(doWhirlChainHash);
        }
    }

    /// <summary>
    /// 同步突刺冲刺方向：AimAtPlayer 强瞄硬转朝向后调用，
    /// 让 OnAnimatorMove 的位移也沿新朝向（否则朝向与位移不一致）。
    /// </summary>
    public void SyncSpikeLungeDir()
    {
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude > 0.0001f)
        {
            spikeLungeDir = fwd.normalized;
        }
    }

    /// <summary>
    /// 突刺起步强力瞄准（平滑版）：进入 SpikeAttack11 瞬间调用。
    /// 与玩家夹角 ≤ strongAimAngle 时，在 spikeAimDuration（默认0.6s）内平滑精确对准
    /// 玩家（无偏置）——强力瞄准与平滑转向共存：强瞄保证直线突进命中，
    /// 平滑保证不突兀。期间抑制动画 RootQ（Slerp 独占转向）。
    /// 冲刺方向逐帧跟随平滑转向（位移与转向一致），协程结束即锁死不再变
    /// （穿过后不追玩家，根治抽搐）。
    /// 夹角过大时不强瞄（避免大角度突兀转头），用进入瞬间锁定的朝向直接冲刺。
    /// </summary>
    public void StartSpikeAim()
    {
        if (PlayerRef == null) return;
        Vector3 toPlayer = PlayerRef.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f) return;

        Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
        float angleToPlayer = Vector3.Angle(fwd, toPlayer.normalized);
        if (angleToPlayer > strongAimAngle) return;

        if (spikeAimCoroutine != null) StopCoroutine(spikeAimCoroutine);
        spikeAimCoroutine = StartCoroutine(SpikeAimRoutine(toPlayer.normalized));
    }

    private IEnumerator SpikeAimRoutine(Vector3 targetDir)
    {
        suppressAnimRotation = true;
        Quaternion start = transform.rotation;
        Quaternion target = Quaternion.LookRotation(targetDir);
        float t = 0f;
        while (t < spikeAimDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / spikeAimDuration);
            // 平滑缓动（先快后慢）
            transform.rotation = Quaternion.Slerp(start, target, 1f - (1f - k) * (1f - k));
            // 逐帧同步冲刺方向：转向过程中位移连续平滑变化，与转头一致
            Vector3 f = transform.forward; f.y = 0f;
            if (f.sqrMagnitude > 0.0001f) spikeLungeDir = f.normalized;
            yield return null;
        }
        transform.rotation = target;
        Vector3 f2 = transform.forward; f2.y = 0f;
        if (f2.sqrMagnitude > 0.0001f) spikeLungeDir = f2.normalized;
        suppressAnimRotation = false;
        spikeAimCoroutine = null;
    }

    /// <summary>
    /// 慢步环绕移动：绕玩家切向随机左右移动（不直线冲向玩家）。
    /// 每 circleSwitchInterval 秒随机换边；速度=approachSpeed（环绕=慢走）。
    /// </summary>
    private void CircleAroundPlayer(float dt)
    {
        if (PlayerRef == null) return;

        // 随机换边：用固定间隔，到点随机翻转方向
        circleSwitchTimer -= dt;
        if (circleSwitchTimer <= 0f)
        {
            circleSwitchTimer = 1.5f;
            circleDir = Random.value < 0.5f ? 1 : -1;
        }

        Vector3 toPlayer = PlayerRef.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f) return;

        // 切向方向 = 垂直玩家方向（左或右）
        Vector3 tangent = Vector3.Cross(Vector3.up, toPlayer.normalized) * circleDir;
        Vector3 move = tangent * approachSpeed * dt;

        var cc = GetComponent<CharacterController>();
        if (cc != null && cc.enabled) cc.Move(move);
        else transform.position += move;
    }

    /// <summary>
    /// 被命中（OnHealthChanged 掉血）：累计惩罚计数。
    /// </summary>
    private void OnBossDamaged(int delta)
    {
        if (delta >= 0) return;
        playerHitStreak++;
        lastHitTime = Time.time;
    }

    /// <summary>
    /// 是否处于快步接近状态（Approach 起手/循环）。
    /// </summary>
    private bool IsApproaching()
    {
        if (animator == null) return false;
        int h = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        return h == ApproachHash || h == ApproachStartHash || h == ApproachLoopHash;
    }

    /// <summary>
    /// 决策窗口：BOSS 处于慢步/进战待机（Idle/EquipAndTurnToPlayer）或快步接近中时做决策。
    /// 快步接近也算窗口：AI 在接近过程中持续判断距离，一旦进入攻击范围立刻发招，
    /// 避免"贴到玩家身上才开始攻击"。
    /// 攻击/防御/突刺等子状态机内不决策（连段期间方向锁死）。
    /// </summary>
    private bool IsInDecisionWindow()
    {
        if (animator == null || PlayerRef == null) return false;
        var st = animator.GetCurrentAnimatorStateInfo(0);
        if (st.shortNameHash == SlowWalkLeftHash || st.shortNameHash == SlowWalkRightHash) return true;
        // 快步接近中也可决策：距离够近立刻发招（打断 Approach）
        if (IsApproaching()) return true;
        // 进战前/待机：Base 层 Idle、EquipAndTurnToPlayer（战斗前也允许面向玩家）
        return st.shortNameHash == IdleHash || st.shortNameHash == EquipAndTurnHash;
    }

    /// <summary>
    /// 是否处于真正的慢步状态（SlowWalk_Left/Right）。
    /// 环绕移动等"战斗中行为"只应在慢步时执行——进战前待机(Idle/Equip)不环绕。
    /// </summary>
    private bool IsSlowWalking()
    {
        if (animator == null) return false;
        int h = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        return h == SlowWalkLeftHash || h == SlowWalkRightHash;
    }

    /// <summary>
    /// 核心决策：惩罚优先 → 距离档位选连段（冷却过滤）。
    /// </summary>
    private void Decide()
    {
        // 快步接近中不清参：ApproachStart 起手段没有攻击出边，本 tick（或更早）
        // 设下的发招参数必须存活到 ApproachLoop 落地才被消费，清掉就是跑步冻结的根源。
        // 慢步/待机中照常清理：这些状态出边即时消费参数，残留才会引发复读漂移。
        if (!IsApproaching())
        {
            animator.SetInteger(comboSelectHash, 0);
            animator.SetInteger(attackIndexHash, 0);
        }

        float dist = Vector3.Distance(transform.position, PlayerRef.position);

        // DEBUG 钢铁旋风：只发 Pick&Slashx2 + 钢铁旋风（龙卷链），用于旋风冻结排查
        if (debugSteelWind)
        {
            if (cdWhirl <= 0f)
            {
                StartWhirlwind();
            }
            return; // debug 模式不走正常决策
        }

        // DEBUG 模式：按勾选顺序轮流释放勾选的招式（蓄力R），开局先突刺一次
        if (debugFocusR)
        {
            DebugProbe("Decide debug 进入, state=" + debugState + " spikeFirst=" + debugSpikeFirst + " dist=" + dist.ToString("0.0"));
            if (debugSpikeFirst && debugState == 0)
            {
                // 开局首刺：进入常规突刺距离（<=farDistance）即可放突刺，
                // 不需要贴到 midDistance——与正常决策的突刺距离一致
                if (dist > farDistance)
                {
                    DebugProbe("首刺前先靠近, dist=" + dist.ToString("0.0"));
                    StartApproach();
                    return;
                }
                debugState = 1;
                DebugProbe("开局首刺 StartSpike, dist=" + dist.ToString("0.0"));
                StartSpike();
                return;
            }
            // 循环释放蓄力R
            if (debugState == 1 || debugState == 3)
            {
                debugState = 2;
            }
            if (debugState == 2)
            {
                debugState = 2;
                StartFocusR();
                return;
            }
            debugState = 2;
        }

        // 惩罚系统：被连击 → 强制防御
        if (playerHitStreak >= punishedHits && cdDefense <= 0f)
        {
            playerHitStreak = 0;
            StartDefense();
            return;
        }

        // 距离超远 → 快步接近
        if (dist > farDistance)
        {
            StartApproach();
            return;
        }

        // 开局必突刺：进战后接近到突刺范围（<=farDistance）且 spike 冷却就绪，
        // 第一次出手必定突刺（只一次），之后进入正常序列。
        if (firstAttackPending && cdSpike <= 0f)
        {
            firstAttackPending = false;
            StartSpike();
            return;
        }

        // 权重序列出招（配表驱动）：按 BossSequence.csv 的槽位顺序循环。
        // fixed 槽直接出该招；pick 槽从候选按权重抽。
        // 招式冷却中 / 距离档位不匹配 → 该槽不可用，回慢步（爆发空档 = 玩家反击机会）。
        TrySequenceAttack(dist);
    }

    /// <summary>
    /// 按权重序列出招：从当前序列索引开始尝试当前槽位。
    /// 成功出招 → 推进序列；槽不可用（冷却/距离）→ 回慢步（不推进，等下次决策）。
    /// </summary>
    private void TrySequenceAttack(float dist)
    {
        if (skillTable == null || skillTable.sequence.Count == 0)
        {
            // 无表：回退旧逻辑（随机出就绪招式）
            FallbackRandomAttack(dist);
            return;
        }

        // 序列为空保护：重新初始化
        if (!sequenceInited)
        {
            sequenceIndex = 0;
            sequenceInited = true;
        }

        var slot = skillTable.sequence[sequenceIndex % skillTable.sequence.Count];
        bool attacked = false; // 本决策是否成功出招（决定空档计时/兜底）

        if (debugDecision)
        {
            Debug.Log($"[BossAI] TrySeq: idx={sequenceIndex} slot={slot.index} isPick={slot.isPick} dist={dist:0.0} cdC={cdCombo1:0.0} cdS={cdSpike:0.0} cdJ={cdJump:0.0} cdW={cdWhirl:0.0} cdF={cdFocus:0.0} cdD={cdDefense:0.0} streak={combo1Streak}");
        }

        if (!slot.isPick)
        {
            // fixed 槽：尝试出该招；无论成功与否都推进到下一槽。
            // 冷却/距离不匹配时跳过（不卡死），让后续 pick 槽（含突刺等）有机会出招。
            try
            {
                if (TryStartSkill(slot.fixedSkill, dist)) attacked = true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BossAI] TryStartSkill 异常: {e}");
            }
            try
            {
                AdvanceSequence();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BossAI] AdvanceSequence 异常: {e}");
            }
            HandleIdleTimer(attacked);
            return;
        }

        // pick 槽：收集候选里"冷却就绪 + 距离匹配"的，按权重抽。
        // combo1 被限次（streak>=max）时排除，避免抽到却出不了导致卡住。
        var candidates = new System.Collections.Generic.List<BossSkillTable.SkillDef>();
        int totalWeight = 0;
        foreach (var skillId in slot.pickSkills)
        {
            if (!skillTable.skills.TryGetValue(skillId, out var def)) continue;
            if (def.id == "combo1" && combo1Streak >= combo1MaxStreak) continue; // combo1 限次排除
            if (!IsSkillReady(def, dist)) continue;
            if (def.weight <= 0) continue; // 权重 0 不参与
            candidates.Add(def);
            totalWeight += def.weight;
        }

        // 死锁兜底：候选全不可用，但 combo1 冷却就绪且仅因限次被排除时，
        // 重置限次并放行 combo1——避免"combo1 被锁死 + 其它招全冷却"导致永久空档。
        if (candidates.Count == 0 && cdCombo1 <= 0f && combo1Streak >= combo1MaxStreak
            && skillTable.skills.TryGetValue("combo1", out var comboDef)
            && SkillRangeMatches(comboDef, dist))
        {
            combo1Streak = 0; // 解除限次
            StartCombo1();
            AdvanceSequence();
            HandleIdleTimer(true);
            return;
        }

        if (candidates.Count > 0)
        {
            // 按权重抽取
            int roll = Random.Range(0, totalWeight);
            BossSkillTable.SkillDef chosen = candidates[0];
            foreach (var def in candidates)
            {
                roll -= def.weight;
                if (roll < 0) { chosen = def; break; }
            }
            if (TryStartSkill(chosen.id, dist))
            {
                attacked = true;
                AdvanceSequence();
            }
        }
        // 候选全冷却/距离不匹配：回慢步（爆发空档）
        HandleIdleTimer(attacked);
    }

    /// <summary>
    /// 空档计时/兜底：本次决策成功出招则清零计时；否则累加。
    /// 连续无法出招超过 fallbackDelay 秒 → 强制触发兜底招式（不耗冷却，应急），清零计时。
    /// </summary>
    private void HandleIdleTimer(bool attacked)
    {
        if (attacked)
        {
            idleTimer = 0f;
            return;
        }

        idleTimer += tickInterval; // 每次决策 tick 约 tickInterval 秒
        if (idleTimer >= skillTable.fallbackDelay)
        {
            idleTimer = 0f;
            // 强制触发兜底（配表 fallbackSkill，默认 defense），不耗冷却
            switch (skillTable.fallbackSkill)
            {
                case "defense": StartDefense(); break;
                case "combo1": StartCombo1(); break;
                case "spike": StartSpike(); break;
                case "jump": StartJumpAttack(); break;
                case "whirl": StartWhirlwind(); break;
                case "focus": StartFocusR(); break;
                default: StartDefense(); break;
            }
            if (debugDecision)
            {
                Debug.Log($"[BossAI] 空档 {skillTable.fallbackDelay:0}s 触发兜底: {skillTable.fallbackSkill}");
            }
        }
    }

    /// <summary>
    /// 仅检查技能的"距离档位"是否匹配当前距离（不含冷却/限次）。
    /// </summary>
    private bool SkillRangeMatches(BossSkillTable.SkillDef def, float dist)
    {
        string range = def.range;
        if (string.IsNullOrEmpty(range)) return true; // 无档位配置视为可用
        if (range.Contains("中") && dist > midDistance) return true;
        if (range.Contains("近") && dist > closeDistance && dist <= farDistance) return true;
        if (range.Contains("贴") && dist <= closeDistance) return true;
        return false;
    }

    /// <summary>
    /// 推进序列索引（循环）。
    /// </summary>
    private void AdvanceSequence()
    {
        sequenceIndex = (sequenceIndex + 1) % skillTable.sequence.Count;
    }

    /// <summary>
    /// 尝试发起指定招式：检查冷却 + 距离档位，成功则调用对应 Start 并返回 true。
    /// </summary>
    private bool TryStartSkill(string skillId, float dist)
    {
        if (!skillTable.skills.TryGetValue(skillId, out var def)) return false;
        if (!IsSkillReady(def, dist)) return false;

        switch (skillId)
        {
            case "combo1":
                // combo1 还有限次逻辑（连续 combo1MaxStreak 次后强制换招）
                if (combo1Streak >= combo1MaxStreak) return false;
                StartCombo1();
                return true;
            case "spike": StartSpike(); return true;
            case "jump": StartJumpAttack(); return true;
            case "whirl": StartWhirlwind(); return true;
            case "focus": StartFocusR(); return true;
            case "defense": StartDefense(); return true;
            default: return false;
        }
    }

    /// <summary>
    /// 招式是否可发：冷却就绪 + 距离档位匹配（表里的"可用档位"：中/近/贴，可组合）。
    /// </summary>
    private bool IsSkillReady(BossSkillTable.SkillDef def, float dist)
    {
        // 冷却检查
        switch (def.id)
        {
            case "combo1": if (cdCombo1 > 0f) return false; break;
            case "spike": if (cdSpike > 0f) return false; break;
            case "jump": if (cdJump > 0f) return false; break;
            case "whirl": if (cdWhirl > 0f) return false; break;
            case "focus": if (cdFocus > 0f) return false; break;
            case "defense": if (cdDefense > 0f) return false; break;
            default: return false;
        }

        // 距离档位：中(>midDistance) / 近(>closeDistance) / 贴(<=closeDistance)
        string range = def.range;
        if (range.Contains("中") && dist > midDistance) return true;
        if (range.Contains("近") && dist > closeDistance && dist <= farDistance) return true;
        if (range.Contains("贴") && dist <= closeDistance) return true;
        return false;
    }

    /// <summary>
    /// 无配表时的兜底：按距离档随机出就绪招式（旧逻辑）。
    /// </summary>
    private void FallbackRandomAttack(float dist)
    {
        var ready = new System.Collections.Generic.List<System.Action>();
        if (dist > midDistance)
        {
            if (cdSpike <= 0f) ready.Add(StartSpike);
            if (cdJump <= 0f) ready.Add(StartJumpAttack);
            if (cdFocus <= 0f) ready.Add(StartFocusR);
        }
        else if (dist > closeDistance)
        {
            if (cdCombo1 <= 0f && combo1Streak < combo1MaxStreak) ready.Add(StartCombo1);
            if (cdFocus <= 0f) ready.Add(StartFocusR);
            if (cdJump <= 0f) ready.Add(StartJumpAttack);
        }
        else
        {
            if (cdWhirl <= 0f) ready.Add(StartWhirlwind);
            if (cdCombo1 <= 0f && combo1Streak < combo1MaxStreak) ready.Add(StartCombo1);
            if (cdFocus <= 0f) ready.Add(StartFocusR);
        }
        if (ready.Count > 0)
        {
            ready[Random.Range(0, ready.Count)]();
        }
    }

    // ---- 连段发起（均先对准玩家再触发） ----

    private void StartSpike()
    {
        animator.ResetTrigger(doSpikeHash);
        animator.SetTrigger(doSpikeHash);
        cdSpike = spikeCooldown;
        combo1Streak = 0; // 出了其它招，重置 combo1 连续计数
        Log("突刺 Spike", Vector3.Distance(transform.position, PlayerRef.position));
    }

    private void StartDefense()
    {
        cdDefense = defenseCooldown;
        animator.ResetTrigger(doDefenseHash);
        animator.SetTrigger(doDefenseHash);
        Log("防御 Defense", Vector3.Distance(transform.position, PlayerRef.position));
    }

    private void StartCombo1()
    {
        // 慢步→Combo1 已改为 comboSelect==1 条件触发，由 AI 主动发起
        animator.SetInteger(attackIndexHash, 0);
        animator.SetInteger(comboSelectHash, 1);
        cdCombo1 = combo1Cooldown;
        combo1Streak++;
        Log("普攻 Combo1", Vector3.Distance(transform.position, PlayerRef.position));
    }

    private void StartJumpAttack()
    {
        animator.SetInteger(attackIndexHash, 0);
        animator.SetInteger(comboSelectHash, 5);
        cdJump = jumpAttackCooldown;
        combo1Streak = 0; // 出了其它招，重置 combo1 连续计数
        Log("跳攻 JumpAttack", Vector3.Distance(transform.position, PlayerRef.position));
    }

    private void StartWhirlwind()
    {
        // 龙卷链全触发器化：doWhirlwind 触发"慢步/ApproachLoop → Pick&Slashx2"入边
        // （即时出边，同帧消费），doWhirlChain 由状态切换检测布防，触发链尾边
        // "Pick&Slashx2(0.99) → SteelWhirlwind"。触发器锁存到消费、用后自动复位，
        // 不受决策窗口清参影响，也根除 comboSelect=9 残留自持循环。
        animator.SetInteger(attackIndexHash, 0);
        animator.SetInteger(comboSelectHash, 0);
        animator.ResetTrigger(doWhirlChainHash);
        animator.SetTrigger(doWhirlwindHash);
        cdWhirl = whirlwindCooldown;
        combo1Streak = 0; // 出了其它招，重置 combo1 连续计数
        Log("龙卷 SteelWhirlwind(双上挑前置)", Vector3.Distance(transform.position, PlayerRef.position));
    }

    private void StartFocusR()
    {
        animator.SetInteger(attackIndexHash, 0);
        animator.SetInteger(comboSelectHash, 3); // comboSelect==3 → FocusEnergyFromIdleR（R手下砸）
        cdFocus = focusEnergyCooldown;
        combo1Streak = 0; // 出了其它招，重置 combo1 连续计数
        Log("蓄力 R手下砸 FocusEnergyFromIdleR", Vector3.Distance(transform.position, PlayerRef.position));
    }

    /// <summary>
    /// 快步前进接近：超出攻击侦测范围时，进入 Approach 状态（Walk_Loop_Root 带 root motion 位移），
    /// 播完回慢步再由 AI 判断是否进入攻击范围。不占用攻击冷却。
    /// </summary>
    private void StartApproach()
    {
        animator.SetInteger(comboSelectHash, 2); // comboSelect==2 → SlowWalk 出边进入 Approach
        if (debugDecision)
        {
            Debug.Log($"[BossAI] 快步接近 Approach (距离 {Vector3.Distance(transform.position, PlayerRef.position):0.0}m)");
        }
    }

    private void Log(string what, float dist)
    {
        if (debugDecision)
        {
            Debug.Log($"[BossAI] 决策: {what}  (距离 {dist:0.0}m)");
        }
    }

    /// <summary>
    /// 调试探针（临时）：写 Temp/debug_probe_log.txt，排查开局首刺等调试流程。
    /// </summary>
    private void DebugProbe(string msg)
    {
        try
        {
            string dir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "Temp");
            string path = System.IO.Path.Combine(dir, "debug_probe_log.txt");
            string line = "t=" + Time.time.ToString("0.000") + " " + msg;
            System.IO.File.AppendAllText(path, line + System.Environment.NewLine);
        }
        catch (System.Exception) { }
    }
}
