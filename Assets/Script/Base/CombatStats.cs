using UnityEngine;
using Script.Base.Interface.Battle;

/// <summary>
/// 通用角色战斗属性脚本
/// 实现IDamageProvider接口，提供伤害值
/// 适用于玩家和敌人，挂载在BattleAttribute子物体上
/// </summary>
public class CombatStats : MonoBehaviour, IDamageProvider
{
    [Header("基础属性")]
    [Tooltip("基础攻击力")]
    public int baseDamage = 10;

    [Tooltip("攻击力加成（可通过装备、增益等修改）")]
    public int damageBonus = 0;

    [Header("角色引用")]
    [Tooltip("角色根物体引用（可选，用于扩展功能）")]
    public GameObject owner;

    /// <summary>
    /// 实现IDamageProvider接口
    /// </summary>
    public int GetDamage()
    {
        return baseDamage + damageBonus;
    }

    /// <summary>
    /// 设置基础伤害（可由装备系统调用）
    /// </summary>
    public void SetBaseDamage(int damage)
    {
        baseDamage = damage;
    }

    /// <summary>
    /// 添加伤害加成（可由buff系统调用）
    /// </summary>
    public void AddDamageBonus(int bonus)
    {
        damageBonus += bonus;
    }

    /// <summary>
    /// 清除伤害加成
    /// </summary>
    public void ClearDamageBonus()
    {
        damageBonus = 0;
    }

    private void Awake()
    {
        // 如果owner未设置，自动设为根物体
        if (owner == null)
        {
            owner = transform.root.gameObject;
        }
    }
}
