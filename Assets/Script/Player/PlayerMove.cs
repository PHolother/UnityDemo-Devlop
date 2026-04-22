using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMove : MonoBehaviour, IMovementProvider , IDodgeProvider
{
    [Header("相机调用")]
    [SerializeField] private Transform cameraTransform;
    private Animator animator;
    private CharacterController controller;
    
    private Vector2 playerInput;
    private Vector3 moveDirection;
    private Vector3 playerMovement;

    [Header("移动速度设置")]
    private bool isSprinting;
    public float runSpeed = 3f;
    public float sprintSpeed = 6f;
    private float targetSpeed;
    private float currentSpeed;
    public float smoothSpeed = 9f;
    private int speedHash = Animator.StringToHash("Speed");
    
    [Header("转向设置")]
    public float rotateSpeed = 5f;
    
    private Vector3 bufferedRotation;
    private bool rotatedAttack;

    void Start()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        UpdateAttackRotate();
        PlayerRotate();
        Move();
    }

    private Vector3 GetCameraInput()
    {
        if(cameraTransform == null) return Vector3.zero;
        
        var forward = cameraTransform.forward;
        var right = cameraTransform.right;
        forward.y = 0;
        forward.Normalize();
        right.y = 0;
        right.Normalize();
        
        var moveDirection =  forward * playerInput.y + right * playerInput.x;
        return moveDirection.normalized;
    }

    // 攻击期间的转向只执行一次
    private void UpdateAttackRotate()
    {
        if (CanRotate) return;
        if (rotatedAttack) return;
        if (playerInput.magnitude < 0.1f) return;
    
        // 计算目标方向
        bufferedRotation = GetCameraInput();
        var targetRotation = Quaternion.LookRotation(bufferedRotation, Vector3.up);
    
        // 快速平滑转向（速度是正常的3-5倍）
        var fastRotateSpeed = rotateSpeed * 4f;  // 可调整：数值越大越快
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, fastRotateSpeed * Time.deltaTime);
    
        // 接近目标角度标记完成
        if (Quaternion.Angle(transform.rotation, targetRotation) < 5f)
        {
            transform.rotation = targetRotation;
            rotatedAttack = true;
        }
    }

    // 正常移动时的转向（平滑转向）
    private void PlayerRotate()
    {
        if (!CanRotate) return;
        if (playerInput.magnitude < 0.1f) return;
        
        moveDirection = GetCameraInput();
        var targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotateSpeed * Time.deltaTime);
    }

    private void Move()
    {
        if (IsDodging) return; // 闪避时不执行 Move
        if (!CanMove) return;
        if (isSprinting && playerInput.magnitude < 0.1f) isSprinting = false;
        
        targetSpeed = (isSprinting ? sprintSpeed : runSpeed) * playerInput.magnitude;
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, smoothSpeed * Time.deltaTime);
        animator.SetFloat(speedHash, currentSpeed);
        
        playerMovement = currentSpeed * moveDirection;
        controller.Move(playerMovement * Time.deltaTime);
    }

    public void GetInput(InputAction.CallbackContext ctx)
    {
        playerInput = ctx.ReadValue<Vector2>();
    }
    
    // 接口实现
    
    public bool CanMove { get; set; } = true;
    public bool CanRotate { get; set; } = true;
    
    public bool IfMove() => playerInput.magnitude > 0.1f;
    public Vector3 GetMoveDirection() => moveDirection;
    
    public float GetCurrentSpeed() => currentSpeed;
    public float GetSprintSpeed() => sprintSpeed;
    public void SetSprintState(bool sprint) => isSprinting = sprint;
    public float GetRotateSpeed() => rotateSpeed;
    
    public void EnableBuffedRotate() => rotatedAttack = false;// 重置，允许下次攻击转向
    public void DisableBuffedRotate() { }
    
    public bool IsDodging { get; }
}