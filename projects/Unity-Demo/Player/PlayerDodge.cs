using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

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
    private int dodgeCount = 0;
    private float nextDodgeTime;
    [SerializeField] private float dodgeCooldown = 1.5f;
    
    [Header("闪避碰撞体参数")]
    [SerializeField] private float dodgeSkinWidth = 0.001f;
    [SerializeField] private float dodgeStepOffset = 0f;
    [SerializeField] private float dodgeHeight = 0.3f;
    [SerializeField] private float dodgeCenterYOffset = 0f;   // 中心点Y偏移
    
    private float originalSkinWidth;
    private float originalStepOffset;
    private Vector3 originalCenter;
    private float originalHeight;
    
    private int dodgeHash;
    
    void Start()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        movementProvider = GetComponent<IMovementProvider>();
        dodgeHash = Animator.StringToHash("isDodging");
        ControllerParametersSave();
        
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
        ControllerParametersAlter();
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
        ControllerParametersRecover();
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

    private void ControllerParametersSave()
    {
        if (controller == null) return;
        originalSkinWidth = controller.skinWidth;
        originalStepOffset = controller.stepOffset;
        originalCenter = controller.center;
        originalHeight = controller.height;
    }

    private void ControllerParametersAlter()
    {
        // 调整碰撞体参数
        if (controller == null) return;
        controller.skinWidth = dodgeSkinWidth;
        controller.stepOffset = dodgeStepOffset;
        controller.height = dodgeHeight;
        controller.center = new Vector3(originalCenter.x, dodgeHeight * 0.5f, originalCenter.z);
    }

    private void ControllerParametersRecover()
    {
        // 恢复碰撞体参数
        if (controller == null) return;
        controller.skinWidth = originalSkinWidth;
        controller.stepOffset = originalStepOffset;
        controller.height = originalHeight;
        controller.center = originalCenter;
    }
}