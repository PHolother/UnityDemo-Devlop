using UnityEngine;
using Script.Base.Hitbox;

/// <summary>
/// GreatSword Boss 连段动画事件接收器。
/// 每个攻击动画在“伤害帧刚结束”的位置都打了一个 RequestNextAttack 事件，
/// 本方法把事件传来的 index 写入 Animator 的 AttackIndex 参数，
/// 由状态机的 Equals 条件驱动过渡，实现“后摇取消 → 立刻接下一招前摇”。
/// 伤害帧由动画事件 OnDamageStart/OnDamageEnd 驱动武器 hitbox 开关。
/// </summary>
public class GreatSwordComboEvents : MonoBehaviour
{
    private Animator animator;
    private int attackIndexHash;
    private int doSpikeHash;
    private int doTurnBackHash;

    private HitboxController hitbox;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        attackIndexHash = Animator.StringToHash("AttackIndex");
        doSpikeHash = Animator.StringToHash("doSpike");
        doTurnBackHash = Animator.StringToHash("doTurnBack");

        // 武器 hitbox：剑模型下的 HitboxController。武器在骨骼深层
        // （root/pelvis/.../hand_r/Weapon_r/GreatSword_01），必须全局搜索。
        hitbox = GetComponentInChildren<HitboxController>(true);
    }

    public void RequestNextAttack(int index)
    {
        if (animator != null)
        {
            animator.SetInteger(attackIndexHash, index);
        }
    }

    /// <summary>
    /// 触发突刺（Attack11）。由 BOSS 决策脚本在慢步窗口调用（距离/其它条件判断）。
    /// </summary>
    public void RequestSpike()
    {
        if (animator != null)
        {
            animator.SetTrigger(doSpikeHash);
        }
    }

    /// <summary>
    /// 触发突刺回身斩（Execution2）。由 BOSS 决策脚本在慢步窗口调用（距离/其它条件判断）。
    /// </summary>
    public void RequestTurnBack()
    {
        if (animator != null)
        {
            animator.SetTrigger(doTurnBackHash);
        }
    }

    /// <summary>
    /// 伤害帧开始：由动画事件调用，开启武器 hitbox 碰撞判定。
    /// </summary>
    public void OnDamageStart()
    {
        if (hitbox == null) hitbox = GetComponentInChildren<HitboxController>(true);
        if (hitbox != null) hitbox.EnableHitbox();
    }

    /// <summary>
    /// 伤害帧结束：由动画事件调用，关闭武器 hitbox 碰撞判定。
    /// </summary>
    public void OnDamageEnd()
    {
        if (hitbox == null) hitbox = GetComponentInChildren<HitboxController>(true);
        if (hitbox != null) hitbox.DisableHitbox();
    }

    [Header("瞄准")]
    [Tooltip("瞄准转向时长（秒）：AimAtPlayer 触发后在此时间内平滑转向玩家，越小越快（0=瞬间硬切）")]
    [SerializeField] private float aimDuration = 0.08f;
    [Tooltip("强力瞄准角度阈值（度）：AimAtPlayer 触发瞬间，若与玩家夹角 ≤ 此值则强制精确对准\n（强瞄走平滑过渡 strongAimDuration），保证直线突进命中；夹角过大则回退普通平滑转向。\n默认45——只有动画里加了 AimAtPlayer 事件的招式才生效")]
    [SerializeField] private float strongAimAngle = 45f;
    [Tooltip("强力瞄准过渡时长（秒）：强瞄时在此时长内平滑转向玩家（当前 0.6s），\n替代瞬间硬转的突兀感")]
    [SerializeField] private float strongAimDuration = 0.6f;

    private Coroutine aimCoroutine;

    /// <summary>
    /// 瞄准玩家：由动画事件调用（冲刺发起帧）。在 dur 内平滑快速转向玩家
    /// （一次性过程，非持续跟踪）。用于突刺/回身斩等直线突进技的"冲刺前对准"，
    /// 保证突进方向命中；平滑转向避免瞬间硬切的视觉突兀。
    /// </summary>
    /// <param name="duration">强瞄时长（秒）。&lt;=0 用全局 strongAimDuration；
    /// 动画事件可带 floatParameter 指定（如防御反击快速转身 0.2s）</param>
    public void AimAtPlayer(float duration = -1f)
    {
        if (aimCoroutine != null) StopCoroutine(aimCoroutine);
        aimCoroutine = StartCoroutine(AimAtPlayerRoutine(duration));
    }

    private System.Collections.IEnumerator AimAtPlayerRoutine(float customDuration)
    {
        var playerGo = GameObject.FindGameObjectWithTag("Player");
        if (playerGo == null) yield break;
        Vector3 toPlayer = playerGo.transform.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f) yield break;

        // 强力瞄准：与玩家夹角 ≤ strongAimAngle 时强制精确对准（无偏置）；
        // 夹角过大时仍平滑转向玩家（同一时长），只是不额外抑制动画旋转之外的逻辑——
        // 统一走 strongAimDuration 平滑过渡，绝不瞬间转头（转头突兀是用户明确不接受的问题）。
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        fwd.Normalize();
        float angleToPlayer = Vector3.Angle(fwd, toPlayer.normalized);
        bool isStrongAim = angleToPlayer <= strongAimAngle;
        // 统一走平滑过渡，绝不瞬间转头（转头突兀是用户明确不接受的问题）。
        // 时长：动画事件带 floatParameter 时用该值（如防御反击 0.2s 快速转身），否则全局 strongAimDuration。
        float dur = customDuration > 0f ? customDuration : strongAimDuration;

        // 平滑期间抑制动画 root 旋转：动画自带 RootQ 旋转与脚本 Slerp 叠加会让转头
        // 远超 dur 时长且方向冲突（突兀）。抑制后 Slerp 独占转向，dur 严格生效。
        var ai = GetComponent<BossAIController>();
        if (ai != null) ai.suppressAnimRotation = true;

        Quaternion start = transform.rotation;
        Quaternion target = Quaternion.LookRotation(toPlayer.normalized);
        if (dur <= 0f)
        {
            transform.rotation = target;
            if (isStrongAim)
            {
                if (ai != null) ai.SyncSpikeLungeDir();
            }
            if (ai != null) ai.suppressAnimRotation = false;
            aimCoroutine = null;
            yield break;
        }
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            // 平滑缓动（先快后慢）
            transform.rotation = Quaternion.Slerp(start, target, 1f - (1f - k) * (1f - k));
            yield return null;
        }
        transform.rotation = target;
        if (ai != null) ai.suppressAnimRotation = false;
        aimCoroutine = null;
    }
}