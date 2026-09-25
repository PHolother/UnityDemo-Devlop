using UnityEngine;
using Script.Base.BattleAttribute;

/// <summary>
/// BOSS 防御控制器：后撤举盾后进入防御态。
/// 防御判定（由 Health.TakeDamage 扣血前通过 IDamageDefender 前置询问）：
/// - 防御中 + 玩家正面普攻 → 完全免伤（不掉血），播格挡受击（defenseHit），正面计数 +1；
///   正面累计 3 次 → 第3次格挡播半段（剑下沉）→ 顿帧缓速 → 防御反击（doCounterAttack）
/// - 防御中 + 玩家背面普攻 → 防御未接住，正常扣血 → 立刻放下防御（doDefenseEnd）
///   并转身重新朝向玩家（背防御护是弱点，打一下即收盾转身）
/// - 技能/大招命中 → 立刻破盾（预留：技能系统未制作，接入后在技能命中路径调用 BreakDefense()）
/// - Defense_Loop 纯举盾累计 4s 未反击/未收盾 → 自然收盾（doDefenseEnd）回慢步
/// 调试：一键演示模式 debugTestMode 自动走完整流程。
/// </summary>
public class BossDefenseController : MonoBehaviour, Script.Base.Interface.Battle.IDamageDefender
{
    [Header("防御参数")]
    [Tooltip("触发防御反击（正面格挡）所需的命中次数")]
    [SerializeField] private int requiredHits = 3;
    [Tooltip("Defense_Loop 纯举盾持续时间上限（秒），被 Hit 打断时暂停计时；到点自然收盾")]
    [SerializeField] private float defenseTimeout = 4f;

    [Header("背面被击反应")]
    [Tooltip("放下防御后转身朝向玩家的速度")]
    [SerializeField] private float turnToPlayerSpeed = 9f;

    [Header("第3次格挡破防演出")]
    [Tooltip("Defense_Hit 中'剑下沉到底'的时刻（秒），此刻触发顿帧+反击；实测 RightHandT.y 最低点 0.25s")]
    [SerializeField] private float swordDropDelay = 0.25f;
    [Tooltip("顿帧持续时长（真实时间秒）")]
    [SerializeField] private float hitStopDuration = 0.5f;
    [Tooltip("顿帧期间的全局时间缩放")]
    [SerializeField] private float hitStopTimeScale = 0.1f;

    [Header("调试（一键演示）")]
    [Tooltip("勾选后自动演示：进防御 → 3次正面命中（第3次顿帧反击）→ 再进防御 → 背面命中（放盾转身）→ 再进防御等待 4s 超时收盾")]
    [SerializeField] private bool debugTestMode;

    private Animator animator;
    private Health bossHealth;
    private Transform player;

    private int doDefenseHash;
    private int defenseHitHash;
    private int doCounterAttackHash;
    private int breakDefenseHash;
    private int doDefenseEndHash;

    private static readonly int DefenseLoopHash = Animator.StringToHash("Defense_Loop");
    private static readonly int DefenseHitHash = Animator.StringToHash("Defense_Hit");

    private int frontHitCount;
    private bool inDefense;
    private float loopTimer;
    private Coroutine finalGuardRoutine;
    private bool hitStopActive;    // 顿帧已开始（timeScale 已改），交给协程自己恢复
    private bool isTurningToPlayer;

    // 演示状态机
    private int debugStep;
    private float debugTimer;
    private int demoWaitCount;
    private bool demoWaitingTimeout; // 演示步骤11：超时豁免解除，恢复正常超时计时

    private void Awake()
    {
        animator = GetComponent<Animator>();
        bossHealth = GetComponentInChildren<Health>();
        doDefenseHash = Animator.StringToHash("doDefense");
        defenseHitHash = Animator.StringToHash("defenseHit");
        doCounterAttackHash = Animator.StringToHash("doCounterAttack");
        breakDefenseHash = Animator.StringToHash("breakDefense");
        doDefenseEndHash = Animator.StringToHash("doDefenseEnd");
    }

    private void Start()
    {
        var playerGo = GameObject.FindGameObjectWithTag("Player");
        player = playerGo != null ? playerGo.transform : null;

        if (bossHealth != null)
        {
            // OnHealthChanged 现在只承载"背面命中"（正常扣血）的放盾反应；
            // 正面免伤不产生掉血事件，不走这里。
            bossHealth.OnHealthChanged.AddListener(OnHealthChanged);
        }
    }

    private void OnDestroy()
    {
        if (bossHealth != null)
        {
            bossHealth.OnHealthChanged.RemoveListener(OnHealthChanged);
        }
        // 兜底：销毁时若顿帧进行中，恢复全局时间
        if (hitStopActive) Time.timeScale = 1f;
    }

    private void Update()
    {
        bool isDefending = IsInDefenseState();
        if (isDefending)
        {
            // 超时只累计纯举盾（Defense_Loop）时长，Hit 打断时暂停；
            // 演示模式下仅"超时演示阶段"（步骤11之后）恢复计时，其余步骤豁免（2s/步节奏会超过 4s 窗口）
            if ((!debugTestMode || demoWaitingTimeout) &&
                animator != null &&
                animator.GetCurrentAnimatorStateInfo(0).shortNameHash == DefenseLoopHash)
            {
                loopTimer += Time.deltaTime;
                if (loopTimer >= defenseTimeout)
                {
                    Debug.Log($"[BossDefenseController] 防御超时（{defenseTimeout}s）→ 自然收盾");
                    EndDefenseByTimeout();
                }
            }
        }
        else
        {
            // 离开防御态（反击/收盾/破盾后）复位
            if (inDefense)
            {
                inDefense = false;
                loopTimer = 0f;
                frontHitCount = 0;
                CancelFinalGuard();
            }
        }
        inDefense = isDefending;

        // 放盾转身：非防御态且转身标记开启时，平滑转向玩家
        if (isTurningToPlayer)
        {
            TurnToPlayer();
        }

        if (debugTestMode)
        {
            RunDebugDemo();
        }
    }

    /// <summary>
    /// IDamageDefender 实现：Health.TakeDamage 扣血前调用。
    /// 防御中且攻击来自正面 → 免伤 + 格挡表现 + 计数，返回 true；
    /// 否则（背面/未来技能）返回 false，伤害正常结算。
    /// </summary>
    public bool TryDefend(int damage, GameObject attacker)
    {
        if (!IsInDefenseState()) return false;
        if (IsPlayerBehindBoss()) return false; // 背面防不住：正常扣血，由 OnHealthChanged 触发放盾转身

        // 正面格挡成功：完全免伤
        frontHitCount++;
        loopTimer = 0f; // 成功格挡刷新防御窗口：防御成功就要继续防御，超时从最近一次格挡重新计
        if (frontHitCount >= requiredHits)
        {
            frontHitCount = 0;
            StartFinalGuardCounter();
        }
        else
        {
            animator.SetTrigger(defenseHitHash);
        }
        return true;
    }

    /// <summary>
    /// 正常扣血路径的反应（背面普攻命中）：
    /// 防御没接住 → 立刻放下防御（doDefenseEnd）并转身朝向玩家。
    /// </summary>
    private void OnHealthChanged(int delta)
    {
        if (delta >= 0) return;           // 只处理掉血
        if (!IsInDefenseState()) return;  // 非防御态不反应

        Debug.Log("[BossDefenseController] 背面被命中，防御未接住 → 放下防御并转身朝向玩家");
        LowerGuardAndTurn();
    }

    /// <summary>
    /// 放下防御并转身：发 doDefenseEnd（Defense_End → Exit 回慢步），
    /// 同时启动转身协程，在放盾过程中平滑转向玩家。
    /// </summary>
    private void LowerGuardAndTurn()
    {
        if (animator == null) return;
        animator.SetTrigger(doDefenseEndHash);
        isTurningToPlayer = true;
    }

    /// <summary>
    /// 平滑转身朝向玩家（复用 NoticePlayer 的 Slerp 口径），转正后自动停止。
    /// </summary>
    private void TurnToPlayer()
    {
        if (player == null) { isTurningToPlayer = false; return; }

        Vector3 direction = player.position - transform.position;
        direction.y = 0;
        if (direction.sqrMagnitude < 0.0001f) { isTurningToPlayer = false; return; }

        Quaternion target = Quaternion.LookRotation(direction.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, turnToPlayerSpeed * Time.deltaTime);

        // 转到与目标夹角 < 2° 视为完成
        if (Vector3.Angle(transform.forward, direction.normalized) < 2f)
        {
            isTurningToPlayer = false;
        }
    }

    /// <summary>
    /// 预留接口：玩家技能/大招命中时调用，立即破盾。
    /// 玩家技能尚未制作，暂无调用方。
    /// </summary>
    public void BreakDefense()
    {
        if (animator == null) return;
        if (!IsInDefenseState()) return;
        animator.SetTrigger(breakDefenseHash);
    }

    /// <summary>
    /// 进入防御流程（后撤→举盾）。未来 BossAIController 决策调用；
    /// 演示模式也走这里。
    /// </summary>
    public void RequestDefense()
    {
        if (animator == null) return;
        if (IsInDefenseState()) return;
        isTurningToPlayer = false; // 进入防御时停止转身
        animator.SetTrigger(doDefenseHash);
    }

    /// <summary>
    /// 启动防御演示（showcase 展示用）：自动走"进防御→3次正面命中→反击→背面→超时收盾"。
    /// 演示完自动重置（debugTestMode=false）。判断演示完成：IsShowcaseDemoRunning() 变 false。
    /// </summary>
    public void StartShowcaseDemo()
    {
        debugTestMode = true;
        debugStep = 0;
        debugTimer = 0f;
        demoWaitCount = 0;
        demoWaitingTimeout = false;
        frontHitCount = 0;
        isTurningToPlayer = false;
    }

    /// <summary>
    /// 防御演示是否运行中（showcase 判断演示完成用）。
    /// </summary>
    public bool IsShowcaseDemoRunning()
    {
        return debugTestMode;
    }

    private void EndDefenseByTimeout()
    {
        loopTimer = 0f;
        frontHitCount = 0;
        animator.SetTrigger(doDefenseEndHash);
    }

    private bool IsInDefenseState()
    {
        if (animator == null) return false;
        var st = animator.GetCurrentAnimatorStateInfo(0);
        return st.shortNameHash == DefenseLoopHash || st.shortNameHash == DefenseHitHash;
    }

    /// <summary>
    /// 玩家是否在 BOSS 身后（后方 180°）。复用突刺层的判定口径。
    /// </summary>
    private bool IsPlayerBehindBoss()
    {
        if (player == null) return false;
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0;
        if (toPlayer.sqrMagnitude < 0.0001f) return false;
        Vector3 forward = transform.forward;
        forward.y = 0;
        forward.Normalize();
        return Vector3.Dot(forward, toPlayer.normalized) < 0f;
    }

    /// <summary>
    /// 第3次格挡破防演出：照常播 Defense_Hit（受击下沉），
    /// 剑下沉到底（swordDropDelay）时：全局顿帧缓速 + 立即反击——
    /// Defense_Hit 只播前半段（下沉），不播回升，直接被 CounterAttack 截断。
    /// </summary>
    private void StartFinalGuardCounter()
    {
        animator.SetTrigger(defenseHitHash);
        if (finalGuardRoutine != null) StopCoroutine(finalGuardRoutine);
        finalGuardRoutine = StartCoroutine(FinalGuardCounterRoutine());
    }

    private System.Collections.IEnumerator FinalGuardCounterRoutine()
    {
        // 等剑下沉到底（受 timeScale 影响的动画时间）
        yield return new WaitForSeconds(swordDropDelay);

        // 顿帧：全局缓速，让玩家看清"防住了→即将反击"
        hitStopActive = true;
        Time.timeScale = hitStopTimeScale;
        // 同时刻截断 Defense_Hit（只播了下沉段），0.25s 混合进反击（在缓速中过渡）
        animator.SetTrigger(doCounterAttackHash);

        // 真实时间计时恢复
        yield return new WaitForSecondsRealtime(hitStopDuration);
        Time.timeScale = 1f;
        hitStopActive = false;
        finalGuardRoutine = null;
    }

    /// <summary>
    /// 恢复全局时间缩放（防御流程被打断时调用，防止 timeScale 泄漏）。
    /// 顿帧已开始则不动（协程会自己恢复），只取消尚未进入顿帧的等待协程。
    /// </summary>
    private void CancelFinalGuard()
    {
        if (finalGuardRoutine != null)
        {
            StopCoroutine(finalGuardRoutine);
            finalGuardRoutine = null;
        }
        if (!hitStopActive)
        {
            Time.timeScale = 1f;
        }
    }

    /// <summary>
    /// 一键演示：进防御 → 3次正面命中（第3次顿帧反击）→ 再进防御 → 背面命中（放盾转身）
    /// → 再进防御 → 等 4s 超时自然收盾。直接走计数/反应分支，不经过伤害系统。
    /// </summary>
    private void RunDebugDemo()
    {
        debugTimer += Time.deltaTime;
        if (debugTimer < 2f) return; // 每步间隔 2 秒
        debugTimer = 0f;

        switch (debugStep)
        {
            case 0:
                Debug.Log("[BossDefenseController][Demo] 步骤1：进入防御");
                RequestDefense();
                break;
            case 1:
            case 2:
            case 3:
                if (!IsInDefenseState()) return; // 等真正进了防御态再模拟（等待不推进步骤）
                Debug.Log($"[BossDefenseController][Demo] 步骤{debugStep + 1}：模拟正面普攻命中（第{debugStep}/3 次）");
                SimulateHit(true);
                break;
            case 4:
                Debug.Log("[BossDefenseController][Demo] 步骤5：正面第3次命中 → 剑沉到底后顿帧+反击");
                SimulateHit(true);
                break;
            case 5:
                // 等反击流程走完（顿帧0.5s + 反击2.43s + Exit），回到非防御态
                if (IsInDefenseState()) return;
                Debug.Log("[BossDefenseController][Demo] 步骤6：反击演示完毕，重新进入防御演示背面反应");
                RequestDefense();
                break;
            case 6:
                if (!IsInDefenseState()) return; // 等重新进了防御态
                Debug.Log("[BossDefenseController][Demo] 步骤7：模拟背面普攻命中（1次）→ 应放盾转身朝向玩家");
                SimulateHit(false);
                break;
            case 7:
                // 等放盾转身流程走完（Defense_End 0.5s + Exit）
                if (IsInDefenseState()) return;
                Debug.Log("[BossDefenseController][Demo] 步骤8：背面反应演示完毕（BOSS 应已转身朝向玩家），进入防御等待 4s 超时自然收盾");
                RequestDefense();
                break;
            case 8:
                if (!IsInDefenseState()) return;
                Debug.Log("[BossDefenseController][Demo] 步骤9：已进入防御，解除超时豁免，等待 4s 超时自然收盾（不再操作）");
                demoWaitingTimeout = true;
                break;
            case 9:
                // 等超时收盾结束（4s Loop + End 0.5s + Exit），最多等 15s 防演示卡死
                demoWaitCount++;
                if (IsInDefenseState() && demoWaitCount < 75) return;
                Debug.Log("[BossDefenseController][Demo] 演示完毕（超时收盾" + (IsInDefenseState() ? "超时未触发" : "已触发") + "），取消勾选 debugTestMode 结束");
                debugTestMode = false;
                debugStep = 0;
                debugTimer = 0f;
                demoWaitCount = 0;
                demoWaitingTimeout = false;
                return;
        }
        demoWaitCount = 0;
        debugStep++;
    }

    /// <summary>
    /// 模拟命中：true=正面（走免伤计数），false=背面（走放盾转身）。仅调试用。
    /// </summary>
    private void SimulateHit(bool fromFront)
    {
        if (!IsInDefenseState()) return;

        if (fromFront)
        {
            frontHitCount++;
            if (frontHitCount >= requiredHits)
            {
                frontHitCount = 0;
                StartFinalGuardCounter();
                return;
            }
            animator.SetTrigger(defenseHitHash);
        }
        else
        {
            // 背面命中：正常扣血（演示不真扣），反应为放盾转身
            LowerGuardAndTurn();
        }
    }
}
