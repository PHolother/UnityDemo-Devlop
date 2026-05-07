using UnityEngine;
using UnityEngine.Events;

namespace Script.Base.Interface.Battle
{
    /// <summary>
    /// 可受伤接口，由需要接收伤害的实体实现
    /// </summary>
    public interface IDamageable
    {
        /// <summary>
        /// 受到伤害
        /// </summary>
        /// <param name="damage">伤害值</param>
        /// <returns>是否成功受到伤害</returns>
        bool TakeDamage(int damage);

        /// <summary>
        /// 获取当前血量
        /// </summary>
        /// <returns>当前血量值</returns>
        int GetCurrentHealth();

        /// <summary>
        /// 是否已死亡
        /// </summary>
        /// <returns>是否死亡</returns>
        bool IsDead();

        /// <summary>
        /// 血量变化事件（传递血量变化量，负数表示减少）
        /// </summary>
        UnityEvent<int> OnHealthChanged { get; }
    }
}
