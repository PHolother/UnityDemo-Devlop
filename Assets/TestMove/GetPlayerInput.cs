using UnityEngine;
using UnityEngine.InputSystem;

public class GetPlayerInput : MonoBehaviour
{
    public bool isForward;
    public bool isBack;
    public bool isLeft;
    public bool isRight;
    public bool isUp;
    public bool isDown;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        isForward = Keyboard.current.wKey.isPressed;
        isBack = Keyboard.current.sKey.isPressed;
        isLeft = Keyboard.current.aKey.isPressed;
        isRight = Keyboard.current.dKey.isPressed;
        isUp = Keyboard.current.spaceKey.isPressed;
        isDown = Keyboard.current.shiftKey.isPressed;
    }
}
