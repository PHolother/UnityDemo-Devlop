using UnityEngine;
using UnityEngine.InputSystem;
public class DebugMove : MonoBehaviour
{
    Animator animator;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        animator = GetComponent<Animator>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void PlayerMove(InputAction.CallbackContext context)
    {
        Vector2 currentMove = context.ReadValue<Vector2>();
        Debug.Log(currentMove);
    }
}