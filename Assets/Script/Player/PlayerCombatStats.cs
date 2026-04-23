using UnityEngine;
using Script.Base.Interface.Battle;

/// <summary>
/// 玩家战斗属性示例脚本
/// 实现IDamageProvider接口，提供伤害值
/// 挂载在BattleAttribute预制体上
/// </summary>
public class PlayerCombatStats : MonoBehaviour, IDamageProvider
{
    [Header("基础属性")]
    [Tooltip("基础攻击力")]
    public int baseDamage = 10;

    [Tooltip("攻击力加成（可通过装备、增益等修改）")]
    public int damageBonus = 0;

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
}
