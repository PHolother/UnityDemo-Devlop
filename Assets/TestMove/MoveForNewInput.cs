using UnityEngine;
using UnityEngine.InputSystem;
public class MoveForNewInput : MonoBehaviour
{
    Animator animator;
    float forwardSpeed = 1.64f;
    float backSpeed = -1.64f;
    float targetSpeed;
    float currentSpeed;
    private Rigidbody rig;
    
    Vector3 movement;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        animator = GetComponent<Animator>();
        animator.SetFloat("ScaleFactor", 1 / animator.humanScale);
        rig = GetComponent<Rigidbody>();
        
    }

    // Update is called once per frame
    void Update()
    {

    }
    void OnAnimatorMove()
    {
        Move();
    }

    void Move()
    {
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, Time.deltaTime * 5f);
        animator.SetFloat("Speed", currentSpeed);
        rig.linearVelocity = animator.velocity;
    }

    public void PlayerMove(InputAction.CallbackContext context)
    {
        Vector2 currentMove = context.ReadValue<Vector2>();
        targetSpeed = currentMove.y > 0 ? currentMove.y * forwardSpeed : currentMove.y * -backSpeed;
    }
}
