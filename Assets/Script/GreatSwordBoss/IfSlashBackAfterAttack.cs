using UnityEngine;

/// <summary>
/// 回身攻击派生控制器：突刺(Attack11)结束后，判断玩家是否在 BOSS 身后，
/// 是则派生回身斩(Execution2)，否则正常收尾回慢步。
/// 同时为回身斩的前冲段附加额外直冲位移（回身斩动画自带前冲较短，
/// 需要对齐普通突刺的攻击距离）。
/// 命名预留：以后其它招式后派生回身斩、或新增回身动作，可在此扩展。
/// </summary>
public class IfSlashBackAfterAttack : MonoBehaviour
{
    [Header("派生判定")]
    [Tooltip("调试用：勾选后无视玩家是否在身后，突刺结束必定派生回身斩")]
    [SerializeField] private bool debugForceSlashBack;
    [Tooltip("有效身后判定距离上限（米）：玩家在身后且在此距离内才派生回身斩，防止远处误判")]
    [SerializeField] private float behindMaxDistance = 4f;

    [Header("回身斩位移增强")]
    [Tooltip("进入回身斩后，沿进入时朝向额外附加的直冲距离（米），用于对齐普通突刺的攻击距离")]
    [SerializeField] private float extraLungeDistance = 0.8f;
    [Tooltip("附加位移的生效区间：回身斩动画归一化时间起")]
    [SerializeField] private float lungeStartNormalized = 0.5086f;
    [Tooltip("附加位移的生效区间：回身斩动画归一化时间止")]
    [SerializeField] private float lungeEndNormalized = 0.8389f;

    private Animator animator;
    private CharacterController controller;
    private Transform player;

    private int doTurnBackHash;
    private static readonly int TurnBackStateHash = Animator.StringToHash("TurnBackExecution2");

    private bool isLunging;
    private Vector3 lungeDirection;
    private float lungeTotalDistance;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        doTurnBackHash = Animator.StringToHash("doTurnBack");
    }

    /// <summary>
    /// 从 BOSS 技能表应用回身斩参数（spike 技能组的子攻击参数），替代 Inspector 手填。
    /// 由 BossAIController 在招式表重载后调用。
    /// </summary>
    public void ApplySpikeSkillTable(BossSkillTable table)
    {
        if (table == null || !table.skills.TryGetValue("spike", out var spike)) return;
        if (!spike.hasTurnBack) return;
        behindMaxDistance = spike.tbBehindMaxDistance;
        extraLungeDistance = spike.tbExtraLungeDistance;
        lungeStartNormalized = spike.tbLungeStartNormalized;
        lungeEndNormalized = spike.tbLungeEndNormalized;
    }

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

    /// <summary>
    /// 由 Attack11 动画事件(OnSpikeEnd, 1.40s)调用：伤害帧结束后、收刀起点。
    /// 判断玩家是否在身后，是则派生回身斩；否则不触发，自然走 ExitTime 收尾回慢步。
    /// </summary>
    public void OnSpikeEnd()
    {
        if (animator == null) return;

        // 防御：清掉可能残留的 trigger，避免上次遗留导致这次误派生
        animator.ResetTrigger(doTurnBackHash);

        // 调试开关：强制派生，方便查看回身斩效果
        if (debugForceSlashBack)
        {
            animator.SetTrigger(doTurnBackHash);
            return;
        }

        if (IsPlayerBehind())
        {
            animator.SetTrigger(doTurnBackHash);
        }
    }

    /// <summary>
    /// 判断玩家是否在 BOSS 身后（面向方向的背后 90° 扇形内）。
    /// 公共方法，未来 BossAIController 决策可直接复用。
    /// </summary>
    public bool IsPlayerBehind()
    {
        Transform target = PlayerRef;
        if (target == null) return false;

        Vector3 toPlayer = target.position - transform.position;
        toPlayer.y = 0;
        if (toPlayer.sqrMagnitude < 0.0001f) return false;

        // 距离上限：玩家在身后但过远时不派生（避免远距离误判）
        if (toPlayer.magnitude > behindMaxDistance) return false;

        Vector3 forward = transform.forward;
        forward.y = 0;
        forward.Normalize();

        // Dot < 0：玩家在身后（夹角 > 90°）
        return Vector3.Dot(forward, toPlayer.normalized) < 0f;
    }

    private void Update()
    {
        HandleExtraLunge();
    }

    /// <summary>
    /// 回身斩前冲段附加直冲位移：沿进入回身斩时的朝向直线前冲，
    /// 让"先直线突进、后段动画自行回身斩"的表现成立（对齐普通突刺的距离）。
    /// </summary>
    private void HandleExtraLunge()
    {
        if (animator == null) return;

        var stateInfo = animator.GetCurrentAnimatorStateInfo(0);

        // 刚进入回身斩状态：快照朝向与总位移
        if (!isLunging && stateInfo.shortNameHash == TurnBackStateHash)
        {
            if (controller == null) return;
            isLunging = true;
            Vector3 fwd = transform.forward;
            fwd.y = 0;
            lungeDirection = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : transform.forward;
            lungeTotalDistance = extraLungeDistance;
        }

        // 退出回身斩状态：复位
        if (isLunging && stateInfo.shortNameHash != TurnBackStateHash)
        {
            isLunging = false;
            return;
        }

        if (!isLunging || controller == null) return;

        // 前冲段：附加位移随归一化时间先快后慢（简单余弦缓出）
        float t = stateInfo.normalizedTime;
        if (t >= lungeStartNormalized && t < lungeEndNormalized)
        {
            float local = Mathf.InverseLerp(lungeStartNormalized, lungeEndNormalized, t);
            float speed = lungeTotalDistance / (lungeEndNormalized - lungeStartNormalized);
            float ease = Mathf.Cos(local * Mathf.PI * 0.5f); // 1 -> 0 缓出
            float perSecond = speed * ease;
            controller.Move(lungeDirection * (perSecond * Time.deltaTime));
        }
    }
}
