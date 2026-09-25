using UnityEngine;
using UnityEngine.Events;

namespace Script.Base.BattleAttribute
{
    /// <summary>
    /// 体力值管理组件（耐力/精力）。
    /// 规则：
    /// - 最大体力 maxStamina（默认 100，可由 PlayerConfig.xlsx 配置）
    /// - 消耗是瞬间的（ConsumeStamina 立即扣减并触发事件）
    /// - 停止消耗 staminaRegenDelay 秒后才开始恢复
    /// - 恢复按每秒 staminaRegenRate 平滑增长（每帧按 deltaTime 累加，非一次性跳变）
    /// - 消耗/恢复都触发 OnStaminaChanged（传变化量，负=消耗/正=恢复）
    /// </summary>
    public class Stamina : MonoBehaviour
    {
        [Header("体力值配置")]
        [Tooltip("最大体力值")]
        public float maxStamina = 100f;

        [Header("恢复配置")]
        [Tooltip("体力每秒恢复量")]
        public float staminaRegenRate = 20f;
        [Tooltip("停止消耗后延迟开始恢复（秒）")]
        public float staminaRegenDelay = 1f;

        // 当前体力（float，支持平滑恢复的亚单位精度）
        private float currentStamina;
        // 最近一次消耗的时间：恢复只在"停耗 staminaRegenDelay 秒后"开始
        private float lastConsumeTime = -999f;

        // 体力变化事件（传递变化量，负数=消耗，正数=恢复）
        private UnityEvent<float> onStaminaChanged = new UnityEvent<float>();

        public UnityEvent<float> OnStaminaChanged => onStaminaChanged;

        private void Awake()
        {
            currentStamina = maxStamina;
        }

        private void Update()
        {
            // 体力恢复：停耗 staminaRegenDelay 秒后，按每秒 staminaRegenRate 平滑增长
            if (currentStamina >= maxStamina) return;
            if (Time.time - lastConsumeTime < staminaRegenDelay) return;

            float prev = currentStamina;
            currentStamina = Mathf.Min(maxStamina, currentStamina + staminaRegenRate * Time.deltaTime);
            float change = currentStamina - prev;
            if (change > 0.001f)
            {
                onStaminaChanged.Invoke(change);
            }
        }

        /// <summary>
        /// 消耗体力（瞬间）。体力不足时返回 false（调用方可不执行动作）。
        /// </summary>
        /// <param name="amount">消耗量</param>
        /// <returns>是否成功消耗</returns>
        public bool ConsumeStamina(float amount)
        {
            if (amount <= 0f) return false;
            if (currentStamina < amount) return false; // 体力不足

            float prev = currentStamina;
            currentStamina -= amount;
            if (currentStamina < 0f) currentStamina = 0f;

            // 记录消耗时刻：之后 staminaRegenDelay 秒内不恢复
            lastConsumeTime = Time.time;

            float change = currentStamina - prev;
            onStaminaChanged.Invoke(change);

            Debug.Log($"[Stamina] {gameObject.name} 消耗 {amount:0.#} 点体力，剩余: {currentStamina:0.#}/{maxStamina:0.#}", this);
            return true;
        }

        /// <summary>
        /// 直接恢复体力（外部接口，如道具/特殊效果）。
        /// </summary>
        public void RestoreStamina(float amount)
        {
            if (amount <= 0f) return;
            float prev = currentStamina;
            currentStamina = Mathf.Min(maxStamina, currentStamina + amount);
            float change = currentStamina - prev;
            if (change > 0.001f)
            {
                onStaminaChanged.Invoke(change);
            }
        }

        /// <summary>
        /// 获取当前体力（float，UI 可直接用）。
        /// </summary>
        public float GetCurrentStamina() => currentStamina;

        /// <summary>
        /// 是否体力耗尽。
        /// </summary>
        public bool IsExhausted => currentStamina <= 0f;

#if UNITY_EDITOR
        private void Reset()
        {
            maxStamina = 100f;
            staminaRegenRate = 20f;
            staminaRegenDelay = 1f;
        }
#endif
    }
}
