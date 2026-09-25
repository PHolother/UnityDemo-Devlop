using Script.Base.Interface.Battle;
using UnityEngine;
using UnityEngine.InputSystem;

public class TargetingSystem : MonoBehaviour
{
    [Header("索敌范围设置")]
    [Tooltip("自动索敌检测距离")]
    [SerializeField] private float autoDetectRange = 25f;

    [Tooltip("超出此距离丢失目标")]
    [SerializeField] private float loseTargetRange = 35f;

    [Tooltip("自动索敌检测间隔（秒）")]
    [SerializeField] private float autoDetectInterval = 0.3f;

    [Header("锁定锚点")]
    [Tooltip("锁定锚点相对目标根的世界高度（米）。BOSS 视觉躯干中心≈1.6m，可运行时/Inspector 微调")]
    [SerializeField] private float lockPointFallbackHeight = 1.6f;
    [Tooltip("运行中是否跟随 Inspector 实时调整锚点高度（便于手动校准）")]
    [SerializeField] private bool liveAdjustLockHeight = true;

    /// <summary>
    /// 锁定锚点：目标的躯干位置（spine 骨骼；找不到时挂根+回退高度）。
    /// 相机取景与锁定图标用这个点——用根位置（脚底）会让取景垂直修正
    /// 与 Composer 注视高度互相拉扯（表现为相机周期性下弹再回弹）。
    /// </summary>
    public Transform LockPoint { get; private set; }

    public Transform CurrentTarget { get; private set; }
    public bool HasTarget => CurrentTarget != null;

    private bool hasAutoLocked;
    private TargetIndicator activeIndicator;
    private float autoDetectTimer;

    private void Update()
    {
        if (HitstopManager.Instance != null && HitstopManager.Instance.IsFrozen) return;

        // 实时校准锚点高度：Inspector 里改 lockPointFallbackHeight 立即生效
        if (liveAdjustLockHeight && HasTarget && LockPoint != null)
        {
            float parentScaleY = Mathf.Max(Mathf.Abs(CurrentTarget.lossyScale.y), 0.0001f);
            LockPoint.localPosition = new Vector3(0f, lockPointFallbackHeight / parentScaleY, 0f);
        }

        if (HasTarget)
        {
            if (!ValidateTarget())
            {
                ClearTarget();
                return;
            }
        }
        else if (!hasAutoLocked)
        {
            autoDetectTimer -= Time.deltaTime;
            if (autoDetectTimer <= 0f)
            {
                autoDetectTimer = autoDetectInterval;
                var nearest = FindNearestEnemy(autoDetectRange);
                if (nearest != null)
                    SetTarget(nearest, true);
            }
        }

        if (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame)
            ToggleManualLock();
    }

    public void HandleMiddleClick(InputAction.CallbackContext ctx)
    {
        if (ctx.performed)
            ToggleManualLock();
    }

    private void ToggleManualLock()
    {
        if (HasTarget)
        {
            ClearTarget();
        }
        else
        {
            var nearest = FindNearestEnemy(autoDetectRange);
            if (nearest != null)
                SetTarget(nearest, true);
        }
    }

    private Transform FindNearestEnemy(float maxRange)
    {
        var enemies = GameObject.FindGameObjectsWithTag("Enemy");
        if (enemies.Length == 0) return null;

        Transform nearest = null;
        float nearestSqrDist = maxRange * maxRange;

        foreach (var enemy in enemies)
        {
            if (enemy == null) continue;

            var damageable = enemy.GetComponentInChildren<IDamageable>();
            if (damageable == null || damageable.IsDead()) continue;

            float sqrDist = (enemy.transform.position - transform.position).sqrMagnitude;
            if (sqrDist < nearestSqrDist)
            {
                nearestSqrDist = sqrDist;
                nearest = enemy.transform;
            }
        }

        return nearest;
    }

    private bool ValidateTarget()
    {
        if (CurrentTarget == null) return false;

        var damageable = CurrentTarget.GetComponentInChildren<IDamageable>();
        if (damageable == null || damageable.IsDead()) return false;

        float sqrDist = (CurrentTarget.position - transform.position).sqrMagnitude;
        if (sqrDist > loseTargetRange * loseTargetRange) return false;

        return true;
    }

    private void SetTarget(Transform target, bool manual)
    {
        CurrentTarget = target;
        hasAutoLocked = true;
        LockPoint = FindOrCreateLockPoint(target);

        if (activeIndicator != null)
            activeIndicator.Hide();

        var indicator = TargetIndicator.GetOrCreate();
        indicator.Show(LockPoint);
        activeIndicator = indicator;
    }

    private void ClearTarget()
    {
        CurrentTarget = null;
        LockPoint = null;

        if (activeIndicator != null)
        {
            activeIndicator.Hide();
            activeIndicator = null;
        }

        autoDetectTimer = 0f;
    }

    /// <summary>
    /// 查找目标的躯干锚点：在目标根下创建 "LockPoint" 子物体（根 + 锁定高度）。
    /// 故意不用 spine 骨骼——攻击动画中躯干上下大幅摆动（RootT.y 0.2~3.3），
    /// 取景/图标跟着骨骼会剧烈跳动；固定根+高度与动画解耦，取景稳定。
    /// </summary>
    private Transform FindOrCreateLockPoint(Transform target)
    {
        if (target == null) return null;

        var existing = target.Find("LockPoint");
        if (existing != null) return existing;

        var go = new GameObject("LockPoint");
        go.transform.SetParent(target, false);
        // 父级（BOSS 根）lossyScale=1.8 会放大 localPosition——用世界高度反推本地值，
        // 保证 LockPoint 的世界高度 = 目标根世界高度 + lockPointFallbackHeight
        float parentScaleY = Mathf.Max(Mathf.Abs(target.lossyScale.y), 0.0001f);
        go.transform.localPosition = new Vector3(0f, lockPointFallbackHeight / parentScaleY, 0f);
        return go.transform;
    }
}
