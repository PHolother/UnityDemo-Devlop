using Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 第三人称相机横向偏移系统
/// 通过 Cinemachine Composer 的 Bias X 实现玩家在屏幕中的左右偏离
/// </summary>
public class PlayerCameraFollow : MonoBehaviour
{
    [Header("Cinemachine 引用")]
    [SerializeField] private CinemachineFreeLook freeLook;
    [SerializeField] private Transform playerTransform;
    
    [Header("相机偏移设置")]
    [Tooltip("偏离幅度系数（控制 Bias X 的偏移强度）")]
    [SerializeField] private float biasMultiplier = 2.0f;
    
    [Tooltip("Soft Zone 动态系数（偏移越大，Soft Zone 越大，偏移开始越晚但幅度越大）")]
    [Range(0f, 1f)]
    [SerializeField] private float softZoneMultiplier = 0.5f;
    
    [Tooltip("基础 Soft Zone 宽度（无偏移时的默认值）")]
    [Range(0f, 1f)]
    [SerializeField] private float baseSoftZoneWidth = 0.5f;
    
    [Tooltip("斜向偏移增强系数（斜向移动时额外放大的倍数，1.0 表示不增强）")]
    [Range(1f, 3f)]
    [SerializeField] private float diagonalOffsetBoost = 1.5f;
    
    [Header("输入设置")]
    [Tooltip("玩家移动输入Action")]
    [SerializeField] private InputActionReference moveAction;
    
    [Header("死区设置")]
    [Tooltip("横向移动死区阈值，小于此值的输入被过滤")]
    [Range(0f, 0.5f)]
    [SerializeField] private float horizontalDeadzone = 0.15f;
    
    [Header("偏离范围设置")]
    [Tooltip("最大横向偏离幅度（屏幕左右百分比位置，0.2-0.3推荐）")]
    [Range(0f, 0.5f)]
    [SerializeField] private float maxHorizontalOffset = 0.25f;
    
    [Header("偏离动画曲线")]
    [Tooltip("控制偏离过程的快慢节奏\nX轴：时间进度0-1\nY轴：偏离完成度0-1\n推荐：起始陡峭（快速），末期平缓（慢速接近边界）")]
    [SerializeField] private AnimationCurve offsetEnterCurve;
    
    [Header("回正动画曲线")]
    [Tooltip("控制回正过程的快慢节奏\nX轴：时间进度0-1\nY轴：回正完成度0-1\n推荐：起始陡峭（快速离开），末期极平缓（精确对齐中心）")]
    [SerializeField] private AnimationCurve offsetExitCurve;
    
    [Header("偏离速度设置")]
    [Tooltip("开始偏离时的持续时间（秒），建议0.6-1.0秒")]
    [SerializeField] private float offsetEnterDuration = 0.8f;
    
    [Tooltip("回正到中心的持续时间（秒），建议1.2-1.8秒")]
    [SerializeField] private float offsetExitDuration = 1.5f;
    

    // 内部状态
    private float currentHorizontalOffset;
    private float targetHorizontalOffset;
    
    // 动画进度跟踪
    private float lastTargetOffset;
    private float startOffset;
    private float animationStartTime;
    
    private void Start()
    {
        if (freeLook == null)
        {
            freeLook = GetComponent<CinemachineFreeLook>();
        }
        
        if (playerTransform == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerTransform = player.transform;
            }
        }
        
        // 设置 FreeLook 的 Follow 和 LookAt 目标
        if (freeLook != null)
        {
            freeLook.Follow = playerTransform;
        }
        
        // 初始化默认曲线：偏离时快速启动、缓慢接近边界
        if (offsetEnterCurve == null || offsetEnterCurve.length == 0)
        {
            offsetEnterCurve = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 3f),      // 起始快速响应
                new Keyframe(0.5f, 0.75f, 1.5f, 0.3f), // 中期减速
                new Keyframe(1f, 1f, 0f, 0f)         // 末期极慢，渐近边界
            );
        }
        
        // 初始化默认曲线：回正时快速离开、缓慢精确对齐
        if (offsetExitCurve == null || offsetExitCurve.length == 0)
        {
            offsetExitCurve = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 4f),        // 起始极快速离开边界
                new Keyframe(0.4f, 0.7f, 1.2f, 0.15f), // 中期大幅减速
                new Keyframe(1f, 1f, 0f, 0f)         // 末期极慢，精确对齐中心
            );
        }
        
        currentHorizontalOffset = 0f;
        targetHorizontalOffset = 0f;
        lastTargetOffset = 0f;
    }
    
    private void Update()
    {
        DetectHorizontalMovement();
        UpdateHorizontalOffset();
        ApplyCameraOffset();
    }
    
    /// <summary>
    /// 检测移动输入并计算目标偏移量
    /// </summary>
    private void DetectHorizontalMovement()
    {
        if (moveAction == null || moveAction.action == null) return;
        
        Vector2 moveInput = moveAction.action.ReadValue<Vector2>();
        float horizontalInput = moveInput.x;
        float verticalInput = moveInput.y;
        
        // 斜向移动增强
        float diagonalBoost = (Mathf.Abs(horizontalInput) > 0.01f && Mathf.Abs(verticalInput) > 0.01f) 
            ? diagonalOffsetBoost : 1f;
        
        // 输入归一化
        if (moveInput.magnitude > 0.01f)
        {
            // 将输入除以移动向量的模长，恢复完整的分量
            horizontalInput = horizontalInput / moveInput.magnitude;
            verticalInput = verticalInput / moveInput.magnitude;
        }
        
        // 横向偏移计算
        if (Mathf.Abs(horizontalInput) < horizontalDeadzone)
        {
            targetHorizontalOffset = 0f;
        }
        else
        {
            float normalizedInput = Mathf.InverseLerp(horizontalDeadzone, 1f, Mathf.Abs(horizontalInput));
            targetHorizontalOffset = Mathf.Sign(horizontalInput) * maxHorizontalOffset * normalizedInput * diagonalBoost;
        }
    }
    
    /// <summary>
    /// 使用动画曲线更新横向偏移量
    /// </summary>
    private void UpdateHorizontalOffset()
    {
        // 检测目标变化
        if (Mathf.Abs(targetHorizontalOffset - lastTargetOffset) > 0.01f)
        {
            startOffset = currentHorizontalOffset;
            animationStartTime = Time.time;
            lastTargetOffset = targetHorizontalOffset;
        }
        
        // 差距过小直接赋值
        if (Mathf.Abs(targetHorizontalOffset - currentHorizontalOffset) < 0.001f)
        {
            currentHorizontalOffset = targetHorizontalOffset;
            return;
        }
        
        // 选择动画曲线和持续时间
        bool isEntering = Mathf.Abs(targetHorizontalOffset) > Mathf.Abs(currentHorizontalOffset);
        float duration = isEntering ? offsetEnterDuration : offsetExitDuration;
        AnimationCurve curve = isEntering ? offsetEnterCurve : offsetExitCurve;
        
        // 计算插值进度
        float progress = Mathf.Clamp01((Time.time - animationStartTime) / duration);
        float curvedProgress = curve.Evaluate(progress);
        currentHorizontalOffset = Mathf.Lerp(startOffset, targetHorizontalOffset, curvedProgress);
    }
    
    /// <summary>
    /// 应用偏移到 Cinemachine Composer 的 Bias X 和 Soft Zone
    /// </summary>
    private void ApplyCameraOffset()
    {
        if (freeLook == null) return;

        float targetBiasX = currentHorizontalOffset * biasMultiplier;
        float targetSoftZone = baseSoftZoneWidth + (Mathf.Abs(currentHorizontalOffset) / maxHorizontalOffset) * softZoneMultiplier;
        
        // 同步更新三个 Rig
        for (int i = 0; i < 3; i++)
        {
            var rig = freeLook.GetRig(i);
            if (rig == null) continue;
            
            var composer = rig.GetCinemachineComponent<CinemachineComposer>();
            if (composer == null) continue;

            composer.m_BiasX = targetBiasX;
            composer.m_SoftZoneWidth = targetSoftZone;
        }
    }
    

}
