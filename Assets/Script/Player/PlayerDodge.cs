using Script.Base.Interface;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerDodge : MonoBehaviour
{
    private Animator animator;
    private IMovementProvider movementProvider;
    private CharacterController controller;

    [Header("闪避")]
    private bool isDodging;
    private bool isInputBuffered;
    private float inputBufferedStartTime;
    
    [SerializeField] private float inputBufferedDuration = 0.35f;
    [SerializeField] private int maxDodgeCount = 2;
    private int dodgeCount;
    private float nextDodgeTime;
    [SerializeField] private float dodgeCooldown = 1.5f;
    
    private int dodgeHash;
    
    void Start()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        movementProvider = GetComponent<IMovementProvider>();
        dodgeHash = Animator.StringToHash("isDodging");
        
    }
    
    void Update()
    {
        CheckInputBufferTimeout();
    }
    
    public void GetShift(InputAction.CallbackContext ctx)
    {
        if (!ctx.started) return;
        if (Time.time < nextDodgeTime) return;
        
        // 已经在闪避
        if (isDodging)
        {
            isInputBuffered = true;
            inputBufferedStartTime = Time.time;
            return;
        }
        StartDodge();
    }
    
    private void CheckInputBufferTimeout()
    {
        if(isInputBuffered && Time.time >= inputBufferedStartTime + inputBufferedDuration) 
            isInputBuffered = false;
    }

    private void StartDodge()
    {
        //攻击被闪避打断处理
        movementProvider.CanMove = true;
        movementProvider.CanRotate = true;

        var attackReset = GetComponent<IAttackReset>();
        attackReset.ResetAttack();
        attackReset.ClearAttackBuffer();
        
        if (isInputBuffered)
        {
            animator.CrossFadeInFixedTime("DodgeToSprinting", 13/41f);
        }
        
        isDodging = true;
        animator.SetBool(dodgeHash, isDodging);
        dodgeCount++;
        movementProvider.SetSprintState(true);
    }
    
    // 闪避动画结束调用
    public void OnDodgeEnd()
    {
        if (isInputBuffered && dodgeCount < maxDodgeCount)
        {
            StartDodge();
            isInputBuffered = false;
            return;
        }

        isInputBuffered = false;
        isDodging = false;
        animator.SetBool(dodgeHash, isDodging);
        
        if (dodgeCount < maxDodgeCount) return;
        nextDodgeTime = Time.time + dodgeCooldown;
        dodgeCount = 0;
    }
}