using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMouseLockAndLocked : MonoBehaviour
{
    
    private bool mouseLocked;
    private bool pressMiddleButton;
    void Start()
    {
        LockMouse();
    }

    void Update()
    {
       IfRelease();
    }
    
    private void IfRelease()
    {
        if (!Keyboard.current.escapeKey.wasPressedThisFrame) return;
        if (mouseLocked) ReleaseMouse();
        else LockMouse();
    }
    private void ReleaseMouse()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        mouseLocked = false;
    }
    
    private void LockMouse()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        mouseLocked = true;
    }
    
    
}