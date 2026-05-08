using Script.Base.Interface.Battle;
using UnityEngine;
using UnityEngine.Events;

namespace Script.Base.BattleAttribute
{
    /// <summary>
    /// 敌人血量管理脚本，实现IDamageable接口
    /// </summary>
    public class Health : MonoBehaviour, IDamageable
    {
        [Header("血量配置")]
        [Tooltip("最大血量")]
        public int maxHealth = 100;

        [Header("死亡动画配置")]
        [Tooltip("死亡动画Trigger名称（固定为\"Die\"）")]
        public string deathTriggerName = "Die";

        // 当前血量
        private int currentHealth;

        // Animator组件引用
        private Animator animator;

        // 是否已死亡
        private bool isDead = false;

        // 血量变化事件
        private UnityEvent<int> onHealthChanged = new UnityEvent<int>();

        /// <summary>
        /// 血量变化事件（实现IDamageable接口）
        /// </summary>
        public UnityEvent<int> OnHealthChanged => onHealthChanged;

        private void Awake()
        {
            // 初始化血量为最大值
            currentHealth = maxHealth;

            // 获取Animator组件
            animator = GetComponentInParent<Animator>();
            if (animator == null)
            {
                Debug.LogWarning("[Health] 未找到Animator组件，死亡动画可能无法播放", this);
            }
        }

        /// <summary>
        /// 受到伤害（实现IDamageable接口）
        /// </summary>
        /// <param name="damage">伤害值</param>
        /// <returns>是否成功受到伤害</returns>
        public bool TakeDamage(int damage)
        {
            // 如果已经死亡，不再受到伤害
            if (isDead)
            {
                return false;
            }

            // 确保伤害值为正数
            if (damage <= 0)
            {
                return false;
            }

            // 记录受伤前的血量
            int previousHealth = currentHealth;

            // 减少血量
            currentHealth -= damage;

            // 确保血量不低于0
            if (currentHealth < 0)
            {
                currentHealth = 0;
            }

            // 计算实际血量变化量（负数表示减少）
            int healthChange = currentHealth - previousHealth;

            // 触发血量变化事件
            onHealthChanged.Invoke(healthChange);

            Debug.Log($"[Health] {gameObject.name} 受到 {damage} 点伤害，剩余血量: {currentHealth}/{maxHealth}", this);

            // 检查是否死亡
            if (currentHealth <= 0 && !isDead)
            {
                Die();
            }

            return true;
        }

        /// <summary>
        /// 获取当前血量（实现IDamageable接口）
        /// </summary>
        /// <returns>当前血量值</returns>
        public int GetCurrentHealth()
        {
            return currentHealth;
        }

        /// <summary>
        /// 是否已死亡（实现IDamageable接口）
        /// </summary>
        /// <returns>是否死亡</returns>
        public bool IsDead()
        {
            return isDead;
        }

        /// <summary>
        /// 死亡处理
        /// </summary>
        private void Die()
        {
            isDead = true;

            Debug.Log($"[Health] {gameObject.name} 已死亡", this);

            // 播放死亡动画
            if (animator != null)
            {
                animator.SetTrigger(deathTriggerName);
                Debug.Log($"[Health] 播放死亡动画Trigger: {deathTriggerName}", this);
            }

            // 禁用移动/AI脚本
            DisableMovementAndAI();

            // 禁用碰撞体
            DisableColliders();

            // 注意：不销毁尸体，保持场景中的存在
        }

        /// <summary>
        /// 禁用移动和AI相关脚本
        /// </summary>
        private void DisableMovementAndAI()
        {
            // 获取所有MonoBehaviour组件
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();

            foreach (MonoBehaviour behaviour in behaviours)
            {
                // 跳过自身脚本
                if (behaviour == this) continue;

                // 根据组件类型判断是否为移动或AI脚本
                string typeName = behaviour.GetType().Name.ToLower();
                
                // 常见的移动/AI脚本关键词
                if (typeName.Contains("move") || 
                    typeName.Contains("ai") || 
                    typeName.Contains("controller") || 
                    typeName.Contains("agent") ||
                    typeName.Contains("nav"))
                {
                    behaviour.enabled = false;
                    Debug.Log($"[Health] 禁用脚本: {behaviour.GetType().Name}", this);
                }
            }
        }

        /// <summary>
        /// 禁用所有碰撞体
        /// </summary>
        private void DisableColliders()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);

            foreach (Collider collider in colliders)
            {
                collider.enabled = false;
            }

            Debug.Log($"[Health] 已禁用 {colliders.Length} 个碰撞体", this);
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器下重置组件时设置默认值
        /// </summary>
        private void Reset()
        {
            maxHealth = 100;
            deathTriggerName = "Die";
        }
#endif
    }
}
