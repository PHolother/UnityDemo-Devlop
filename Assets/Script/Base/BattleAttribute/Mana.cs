using UnityEngine;
using UnityEngine.Events;

namespace Script.Base.BattleAttribute
{
    /// <summary>
    /// 法力值管理组件
    /// 负责管理角色的法力值消耗和恢复
    /// </summary>
    public class Mana : MonoBehaviour
    {
        [Header("法力值配置")]
        [Tooltip("最大法力值")]
        public int maxMana = 100;

        // 当前法力值
        private int currentMana;

        // 法力值变化事件（传递变化量，负数表示消耗）
        private UnityEvent<int> onManaChanged = new UnityEvent<int>();

        /// <summary>
        /// 法力值变化事件
        /// </summary>
        public UnityEvent<int> OnManaChanged => onManaChanged;

        private void Awake()
        {
            // 初始化法力值为最大值
            currentMana = maxMana;
        }

        /// <summary>
        /// 消耗法力值
        /// </summary>
        /// <param name="amount">消耗的法力值数量</param>
        /// <returns>是否成功消耗</returns>
        public bool ConsumeMana(int amount)
        {
            // 如果消耗量小于等于0，返回false
            if (amount <= 0)
            {
                return false;
            }

            // 如果当前法力值小于等于0，返回false（法力耗尽）
            if (currentMana <= 0)
            {
                return false;
            }

            // 记录消耗前的法力值
            int previousMana = currentMana;

            // 减少法力值
            currentMana -= amount;

            // 确保法力值不低于0
            if (currentMana < 0)
            {
                currentMana = 0;
            }

            // 计算变化量（负数表示消耗）
            int manaChange = currentMana - previousMana;

            // 触发事件
            onManaChanged.Invoke(manaChange);

            Debug.Log($"[Mana] {gameObject.name} 消耗 {amount} 点法力，剩余: {currentMana}/{maxMana}", this);

            return true;
        }

        /// <summary>
        /// 恢复法力值（预留接口，可通过道具系统调用）
        /// </summary>
        /// <param name="amount">恢复的法力值数量</param>
        public void RestoreMana(int amount)
        {
            // 如果恢复量小于等于0，直接返回
            if (amount <= 0)
            {
                return;
            }

            // 记录恢复前的法力值
            int previousMana = currentMana;

            // 增加法力值
            currentMana += amount;

            // 确保法力值不超过最大值
            if (currentMana > maxMana)
            {
                currentMana = maxMana;
            }

            // 计算变化量（正数表示恢复）
            int manaChange = currentMana - previousMana;

            // 触发事件
            onManaChanged.Invoke(manaChange);

            Debug.Log($"[Mana] {gameObject.name} 恢复 {amount} 点法力，当前: {currentMana}/{maxMana}", this);
        }

        /// <summary>
        /// 获取当前法力值
        /// </summary>
        /// <returns>当前法力值</returns>
        public int GetCurrentMana()
        {
            return currentMana;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器下重置组件时设置默认值
        /// </summary>
        private void Reset()
        {
            maxMana = 100;
        }
#endif
    }
}
