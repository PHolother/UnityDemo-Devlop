using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Script.Base.Interface.Battle;

namespace Script.Base.Hitbox
{
    /// <summary>
    /// Hitbox控制器，管理武器上所有HitboxPart的激活/禁用
    /// 从角色根节点查找BattleAttribute并获取伤害值
    /// 对同一目标去重，通过UnityEvent向外发送命中事件
    /// </summary>
    public class HitboxController : MonoBehaviour
    {
        [Header("攻击者设置")]
        [Tooltip("攻击者根物体，留空则自动设为transform.root")]
        public GameObject attacker;

        [Header("命中事件")]
        [Tooltip("命中时触发的事件 (attacker, battleAttribute, targetRoot, damage)")]
        public UnityEvent<GameObject, GameObject, GameObject, int> OnHit;

        // HurtBox Layer索引
        private int hurtBoxLayerIndex;

        // 缓存所有需要控制的Collider
        private List<Collider> cachedColliders = new List<Collider>();

        // 记录本次攻击已命中的目标根物体
        private HashSet<GameObject> hitTargets = new HashSet<GameObject>();

        // 当前BattleAttribute引用（每次EnableHitbox时重新查找）
        private GameObject currentBattleAttribute;
        private IDamageProvider currentDamageProvider;

        private void Awake()
        {
            // 获取HurtBox的Layer索引
            hurtBoxLayerIndex = LayerMask.NameToLayer("HurtBox");
            if (hurtBoxLayerIndex == -1)
            {
                Debug.LogWarning("[HitboxController] 未找到 'HurtBox' Layer，请确保已在Tag Manager中创建该Layer！", this);
            }

            // 如果attacker未设置，自动设为根物体
            if (attacker == null)
            {
                attacker = transform.root.gameObject;
            }

            // 收集所有子物体的Collider（不包括自身）
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in colliders)
            {
                // 排除自身（HitboxController所在物体通常不需要Collider）
                if (collider.transform != transform)
                {
                    cachedColliders.Add(collider);
                }
            }

            Debug.Log($"[HitboxController] 初始化完成，共收集 {cachedColliders.Count} 个Collider", this);
        }

        /// <summary>
        /// 启用Hitbox（在动画挥出帧调用）
        /// </summary>
        public void EnableHitbox()
        {
            // 清空命中记录
            hitTargets.Clear();

            // 重新查找BattleAttribute（支持动态替换）
            FindBattleAttribute();

            // 验证IDamageProvider存在
            if (currentDamageProvider == null)
            {
                Debug.LogError("[HitboxController] 未找到实现IDamageProvider的BattleAttribute组件！请检查层级结构和脚本配置。", this);
                return;
            }

            // 启用所有缓存的Collider
            foreach (Collider collider in cachedColliders)
            {
                collider.enabled = true;
            }

            Debug.Log($"[HitboxController] Hitbox已启用，当前伤害值: {currentDamageProvider.GetDamage()}", this);
        }

        /// <summary>
        /// 禁用Hitbox（在动画收招帧调用）
        /// </summary>
        public void DisableHitbox()
        {
            // 禁用所有缓存的Collider
            foreach (Collider collider in cachedColliders)
            {
                collider.enabled = false;
            }

            Debug.Log("[HitboxController] Hitbox已禁用", this);
        }

        /// <summary>
        /// 由子物体HitboxPart调用的内部处理方法
        /// </summary>
        /// <param name="other">触发碰撞的Collider</param>
        public void OnChildTrigger(Collider other)
        {
            // 检查是否为空
            if (other == null) return;

            // Layer过滤（仅HurtBox）
            if (other.gameObject.layer != hurtBoxLayerIndex)
            {
                return;
            }

            // 获取受击目标的根物体
            GameObject targetRoot = other.transform.root.gameObject;

            // 根物体去重
            if (hitTargets.Contains(targetRoot))
            {
                return;
            }

            // 验证BattleAttribute和DamageProvider
            if (currentDamageProvider == null)
            {
                Debug.LogWarning("[HitboxController] BattleAttribute或IDamageProvider未找到，无法造成伤害", this);
                return;
            }

            // 添加到已命中列表
            hitTargets.Add(targetRoot);

            // 获取伤害值
            int damage = currentDamageProvider.GetDamage();

            // 触发OnHit事件
            if (OnHit != null)
            {
                OnHit.Invoke(attacker, currentBattleAttribute, targetRoot, damage);
            }

            Debug.Log($"[HitboxController] 命中目标: {targetRoot.name}, 伤害: {damage}", this);
        }

        /// <summary>
        /// 查找BattleAttribute并获取IDamageProvider
        /// </summary>
        private void FindBattleAttribute()
        {
            if (attacker == null)
            {
                currentBattleAttribute = null;
                currentDamageProvider = null;
                return;
            }

            // 在攻击者根节点下查找名为"BattleAttribute"的子物体
            Transform battleAttrTransform = attacker.transform.Find("BattleAttribute");
            
            if (battleAttrTransform == null)
            {
                currentBattleAttribute = null;
                currentDamageProvider = null;
                Debug.LogWarning("[HitboxController] 未在攻击者根节点下找到 'BattleAttribute' 子物体", this);
                return;
            }

            currentBattleAttribute = battleAttrTransform.gameObject;
            currentDamageProvider = currentBattleAttribute.GetComponent<IDamageProvider>();

            if (currentDamageProvider == null)
            {
                Debug.LogWarning("[HitboxController] BattleAttribute上未找到实现IDamageProvider的组件", this);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Gizmos绘制（选中物体时显示）
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            // 绘制所有缓存的Collider
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in colliders)
            {
                if (collider.transform == transform) continue; // 跳过自身

                Color gizmoColor = collider.enabled ? Color.green : Color.gray;
                Gizmos.color = gizmoColor;

                // BoxCollider
                if (collider is BoxCollider box)
                {
                    Matrix4x4 rotationMatrix = Matrix4x4.TRS(box.transform.position, box.transform.rotation, Vector3.one);
                    Gizmos.matrix = rotationMatrix;
                    Gizmos.DrawWireCube(box.center, box.size);
                }
                // SphereCollider
                else if (collider is SphereCollider sphere)
                {
                    Gizmos.DrawWireSphere(sphere.transform.position + sphere.center, sphere.radius);
                }
                // CapsuleCollider
                else if (collider is CapsuleCollider capsule)
                {
                    DrawCapsuleGizmo(capsule);
                }
            }
        }

        /// <summary>
        /// 绘制CapsuleCollider的Gizmos
        /// </summary>
        private void DrawCapsuleGizmo(CapsuleCollider capsule)
        {
            Vector3 position = capsule.transform.position;
            Quaternion rotation = capsule.transform.rotation;
            float radius = capsule.radius;
            float height = capsule.height;
            int direction = capsule.direction;

            // 计算胶囊的两个端点
            Vector3 center = capsule.center;
            float halfHeight = height * 0.5f;
            Vector3 offset = Vector3.zero;

            switch (direction)
            {
                case 0: // X轴
                    offset = new Vector3(halfHeight - radius, 0, 0);
                    break;
                case 1: // Y轴
                    offset = new Vector3(0, halfHeight - radius, 0);
                    break;
                case 2: // Z轴
                    offset = new Vector3(0, 0, halfHeight - radius);
                    break;
            }

            Vector3 point1 = position + rotation * (center + offset);
            Vector3 point2 = position + rotation * (center - offset);

            // 绘制胶囊体（简化为两个球体和圆柱）
            Gizmos.DrawWireSphere(point1, radius);
            Gizmos.DrawWireSphere(point2, radius);
        }
#endif
    }
}
