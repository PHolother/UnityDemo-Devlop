using System.Collections.Generic;
using UnityEngine;

namespace Script.Base.Hitbox
{
    /// <summary>
    /// 武器Hitbox控制器，用于管理太刀三段式攻击判定
    /// 挂载位置：KatanaHitBox1（作为三个碰撞箱的父物体）
    /// </summary>
    public class WeaponHitboxController : MonoBehaviour
    {
        [Header("伤害设置")]
        [Tooltip("伤害值")]
        public int damage = 10;

        // 记录本次攻击已命中的目标根物体
        private List<GameObject> hitTargets = new List<GameObject>();

        // HurtBox Layer索引
        private int hurtBoxLayerIndex;

        // 缓存所有需要控制的Collider（包括自身和子物体）
        private List<Collider> cachedColliders = new List<Collider>();

        private void Awake()
        {
            // 获取HurtBox的Layer索引
            hurtBoxLayerIndex = LayerMask.NameToLayer("HurtBox");
        
            if (hurtBoxLayerIndex == -1)
            {
                Debug.LogWarning("未找到'HurtBox' Layer，请确保已在Tag Manager中创建该Layer！");
            }

            // 收集当前物体及其所有子物体的Collider
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in colliders)
            {
                cachedColliders.Add(collider);
            }

            Debug.Log($"WeaponHitboxController 初始化完成，共收集 {cachedColliders.Count} 个Collider");
        }

        /// <summary>
        /// 启用Hitbox（在动画挥出瞬间调用）
        /// </summary>
        public void EnableHitbox()
        {
            // 清空已命中目标列表，允许新的攻击判定
            hitTargets.Clear();

            // 启用所有缓存的Collider
            foreach (Collider collider in cachedColliders)
            {
                collider.enabled = true;
            }

            Debug.Log("HitBox 已启用");
        }

        /// <summary>
        /// 禁用Hitbox（在动画收招瞬间调用）
        /// </summary>
        public void DisableHitbox()
        {
            // 禁用所有缓存的Collider
            foreach (Collider collider in cachedColliders)
            {
                collider.enabled = false;
            }

            Debug.Log("HitBox 已禁用");
        }

        /// <summary>
        /// 由子物体调用的内部处理方法
        /// </summary>
        /// <param name="other">触发碰撞的Collider</param>
        public void OnChildTrigger(Collider other)
        {
            // 检查是否为空
            if (other == null) return;

            // 判断碰撞物体的Layer是否为HurtBox
            if (other.gameObject.layer != hurtBoxLayerIndex)
            {
                return;
            }

            // 获取受击目标的根GameObject
            GameObject targetRoot = other.transform.root.gameObject;

            // 检查是否已经命中过该目标
            if (hitTargets.Contains(targetRoot))
            {
                return; // 防止重复伤害
            }

            // 添加到已命中列表
            hitTargets.Add(targetRoot);

            // 向根GameObject发送TakeDamage消息
            targetRoot.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);

            Debug.Log($"命中目标: {targetRoot.name}, 伤害: {damage}");
        }
    }
}
