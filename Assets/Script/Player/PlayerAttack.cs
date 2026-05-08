using Script.Base.Interface;
using Script.Base.BattleAttribute;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAttack : MonoBehaviour , IAttackReset
{
    private Animator animator;
    private IMovementProvider movementProvider;

    private bool wasMoving;
    private bool isAttacking;
    
    private int attackedHash;
    private int turnToMoveHash;
    private int attackedToUnarmHash;
    
    [Header("攻击设置")]
    public float attackBufferedDuration = 0.3f;
    public bool attackBuffered;
    private float attackBufferedStartTime;
    
    [Header("攻击消耗配置")]
    [Tooltip("普通攻击消耗的体力值")]
    [SerializeField] private int attackStaminaCost = 10;
    
    // 属性组件引用
    private Stamina stamina;
    
    void Start()
    {
        animator = GetComponent<Animator>();
        movementProvider = GetComponent<IMovementProvider>();
        
        turnToMoveHash = Animator.StringToHash("turnToMove");
        attackedHash = Animator.StringToHash("attacked");
        attackedToUnarmHash = Animator.StringToHash("attackedToUnarm");
        
        // 获取体力组件
        stamina = GetComponentInChildren<Stamina>();
        
        if (stamina == null)
        {
            Debug.Log("[PlayerAttack] 未找到Stamina组件，攻击将不消耗体力", this);
        }
        else
        {
            Debug.Log($"[PlayerAttack] 成功获取Stamina组件，攻击消耗: {attackStaminaCost}", this);
        }
    }
    
    void Update()
    {
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
        // 检查体力是否为0
        if (stamina != null && stamina.GetCurrentStamina() <= 0)
        {
            Debug.Log("[PlayerAttack] 体力耗尽，无法攻击");
            return;
        }
        
        // 消耗体力
        if (stamina != null)
        {
            stamina.ConsumeStamina(attackStaminaCost);
            Debug.Log($"[PlayerAttack] 消耗体力: {attackStaminaCost}, 剩余: {stamina.GetCurrentStamina()}");
        }
        
        movementProvider.EnableBuffedRotate();// 允许预输入攻击转向
        animator.SetTrigger(attackedHash);
        isAttacking = true;
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
    private void AttackEndToUnarm() => animator.SetTrigger(attackedToUnarmHash);
    
    // 攻击开始，插入在攻击动画第0帧
    public void DisableControl()
    {
        animator.ResetTrigger("attacked");
        movementProvider.CanMove = false;
        movementProvider.CanRotate = false;
    }
    
    // 攻击结束，插入在攻击动画每段结束帧
    public void EnableControl()
    {
        isAttacking = false;
        movementProvider.CanMove = true;
        movementProvider.CanRotate = true;
        movementProvider.DisableBuffedRotate();
        wasMoving = false;
    }
    
    // 接口实现
    public void ResetAttack()
    {
        movementProvider.CanMove = true;
        movementProvider.CanRotate = true;
        animator.ResetTrigger("attacked");
    }

    public void ClearAttackBuffer()
    {
        attackBuffered = false;
    }
}