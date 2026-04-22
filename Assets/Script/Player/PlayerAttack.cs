using Script.Base.Interface;
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
    
    void Start()
    {
        animator = GetComponent<Animator>();
        movementProvider = GetComponent<IMovementProvider>();
        
        turnToMoveHash = Animator.StringToHash("turnToMove");
        attackedHash = Animator.StringToHash("attacked");
        attackedToUnarmHash = Animator.StringToHash("attackedToUnarm");
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