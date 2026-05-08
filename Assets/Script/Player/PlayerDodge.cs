using Script.Base.Interface;
using Script.Base.BattleAttribute;
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
    
    [Header("体力消耗配置")]
    [Tooltip("闪避消耗的体力值")]
    [SerializeField] private int dodgeStaminaCost = 20;
    
    // 体力组件引用
    private Stamina stamina;
    
    void Start()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        movementProvider = GetComponent<IMovementProvider>();
        dodgeHash = Animator.StringToHash("isDodging");
        
        // 获取体力组件
        stamina = GetComponentInChildren<Stamina>();
        
        if (stamina == null)
        {
            Debug.LogWarning("[PlayerDodge] 未找到Stamina组件，闪避将不消耗体力", this);
        }
        else
        {
            Debug.Log($"[PlayerDodge] 成功获取Stamina组件，闪避消耗: {dodgeStaminaCost}", this);
        }
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
        // 检查体力是否为0
        if (stamina != null && stamina.GetCurrentStamina() <= 0)
        {
            Debug.Log("[PlayerDodge] 体力耗尽，无法闪避");
            return; // 体力为0，不能闪避
        }
        
        // 尝试消耗体力（即使不足也会消耗到0）
        if (stamina != null)
        {
            bool consumed = stamina.ConsumeStamina(dodgeStaminaCost);
            if (!consumed)
            {
                Debug.Log("[PlayerDodge] 体力不足，无法闪避");
                return;
            }
            Debug.Log($"[PlayerDodge] 消耗体力: {dodgeStaminaCost}, 剩余: {stamina.GetCurrentStamina()}");
        }
        
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