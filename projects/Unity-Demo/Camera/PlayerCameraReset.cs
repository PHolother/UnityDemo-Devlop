using System;
using Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCameraReset : MonoBehaviour
{
    private CinemachineFreeLook freeLook;
    private Transform playerTransform;
    
    private bool isResetting;
    private float resetTimer;
    private float startX;
    private float startY;
    
    [Header("回正设置")]
    private float targetX;
    [SerializeField] private float targetY = 0.5f;
    [SerializeField] private float resetSpeed = 8f;
    
    [SerializeField] private InputActionReference lookAction;
    
    private void Start()
    {
        freeLook = GetComponent<CinemachineFreeLook>();
        playerTransform = GameObject.FindGameObjectWithTag("Player").transform;
    }

    void Update()
    {
        GetMiddleButton();
        IfReset();
    }
    
    public void GetMiddleButton()
    {
        if (Mouse.current?.middleButton.wasPressedThisFrame == true)
        {
            StartResetCamera();
        }
    }

    private void StartResetCamera()
    {
        targetX = CalculateTargetX();
        isResetting = true;
        resetTimer = 0f;
        startX = freeLook.m_XAxis.Value;
        startY = freeLook.m_YAxis.Value;
    }

    private void UpdateResetCamera()
    {
        resetTimer += Time.deltaTime * resetSpeed;
        
        freeLook.m_XAxis.Value = Mathf.Lerp(startX, targetX, resetTimer);
        freeLook.m_YAxis.Value = Mathf.Lerp(startY, targetY, resetTimer);

        if (resetTimer < 1f) return;
        
        isResetting = false;
        freeLook.m_XAxis.Value = targetX;
        freeLook.m_YAxis.Value = targetY;
    }

    private void IfReset()
    {
        // 正常视角控制
        if (!isResetting)
        {
            var lookDelta = lookAction.action.ReadValue<Vector2>();
            freeLook.m_XAxis.m_InputAxisValue = lookDelta.x;
            freeLook.m_YAxis.m_InputAxisValue = lookDelta.y;
        }
        
        // 回正动画
        if (isResetting)
        {
            UpdateResetCamera();
        }
    }

    private float CalculateTargetX()
    {
        var playerDirection = Mathf.Atan2(playerTransform.forward.x, playerTransform.forward.z) * Mathf.Rad2Deg;
        var playerBackDirection = playerDirection + 180f;
    
        // 计算当前相机位置的角度
        var cameraOffset = freeLook.transform.position - playerTransform.position;
        var cameraAngle = Mathf.Atan2(cameraOffset.x, cameraOffset.z) * Mathf.Rad2Deg;
        var currentX = freeLook.m_XAxis.Value;
    
        // 反推 X=0 对应的角度
        var zeroAngle = cameraAngle - currentX * 360f;
    
        // 计算目标
        var targetX = (playerBackDirection - zeroAngle) / 360f;
        
        return Mathf.Repeat(targetX, 1f);
    }
}