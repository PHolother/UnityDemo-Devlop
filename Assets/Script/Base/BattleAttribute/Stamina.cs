using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace Script.Base.BattleAttribute
{
    /// <summary>
    /// 体力值管理组件（含自动恢复）
    /// 负责管理角色的体力值消耗和自动恢复
    /// </summary>
    public class Stamina : MonoBehaviour
    {
        [Header("体力值配置")]
        [Tooltip("最大体力值")]
        public int maxStamina = 100;

        [Header("体力恢复配置")]
        [Tooltip("停止消耗后多少秒开始恢复")]
        public float regenerationDelay = 1.5f;

        [Tooltip("每秒恢复的体力值（固定数值）")]
        public float regenerationValue = 20f;

        // 当前体力值
        private int currentStamina;

        // 上次消耗体力的时间
        private float lastConsumeTime = 0f;

        // 是否正在恢复
        private bool isRegenerating = false;

        // 体力值变化事件
        private UnityEvent<int> onStaminaChanged = new UnityEvent<int>();

        /// <summary>
        /// 体力值变化事件
        /// </summary>
        public UnityEvent<int> OnStaminaChanged => onStaminaChanged;

        private void Awake()
        {
            // 初始化体力值为最大值
            currentStamina = maxStamina;
        }

        private void Update()
        {
            // 如果体力已满，停止恢复
            if (currentStamina >= maxStamina)
            {
                isRegenerating = false;
                return;
            }

            // 检查是否还在等待期
            if (Time.time - lastConsumeTime < regenerationDelay)
            {
                // 还在等待期，不恢复
                isRegenerating = false;
                return;
            }

            // 满足恢复条件
            if (!isRegenerating)
            {
                isRegenerating = true;
                Debug.Log("[Stamina] 开始恢复体力", this);
            }

            // 计算本帧恢复量
            float restoreAmount = regenerationValue * Time.deltaTime;

            // 调用恢复方法
            RestoreStamina(restoreAmount);
        }

        /// <summary>
        /// 消耗体力值
        /// </summary>
        /// <param name="amount">消耗的体力值数量</param>
        /// <returns>是否成功消耗</returns>
        public bool ConsumeStamina(int amount)
        {
            // 如果消耗量小于等于0，返回false
            if (amount <= 0)
            {
                return false;
            }

            // 如果当前体力值小于等于0，返回false（体力耗尽）
            if (currentStamina <= 0)
            {
                return false;
            }

            // 记录消耗前的体力值
            int previousStamina = currentStamina;

            // 减少体力值
            currentStamina -= amount;

            // 确保体力值不低于0
            if (currentStamina < 0)
            {
                currentStamina = 0;
            }

            // 计算变化量（负数表示消耗）
            int staminaChange = currentStamina - previousStamina;

            // 触发事件
            onStaminaChanged.Invoke(staminaChange);

            // 关键：重置恢复计时器
            lastConsumeTime = Time.time;

            // 关键：停止恢复
            isRegenerating = false;

            Debug.Log($"[Stamina] {gameObject.name} 消耗 {amount} 点体力，剩余: {currentStamina}/{maxStamina}", this);

            return true;
        }

        /// <summary>
        /// 恢复体力值（内部使用，支持小数）
        /// </summary>
        /// <param name="amount">恢复的体力值数量</param>
        private void RestoreStamina(float amount)
        {
            // 如果恢复量小于等于0，直接返回
            if (amount <= 0)
            {
                return;
            }

            // 记录恢复前的体力值
            int previousStamina = currentStamina;

            // 增加体力值（向上取整，确保至少恢复1点）
            currentStamina += Mathf.CeilToInt(amount);

            // 确保体力值不超过最大值
            if (currentStamina > maxStamina)
            {
                currentStamina = maxStamina;
            }

            // 计算变化量（正数表示恢复）
            int staminaChange = currentStamina - previousStamina;

            // 触发事件
            onStaminaChanged.Invoke(staminaChange);

            // 如果体力已满，输出日志
            if (currentStamina >= maxStamina)
            {
                Debug.Log("[Stamina] 体力已回满", this);
            }
        }

        /// <summary>
        /// 获取当前体力值
        /// </summary>
        /// <returns>当前体力值</returns>
        public int GetCurrentStamina()
        {
            return currentStamina;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器下重置组件时设置默认值
        /// </summary>
        private void Reset()
        {
            maxStamina = 100;
            regenerationDelay = 1.5f;
            regenerationValue = 20f;
        }
#endif
    }
}
