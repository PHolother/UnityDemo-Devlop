using UnityEngine;
using Script.Base.Interface.Battle;

/// <summary>
/// 战斗事件处理器示例
/// 接收HitboxController的OnHit事件，处理伤害应用、音效等逻辑
/// 挂载在场景中的空物体上
/// </summary>
public class CombatHandler : MonoBehaviour
{
    [Header("音效引用")]
    [Tooltip("命中音效")]
    public AudioClip hitSound;

    [Header("命中停顿配置")]
    [Tooltip("命中停顿时长时间（秒）")]
    public float hitStopDuration = 0.1f;

    [Tooltip("命中停顿时的时间缩放比例（0-1之间）")]
    public float hitStopTimeScale = 0.1f;

    // 玩家Animator引用
    private Animator playerAnimator;

    // 是否正在命中停顿中
    private bool isHitStopActive = false;

    // 命中停顿计时器
    private float hitStopTimer = 0f;

    private void Awake()
    {
        // 查找玩家Animator组件
        playerAnimator = GetComponentInChildren<Animator>();
        if (playerAnimator == null)
        {
            Debug.LogWarning("[CombatHandler] 未找到Animator组件，命中停顿功能可能无法正常工作", this);
        }
    }

    private void Update()
    {
        // 处理命中停顿恢复
        if (isHitStopActive)
        {
            hitStopTimer += Time.unscaledDeltaTime;

            if (hitStopTimer >= hitStopDuration)
            {
                // 恢复正常时间流速
                Time.timeScale = 1f;
                isHitStopActive = false;
                hitStopTimer = 0f;

                // 恢复玩家Animator速度
                if (playerAnimator != null)
                {
                    playerAnimator.speed = 1f;
                }

                Debug.Log("[CombatHandler] 命中停顿结束，恢复正常速度");
            }
        }
    }

    /// <summary>
    /// HitboxController的OnHit事件绑定此方法
    /// 参数：attacker, battleAttribute, targetRoot, damage
    /// </summary>
    public void HandleHit(GameObject attacker, GameObject battleAttribute, GameObject target, int damage)
    {
        Debug.Log($"[CombatHandler] 收到命中事件 - 攻击者: {attacker.name}, 目标: {target.name}, 伤害: {damage}");

        // 1. 应用伤害到目标
        ApplyDamage(target, damage);

        // 2. 播放命中音效
        PlayHitSound();

        // 3. 触发命中停顿
        TriggerHitStop();

        // 注意：武器挥舞特效由武器自身控制，不在这里处理
    }

    /// <summary>
    /// 对目标应用伤害
    /// </summary>
    private void ApplyDamage(GameObject target, int damage)
    {
        // 方式1：优先使用IDamageable接口（在子物体中查找）
        IDamageable damageable = target.GetComponentInChildren<IDamageable>();
        if (damageable != null)
        {
            bool success = damageable.TakeDamage(damage);
            if (success)
            {
                Debug.Log($"[CombatHandler] 通过IDamageable接口成功对 {target.name} 造成 {damage} 点伤害");
            }
            else
            {
                Debug.LogWarning($"[CombatHandler] 对 {target.name} 造成伤害失败（可能已死亡或无效伤害）");
            }
            return;
        }

        // 方式2：降级到SendMessage调用目标的TakeDamage方法
        target.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
        Debug.Log($"[CombatHandler] 通过SendMessage对 {target.name} 造成 {damage} 点伤害");
    }

    /// <summary>
    /// 播放命中音效
    /// </summary>
    private void PlayHitSound()
    {
        if (hitSound != null)
        {
            AudioSource.PlayClipAtPoint(hitSound, transform.position);
        }
    }

    /// <summary>
    /// 触发命中停顿效果
    /// </summary>
    private void TriggerHitStop()
    {
        if (hitStopDuration <= 0f)
        {
            return; // 未配置命中停顿，直接返回
        }

        // 设置全局时间缩放
        Time.timeScale = hitStopTimeScale;

        // 重置玩家Animator速度为正常值（不受timeScale影响）
        if (playerAnimator != null)
        {
            playerAnimator.speed = 1f / hitStopTimeScale;
        }

        // 启动命中停顿计时
        isHitStopActive = true;
        hitStopTimer = 0f;

        Debug.Log($"[CombatHandler] 触发命中停顿，时长: {hitStopDuration}s, 时间缩放: {hitStopTimeScale}");
    }
}
