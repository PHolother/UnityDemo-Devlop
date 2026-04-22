using UnityEngine;

namespace Script.Base.Hitbox
{
    /// <summary>
    /// Hitbox子物体触发器转发器
    /// 挂载位置：KatanaHitBox1、KatanaHitBox2、KatanaHitBox3（每个碰撞箱物体各挂一个）
    /// </summary>
    public class HitboxChild : MonoBehaviour
    {
        [Header("引用设置")]
        [Tooltip("父物体上的WeaponHitboxController组件")]
        public WeaponHitboxController parentController;

        /// <summary>
        /// 触发进入时调用
        /// </summary>
        /// <param name="other">碰撞体</param>
        private void OnTriggerEnter(Collider other)
        {
            // 如果父控制器不为空，调用其处理方法
            if (parentController != null)
            {
                parentController.OnChildTrigger(other);
            }
        }
    }
}
