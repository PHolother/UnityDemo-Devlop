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

    /// <summary>
    /// 玩家是否处于闪避/连续闪避（含 DodgeToSprinting 冲刺过渡）状态。
    /// CameraFollowProxy 用此冻结 Proxy Y——闪避/冲刺的下蹲起身动画
    /// （RootT.y -0.055→0.55）会让视角先向下再回正，冻结后视角保持水平。
    /// </summary>
    public bool IsDodging => isDodging;
    
    [SerializeField] private float inputBufferedDuration = 0.35f;
    private float nextDodgeTime;
    [Tooltip("闪避与闪避之间的固定冷却（秒）：防止狂点闪避键连续闪避（由 PlayerConfig.xlsx 的 dodgeCooldown 配置）")]
    [SerializeField] private float dodgeCooldown = 1.5f;

    [Header("体力消耗")]
    [Tooltip("每次闪避消耗体力（由 PlayerConfig.xlsx 的 dodgeStaminaCost 配置）")]
    public float dodgeStaminaCost = 10f;

    private Script.Base.BattleAttribute.Stamina stamina;
    
    private int dodgeHash;
    
    void Start()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        movementProvider = GetComponent<IMovementProvider>();
        // Stamina 组件挂在 BattleAttributes 子物体上（与 Health/Mana 同物体），需在子物体查找
        stamina = GetComponentInChildren<Script.Base.BattleAttribute.Stamina>(true);
        dodgeHash = Animator.StringToHash("isDodging");
    }
    
    void Update()
    {
        if (HitstopManager.Instance != null && HitstopManager.Instance.IsFrozen) return;
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
        // 体力不足时不能闪避
        if (stamina != null && !stamina.ConsumeStamina(dodgeStaminaCost))
        {
            return;
        }

        // 攻击被闪避打断：解除攻击锁（不碰受击锁——受击中不能闪避）
        movementProvider.SetAttackLock(false);

        var attackReset = GetComponent<IAttackReset>();
        attackReset.ResetAttack();
        attackReset.ClearAttackBuffer();
        
        if (isInputBuffered)
        {
            animator.CrossFadeInFixedTime("DodgeToSprinting", 13/41f);
        }
        
        isDodging = true;
        animator.SetBool(dodgeHash, isDodging);
        movementProvider.SetSprintState(true);
    }
    
    // 闪避动画结束调用
    public void OnDodgeEnd()
    {
        isInputBuffered = false;
        isDodging = false;
        animator.SetBool(dodgeHash, isDodging);
        // 闪避与闪避之间的固定冷却（防狂点）：动画结束后开始计时
        nextDodgeTime = Time.time + dodgeCooldown;
    }
}
