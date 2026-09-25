using Script.Base.BattleAttribute;
using Script.Base.Interface;
using UnityEngine;

/// <summary>
/// 玩家受击反馈：Health 掉血 → 播受击动画（Hurt_F）+ 打断攻击/闪避 + 受击硬直。
/// 受击后短暂无敌帧，防止 BOSS 连段把玩家一套带走。
/// 掉血链路（HitboxController.OnHit → CombatHandler.HandleHit → Health.TakeDamage）
/// 完成后由 onHealthChanged 事件驱动本组件，不侵入现有战斗代码。
/// </summary>
public class PlayerHurtController : MonoBehaviour
{
    [Header("受击硬直（秒）：受击动画期间禁操控")]
    [SerializeField] private float stunDuration = 0.5f;
    [Header("受击无敌帧（秒）：硬直结束后仍短暂无敌，防连段秒杀")]
    [SerializeField] private float invincibleDuration = 0.8f;
    [Header("受击动画过渡时长（秒）")]
    [SerializeField] private float hurtFadeDuration = 0.08f;
    [Header("调试：受击反馈正常但不扣血（测试受击表现用）")]
    [SerializeField] private bool debugInvulnerable = false;

    private Animator animator;
    private IMovementProvider movementProvider;
    private Health health;

    private int hurtHash;
    private int hurtBackHash;
    private int speedHash;
    private float stunEndTime;
    private float invincibleEndTime;
    private bool hurtLockActive; // 受击锁是否激活（受击时 true，硬直结束 false）
    private float lastHurtTriggerTime = -999f; // 最近一次 SetTrigger(hurt) 时间（探针用）
    private bool pendingFromBack; // CombatHandler 命中时暂存的正/背面受击方向（OnHealthChanged 触发时消费）
    private Script.Player.KatanaAnimationEvents katanaEvents; // 攻击动画事件转发（受击时关闭武器 hitbox）

    private static readonly int HurtCrossfadeHash = Animator.StringToHash("Hurt_F"); // 保留：探针对照用
    private static bool hurtProbe = true; // 受击链路探针开关（临时）

    private void Awake()
    {
        animator = GetComponent<Animator>();
        movementProvider = GetComponent<IMovementProvider>();
        katanaEvents = GetComponent<Script.Player.KatanaAnimationEvents>();
        hurtHash = Animator.StringToHash("hurt");
        hurtBackHash = Animator.StringToHash("hurtBack");
        speedHash = Animator.StringToHash("Speed");
        HurtProbe("Awake animator=" + (animator != null ? "ok" : "null"));
    }

    private void Start()
    {
        // 直接找 Health（BattleAttributes 子物体上），不依赖桥接类型
        foreach (var c in GetComponentsInChildren<Component>(true))
        {
            if (c != null && c.GetType().Name == "Health")
            {
                health = c as Health;
                break;
            }
        }

        HurtProbe("Start health=" + (health != null ? health.gameObject.name : "NULL!"));

        if (health == null)
        {
            Debug.LogError("[PlayerHurtController] 未找到 Health 组件，受击反馈不生效", this);
            enabled = false;
            return;
        }

        health.OnHealthChanged.AddListener(OnHealthChanged);
    }

    private void OnDestroy()
    {
        if (health != null) health.OnHealthChanged.RemoveListener(OnHealthChanged);
    }

    /// <summary>
    /// Health.OnHealthChanged 回调：delta 为负表示掉血。
    /// </summary>
    private void OnHealthChanged(int delta)
    {
        HurtProbe("OnHealthChanged delta=" + delta + " cur=" + health.GetCurrentHealth());
        if (delta >= 0) return; // 只处理掉血

        // 死亡交给死亡流程处理，不受击动画
        if (health.GetCurrentHealth() <= 0) return;

        // 受击反馈照常触发（硬直/无敌/打断）
        HurtProbe("调用 TriggerHurtAnimation 前 animator=" + (animator != null ? "ok state=" + GetAnimStateName() : "NULL"));
        // 用 CombatHandler 命中时暂存的方向触发正/背面受击动画（消费后重置为正面兜底）
        TriggerHurtAnimation(pendingFromBack);
        pendingFromBack = false;
        HurtProbe("TriggerHurtAnimation 后 animator=" + (animator != null ? "ok state=" + GetAnimStateName() : "NULL"));
        stunEndTime = Time.time + stunDuration;
        invincibleEndTime = Time.time + invincibleDuration;

        // 打断攻击与闪避（与 PlayerDodge.StartDodge 的打断处理同款）
        var attackReset = GetComponent<IAttackReset>();
        if (attackReset != null)
        {
            attackReset.ResetAttack();
            attackReset.ClearAttackBuffer();
        }
        movementProvider.SetHurtLock(true);    // 受击锁定（独立于攻击锁）
        movementProvider.SetAttackLock(false); // 确保攻击锁已解除（受击打断攻击）
        hurtLockActive = true;

        // Debug Invulnerable：反馈已触发，把本次掉血回滚（血量不变）
        // 直接改 currentHealth 字段，不会再触发 OnHealthChanged（无递归）。
        // 注：UI 血条由 OnHealthChanged 驱动，回滚后血条会在下一次事件时对齐。
        if (debugInvulnerable)
        {
            var t = health.GetType();
            var f = t.GetField("currentHealth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            int cur = (int)f.GetValue(health);
            int max = (int)t.GetField("maxHealth").GetValue(health);
            f.SetValue(health, Mathf.Min(cur - delta, max)); // delta 为负，-delta 即回滚掉血量
        }
    }

    /// <summary>
    /// 触发受击动画。fromBack=true 用背面受击（hurtBack trigger），否则正面受击（hurt trigger）。
    /// 受击瞬间把移动速度归零：受击动画期间 Locomotion 混合树停在 Idle 姿态，
    /// 返回过渡淡入时是站姿（与受击末姿态接近），避免"弹回跑步姿态"的突兀。
    /// </summary>
    private void TriggerHurtAnimation(bool fromBack = false)
    {
        HurtProbe("TriggerHurtAnimation 执行 fromBack=" + fromBack + " animator=" + (animator != null ? "ok" : "NULL"));
        if (animator == null) { HurtProbe("animator 为 NULL，无法播受击"); return; }
        animator.SetFloat(speedHash, 0f);
        // 受击瞬间主动关闭武器 hitbox：动画从攻击状态切到 Hurt_F 时，攻击动画里的
        // DisableHitbox 事件不会触发（动画被中断），刀 hitbox 会残留激活（一直能打到 BOSS）。
        if (katanaEvents != null)
        {
            katanaEvents.DisableHitbox();
            HurtProbe("受击主动关闭武器 hitbox");
        }
        // 用 hurt/hurtBack Trigger 触发 AnyState 过渡到 Hurt_F/Hurt_B（控制器 7c56248 设计的方式）。
        if (fromBack)
        {
            animator.ResetTrigger(hurtBackHash);
            animator.SetTrigger(hurtBackHash);
        }
        else
        {
            animator.ResetTrigger(hurtHash);
            animator.SetTrigger(hurtHash);
        }
        lastHurtTriggerTime = Time.time;
    }

    /// <summary>
    /// 受击方向暂存（由 CombatHandler 在命中瞬间调用）：根据攻击者相对玩家的位置
    /// 判断正面/背面受击，供 OnHealthChanged 触发受击动画时消费。
    /// 完全侧面（夹角接近 90°，无法判断正反）时以正面受击兜底。
    /// </summary>
    /// <param name="attackerPosition">攻击者世界坐标</param>
    public void SetHitDirection(Vector3 attackerPosition)
    {
        Vector3 toAttacker = attackerPosition - transform.position;
        toAttacker.y = 0f;
        if (toAttacker.sqrMagnitude < 0.0001f)
        {
            // 攻击者与玩家重合：无法判断方向，正面兜底
            pendingFromBack = false;
            return;
        }
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        fwd.Normalize();
        float dot = Vector3.Dot(fwd, toAttacker.normalized);
        // dot>0 = 攻击者在玩家前方（正面受击）；dot<0 = 在后方（背面受击）。
        // |dot| < 0.3（夹角约 72°~108°）视为完全侧面，无法判断正反 → 正面兜底。
        pendingFromBack = dot < -0.3f;
        HurtProbe("SetHitDirection dot=" + dot.ToString("0.000") + " fromBack=" + pendingFromBack);
    }

    /// <summary>
    /// 当前 Animator 状态名（探针用，短哈希匹配）。
    /// </summary>
    private string GetAnimStateName()
    {
        try
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);
            var next = animator.GetNextAnimatorStateInfo(0);
            return "curHash=" + st.shortNameHash + " nextHash=" + next.shortNameHash + " nextNorm=" + next.normalizedTime.ToString("0.00");
        }
        catch (System.Exception) { return "?error"; }
    }

    private void Update()
    {
        // 受击动画进入检测探针：SetTrigger(hurt) 后 1.6s 内持续记录 Animator 状态，
        // 覆盖完整受击动画（1.167s）+ 返回过渡，确认 AnyState 过渡切入/弹回的时刻。
        if (hurtProbe && Time.time - lastHurtTriggerTime < 1.6f)
        {
            var st = animator != null ? animator.GetCurrentAnimatorStateInfo(0) : default;
            bool inHurt = st.shortNameHash == HurtCrossfadeHash || st.shortNameHash == Animator.StringToHash("Hurt_F");
            var next = animator != null ? animator.GetNextAnimatorStateInfo(0) : default;
            HurtProbe("Update 检测 inHurt=" + inHurt + " curHash=" + st.shortNameHash + " curNorm=" + st.normalizedTime.ToString("0.00") + " nextHash=" + next.shortNameHash + " nextNorm=" + next.normalizedTime.ToString("0.00"));
        }

        // 受击硬直结束：仅解除受击锁（攻击锁独立管理，不互相干扰）。
        // 只有受击锁在时才能恢复——若攻击锁也在（攻击中被打断后新攻击），
        // CanMove 仍由攻击锁控制，不会误恢复。
        if (movementProvider != null && hurtLockActive &&
            Time.time >= stunEndTime && health != null && health.GetCurrentHealth() > 0)
        {
            movementProvider.SetHurtLock(false);
            hurtLockActive = false;
        }
    }

    /// <summary>
    /// 是否处于受击无敌帧（供受击方伤害过滤或表现扩展）。
    /// 注意：无敌帧生效依赖伤害入口调用 CanBeHurt 检查。
    /// </summary>
    public bool IsInvincible => Time.time < invincibleEndTime;

    /// <summary>
    /// 是否正在受击硬直中。
    /// </summary>
    public bool IsStunned => Time.time < stunEndTime;

    /// <summary>
    /// 受击链路探针（临时）：写项目根 ProbeLogs/damage_chain_log.txt。
    /// </summary>
    private void HurtProbe(string msg)
    {
        if (!hurtProbe) return;
        try
        {
            string root = System.IO.Directory.GetParent(Application.dataPath).FullName;
            string dir = System.IO.Path.Combine(root, "ProbeLogs");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "damage_chain_log.txt");
            System.IO.File.AppendAllText(path, "t=" + Time.time.ToString("0.000") + " [Hurt]" + msg + System.Environment.NewLine);
        }
        catch (System.Exception) { }
    }
}
