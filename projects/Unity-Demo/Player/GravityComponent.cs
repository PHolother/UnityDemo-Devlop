using UnityEngine;

public class GravityScript : MonoBehaviour , IDodgeProvider
{
    private bool isDodging;
    public bool IsDodging => isDodging;
    private CharacterController controller;
    private Animator animator;
    private PlayerMove playermove;
    private bool isGrounded;
    private float gravity = -9.81f;
    private float verticalVelocity = 0f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        controller = GetComponent<CharacterController>();
        playermove = GetComponent<PlayerMove>();
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
