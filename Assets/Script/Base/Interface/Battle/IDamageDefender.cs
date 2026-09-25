using UnityEngine;

namespace Script.Base.Interface.Battle
{
    /// <summary>
    /// 可"防御伤害"能力接口：由拥有盾/格挡状态的角色实现。
    /// Health.TakeDamage 扣血前逐个询问，任一实现返回 true 即视为该次伤害被防住（不扣血、不触发受击事件）。
    /// </summary>
    public interface IDamageDefender
    {
        /// <summary>
        /// 尝试防御一次伤害。
        /// </summary>
        /// <param name="damage">伤害值</param>
        /// <param name="attacker">攻击者根物体（可为 null）</param>
        /// <returns>true=已防住（免伤）；false=未防住，伤害正常结算</returns>
        bool TryDefend(int damage, GameObject attacker);
    }
}
