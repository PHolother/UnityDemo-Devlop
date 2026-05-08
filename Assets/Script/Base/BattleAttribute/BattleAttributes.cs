using UnityEngine;
using Script.Base.Interface.Battle;

namespace Script.Base.BattleAttribute
{
    /// <summary>
    /// 通用角色战斗属性脚本
    /// 实现IDamageProvider接口，提供伤害值
    /// 统一管理Health、Mana、Stamina组件引用
    /// 适用于玩家和敌人，挂载在BattleAttribute子物体上
    /// </summary>
    public class BattleAttributes : MonoBehaviour, IDamageProvider
    {
        [Header("基础属性")]
        [Tooltip("基础攻击力")]
        public int baseDamage = 10;

        [Tooltip("攻击力加成（可通过装备、增益等修改）")]
        public int damageBonus = 0;

        [Header("角色引用")]
        [Tooltip("角色根物体引用（可选，用于扩展功能）")]
        public GameObject owner;

        [Header("属性组件引用")]
        [Tooltip("血量组件（在同一物体或父物体上）")]
        private Health health;

        [Tooltip("法力组件（在同一物体或父物体上）")]
        private Mana mana;

        [Tooltip("体力组件（在同一物体或父物体上）")]
        private Stamina stamina;

        // 便捷访问属性
        public Health Health => health;
        public Mana Mana => mana;
        public Stamina Stamina => stamina;

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

            // 获取组件引用（使用GetComponentInParent，因为组件可能在同一物体）
            health = GetComponentInParent<Health>();
            mana = GetComponentInParent<Mana>();
            stamina = GetComponentInParent<Stamina>();

            // Health是必须的
            if (health == null)
            {
                Debug.LogError("[BattleAttributes] 未找到Health组件！", this);
            }
            else
            {
                Debug.Log($"[BattleAttributes] 成功获取Health组件", this);
            }

            // Mana是可选的
            if (mana == null)
            {
                Debug.Log($"[BattleAttributes] 未找到Mana组件（该角色无法力值）", this);
            }
            else
            {
                Debug.Log($"[BattleAttributes] 成功获取Mana组件", this);
            }

            // Stamina是可选的
            if (stamina == null)
            {
                Debug.Log($"[BattleAttributes] 未找到Stamina组件（该角色无体力值）", this);
            }
            else
            {
                Debug.Log($"[BattleAttributes] 成功获取Stamina组件", this);
            }
        }
    }
}
