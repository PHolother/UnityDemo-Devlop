using System.Reflection;
using Script.Base.Interface;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAttack : MonoBehaviour , IAttackReset
{
    private Animator animator;
    private IMovementProvider movementProvider;
    private Script.Base.BattleAttribute.Stamina stamina;

    private bool wasMoving;
    private bool isAttacking;
    
    private int attackedHash;
    private int turnToMoveHash;
    private int attackedToUnarmHash;
    
    [Header("攻击设置")]
    public float attackBufferedDuration = 0.3f;
    public bool attackBuffered;
    private float attackBufferedStartTime;

    [Header("体力消耗")]
    [Tooltip("各连段攻击消耗体力（A1~A4，由 PlayerConfig.xlsx 的 attackStaminaCost_A1~A4 配置）")]
    public float[] attackStaminaCosts = { 10f, 8f, 6f, 15f };

    [Header("连段伤害")]
    [Tooltip("各连段攻击伤害（A1~A4，由 PlayerConfig.xlsx 的 attackDamage_A1~A4 配置）")]
    public float[] attackDamages = { 12f, 10f, 7.5f, 18f };

    [Header("测试")]
    [Tooltip("攻击慢动作系数：1=正常；0.3=攻击动画放慢到 0.3 倍速（伤害帧事件/刀光窗口同步拉长，便于观察贴合度）。仅影响攻击状态期间，回到移动即恢复。")]
    [Range(0.05f, 1f)] public float attackSlowMo = 1f;

    private Script.Base.BattleAttribute.BattleAttributes battleAttributes;
    // 刀光播放器（可选组件：不挂则完全不影响攻击逻辑）
    private Script.Player.SwordSlashController slashVfx;

    // 攻击段索引（1=A1, 2=A2, 3=A3, 4=A4）
    private int attackIndex = 1;
    // 当前段内已挥刀次数（A3 有 2 次，每次独立扣体力/设伤害）
    private int swingCountInCombo;

    private static readonly int Attack1Hash = Animator.StringToHash("Attack01_1_49f");
    private static readonly int Attack2Hash = Animator.StringToHash("Attack01_2_50f");
    private static readonly int Attack3Hash = Animator.StringToHash("Attack01_3_80f");
    private static readonly int Attack4Hash = Animator.StringToHash("Attack01_4_84f");
    
    void Start()
    {
        animator = GetComponent<Animator>();
        movementProvider = GetComponent<IMovementProvider>();
        // Stamina 组件挂在 BattleAttributes 子物体上（与 Health/Mana 同物体），需在子物体查找
        stamina = GetComponentInChildren<Script.Base.BattleAttribute.Stamina>(true);
        battleAttributes = GetComponentInChildren<Script.Base.BattleAttribute.BattleAttributes>(true);
        slashVfx = GetComponent<Script.Player.SwordSlashController>();
        
        turnToMoveHash = Animator.StringToHash("turnToMove");
        attackedHash = Animator.StringToHash("attacked");
        attackedToUnarmHash = Animator.StringToHash("attackedToUnarm");
    }
    
    void Update()
    {
        if (HitstopManager.Instance != null && HitstopManager.Instance.IsFrozen) return;
        TurnToMove();
        CheckAttackBufferTimeout();
        ApplyAttackBuffer();
    }
    
    public void GetLeftButton(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;
        if (!movementProvider.CanMove) StartAttackBuffer();
        else StartAttack();
    }
    
    private void StartAttack()
    {
        // 判断本次攻击是第几段（由当前 Animator 状态决定：Locomotion→A1，Attack1→A2...）
        DetermineAttackIndex();
        swingCountInCombo = 0;

        // 预检：当前段第一刀体力够不够，不够不能起手（实际消耗在每次挥刀 OnAttackSwing）
        float firstSwingCost = GetStaminaCost(attackIndex);
        if (stamina != null && stamina.GetCurrentStamina() < firstSwingCost)
        {
            return;
        }

        movementProvider.EnableBuffedRotate();
        animator.SetTrigger(attackedHash);
        isAttacking = true;
    }

    /// <summary>
    /// 每次挥刀（EnableHitbox 事件触发）：按当前段消耗体力 + 设置该刀伤害。
    /// A3 两次挥刀 → 每刀独立扣体力(6)和设伤害(7.5)。
    /// 规则：段已起手则刀刀正常（体力不足不倒扣伤害）——起手预检已保证第一刀够扣，
    /// 连段中途体力耗尽的唯一后果是 StartAttack 预检挡住下一段（如 A3 后接不上 A4）。
    /// </summary>
    public void OnAttackSwing()
    {
        swingCountInCombo++;

        // 每刀尝试消耗体力（A3 两次挥刀各扣 6）；不足则本刀不扣，但不影响攻击本身
        float cost = GetStaminaCost(attackIndex);
        if (stamina != null)
        {
            stamina.ConsumeStamina(cost);
        }

        // 伤害始终按段配置设置（段内不因体力不足而削伤）
        if (battleAttributes != null)
        {
            battleAttributes.SetCurrentAttackDamage(GetDamage(attackIndex));
        }

        // 刀光：与本事件严格同步（伤害帧开始 = 刀光出现），时长由预设控制（≈伤害窗口）
        if (slashVfx != null)
        {
            slashVfx.PlaySlash(attackIndex, swingCountInCombo);
        }
    }

    /// <summary>
    /// 编辑器检视用：强制直接播放指定段的攻击状态（segment 1~4；0=按当前连段续）。
    /// 绕开体力预检与输入，动画事件链（DisableControl/EnableHitbox/EnableControl）照常触发，
    /// 供 SwordSlashController 的轨迹录制取到真实伤害帧时刻。
    /// </summary>
    public bool DebugForceAttack(int segment = 0)
    {
        if (animator == null || isAttacking) return false;
        int idx = (segment >= 1 && segment <= 4) ? segment : attackIndex;
        attackIndex = idx;
        swingCountInCombo = 0;
        int hash = idx == 1 ? Attack1Hash : idx == 2 ? Attack2Hash : idx == 3 ? Attack3Hash : Attack4Hash;
        animator.Play(hash, 0, 0f);
        isAttacking = true;
        return true;
    }

    /// <summary>
    /// 根据当前 Animator 状态确定本次攻击段（1=A1...4=A4）。
    /// </summary>
    private void DetermineAttackIndex()
    {
        if (animator == null) return;
        var cur = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        if (cur == Attack1Hash) attackIndex = 2;
        else if (cur == Attack2Hash) attackIndex = 3;
        else if (cur == Attack3Hash) attackIndex = 4;
        else if (cur == Attack4Hash) attackIndex = 1; // A4 后再按 → 回到 A1
        else attackIndex = 1; // Locomotion/其他 → A1
    }

    /// <summary>
    /// 取第 index 段的体力消耗（越界用 A1）。
    /// </summary>
    private float GetStaminaCost(int index)
    {
        if (attackStaminaCosts == null || attackStaminaCosts.Length == 0) return 0f;
        int i = Mathf.Clamp(index - 1, 0, attackStaminaCosts.Length - 1);
        return attackStaminaCosts[i];
    }

    /// <summary>
    /// 取第 index 段的伤害（越界用 A1）。
    /// </summary>
    private float GetDamage(int index)
    {
        if (attackDamages == null || attackDamages.Length == 0) return 0f;
        int i = Mathf.Clamp(index - 1, 0, attackDamages.Length - 1);
        return attackDamages[i];
    }
    
    private void StartAttackBuffer()
    {
        attackBuffered = true;
        attackBufferedStartTime = Time.time;
    }
    
    private void ApplyAttackBuffer()
    {
        if (!attackBuffered || !movementProvider.CanMove) return;
        StartAttack();
        attackBuffered = false;
    }
    
    private void CheckAttackBufferTimeout()
    {
        if (attackBuffered && Time.time >= attackBufferedStartTime + attackBufferedDuration)
            attackBuffered = false;
    }
    
    private void TurnToMove()
    {
        if (movementProvider == null) return;
        
        var isMoving = movementProvider.IfMove();
        if (!wasMoving && isMoving && movementProvider.CanMove && !attackBuffered && !isAttacking)
            animator.SetTrigger(turnToMoveHash);
        wasMoving = isMoving;
    }

    // 攻击自然结束，回到移动
    private void AttackEndToUnarm()
    {
        // 定格 scrub 顺带触发时吞掉：SetTrigger 会在状态机里强行跳出攻击态，毁掉定格
        if (slashVfx != null && slashVfx.inspectReady) return;
        animator.SetTrigger(attackedToUnarmHash);
    }
    
    // 攻击开始，插入在攻击动画第0帧
    public void DisableControl()
    {
        // 检视定格期间被帧步 scrub 顺带触发的动画事件不是真实攻击起点：整体吞掉，
        // 否则会覆写定格用的 animator.speed=0，玩家脱离定格继续播攻击。
        if (slashVfx != null && slashVfx.inspectReady) return;
        animator.ResetTrigger("attacked");
        movementProvider.SetAttackLock(true);   // 攻击锁定（与受击锁独立，不碰受击锁）
        animator.speed = attackSlowMo;          // 测试：攻击期间动画降速（事件时间同步变慢）
        if (slashVfx != null) slashVfx.slowMotion = attackSlowMo; // 刀光存续/跟随窗口按同倍数拉长
    }
    
    // 攻击结束，插入在攻击动画每段结束帧
    public void EnableControl()
    {
        // 同上：定格 scrub 越过结束帧事件时吞掉。退出检视后动画自然播到真正结束帧会再次调用，届时正常收尾。
        if (slashVfx != null && slashVfx.inspectReady) return;
        isAttacking = false;
        movementProvider.SetAttackLock(false);  // 攻击解锁（仅解除攻击锁）
        movementProvider.DisableBuffedRotate();
        wasMoving = false;
        animator.speed = 1f;                    // 恢复常速（非攻击期不受慢动作影响）
    }
    
    // 接口实现
    public void ResetAttack()
    {
        movementProvider.SetAttackLock(false);  // 打断攻击：解除攻击锁
        animator.ResetTrigger("attacked");
    }

    public void ClearAttackBuffer()
    {
        attackBuffered = false;
    }
}