using UnityEngine;

/// <summary>
/// 战斗事件处理器示例
/// 接收HitboxController的OnHit事件，处理伤害应用、音效、特效等逻辑
/// 挂载在场景中的空物体上
/// </summary>
public class CombatHandler : MonoBehaviour
{
    [Header("特效引用")]
    [Tooltip("命中特效预制体")]
    public GameObject hitEffectPrefab;

    [Tooltip("命中音效")]
    public AudioClip hitSound;

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

        // 3. 生成命中特效（可选）
        SpawnHitEffect(target.transform.position);
    }

    /// <summary>
    /// 对目标应用伤害
    /// </summary>
    private void ApplyDamage(GameObject target, int damage)
    {
        // 方式1：通过SendMessage调用目标的TakeDamage方法
        target.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);

        // 方式2：如果目标有IDamageable接口，可以直接调用
        // IDamageable damageable = target.GetComponent<IDamageable>();
        // if (damageable != null)
        // {
        //     damageable.TakeDamage(damage);
        // }
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
    /// 生成命中特效
    /// </summary>
    private void SpawnHitEffect(Vector3 position)
    {
        if (hitEffectPrefab != null)
        {
            Instantiate(hitEffectPrefab, position, Quaternion.identity);
        }
    }
}
