using UnityEngine;

namespace Script.Base.Hitbox
{
    /// <summary>
    /// Hitbox碰撞转发器，极简设计
    /// 只负责在OnTriggerEnter时通知父级HitboxController
    /// </summary>
    public class HitboxPart : MonoBehaviour
    {
        // 父级HitboxController（自动查找）
        private HitboxController parentController;

        private void Awake()
        {
            // 自动查找父物体上的HitboxController
            parentController = transform.parent.GetComponentInParent<HitboxController>();
            
            if (parentController == null)
            {
                Debug.LogWarning("[HitboxPart] 未在父物体中找到HitboxController组件！", this);
            }
        }

        /// <summary>
        /// 触发进入时调用
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            // 如果父控制器存在，调用其处理方法
            if (parentController != null)
            {
                parentController.OnChildTrigger(other);
            }
        }
    }
}
