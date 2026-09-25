using UnityEngine;
using UnityEngine.InputSystem;

public class TestMove : MonoBehaviour
{
    private bool isForward;
    private bool isBack;
    private bool isLeft;
    private bool isRight;
    private bool isUp;
    private bool isDown;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
    
    }

    // Update is called once per frame
    void Update()
    {
      GetPlayerInput();
      MoveCube();
      
    }
    
    private void GetPlayerInput()
    {
       isForward = Keyboard.current.wKey.isPressed;
       isBack = Keyboard.current.sKey.isPressed;
       isLeft = Keyboard.current.aKey.isPressed;
       isRight = Keyboard.current.dKey.isPressed;
       isUp = Keyboard.current.spaceKey.isPressed;
       isDown = Keyboard.current.shiftKey.isPressed;
    }

    private void MoveCube()
    {
        if (isForward)
        {
            transform.Translate(Vector3.forward *  Time.deltaTime);
        }
        if (isBack)
        {
            transform.Translate(Vector3.back *  Time.deltaTime);
        }
        if (isLeft)
        {
            transform.Translate(Vector3.left *  Time.deltaTime);
        }

        if (isRight)
        {
            transform.Translate(Vector3.right *  Time.deltaTime);
        }

        if (isUp)
        {
            transform.Translate(Vector3.up *  Time.deltaTime);
        }

        if (isDown)
        {
            transform.Translate(Vector3.down *  Time.deltaTime);
        }
        
    }
}
