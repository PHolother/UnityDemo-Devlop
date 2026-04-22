using UnityEngine;

public class GravityScript : MonoBehaviour , IDodgeProvider
{
    private bool isDodging;
    // 接口实现
    public bool IsDodging
    {
        get => isDodging;
        set => isDodging = value;
    }

    private CharacterController controller;
    private Animator animator;
    private bool isGrounded;
    private float gravity = -9.81f;
    private float verticalVelocity = 0f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        ApplyGravity();
    }

    public void ApplyGravity()
    {
        isGrounded = controller.isGrounded;
        if(isGrounded && verticalVelocity < 0f) verticalVelocity = -0.1f;// 防止角色砸穿地板，使角色始终在地板上
        if(isGrounded || isDodging) return;
        verticalVelocity += gravity * Time.deltaTime;// 注意符号，重力加速度本身为负
        var verticalMove = new Vector3(0, verticalVelocity, 0);
        controller.Move(verticalMove * Time.deltaTime);
    }
}
