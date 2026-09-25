using UnityEngine;
using UnityEngine.InputSystem;

public class TopDownScript : MonoBehaviour
{
    Animator animator;
    Rigidbody rig;
    private Vector2 playerInputVec;
    bool isRunning;
    private Vector3 PlayerMovement;
    
    private float RotateSpeed = 2.0f;
    private float walkSpeed = 1.8f;
    private float runSpeed = 5.0f;
    
    private float targetSpeed;
    private float currentSpeed;
    
    Transform playerTransform;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        animator = GetComponent<Animator>();
        rig = GetComponent<Rigidbody>();
        playerTransform = transform;
    }

    // Update is called once per frame
    void Update()
    {
        Move();
        PlayerRotate();
    }
    
    public void GetPlayerInput(InputAction.CallbackContext ctx)
    {
        playerInputVec = ctx.ReadValue<Vector2>();
    }
    
    //疾跑回调
    public void Run(InputAction.CallbackContext ctx)
    {
        isRunning = ctx.ReadValue<float>() > 0;
    }
    
    private void PlayerRotate()
    {
        if(playerInputVec.Equals(Vector2.zero)) return;
        PlayerMovement.x = playerInputVec.x;
        PlayerMovement.z = playerInputVec.y;//输入y = 世界Z
        
        var targetRotation = Quaternion.LookRotation(PlayerMovement, Vector3.up);
        playerTransform.rotation = Quaternion.Slerp(playerTransform.rotation, targetRotation, RotateSpeed *  Time.deltaTime);

    }

    private void Move()
    {
        targetSpeed = isRunning ? runSpeed : walkSpeed;
        targetSpeed *= playerInputVec.magnitude;
        
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, 5f * Time.deltaTime);//5f为默认平滑度
        animator.SetFloat("Speed", currentSpeed);
    }
}
