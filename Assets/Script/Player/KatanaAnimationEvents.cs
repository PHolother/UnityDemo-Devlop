using Script.Base.Hitbox;
using UnityEngine;

namespace Script.Player
{
    /// <summary>
    /// Katana 动画事件包装器
    /// 挂载位置：Player（有 Animator 的物体）
    /// 作用：将 Animator 的 Animation Event 转发到 KatanaHitBox1 上的 WeaponHitboxController
    /// </summary>
    public class KatanaAnimationEvents : MonoBehaviour
    {
        [Header("引用设置")]
        [Tooltip("KatanaHitBox1 上的 WeaponHitboxController 组件")]
        public WeaponHitboxController weaponHitbox;

        /// <summary>
        /// 启用 HitBox（由 Animation Event 调用）
        /// </summary>
        public void EnableHitbox()
        {
            if (weaponHitbox != null)
            {
                weaponHitbox.EnableHitbox();
            }
            else
            {
                Debug.LogWarning("KatanaAnimationEvents: weaponHitbox 未指定！");
            }
        }

        /// <summary>
        /// 禁用 HitBox（由 Animation Event 调用）
        /// </summary>
        public void DisableHitbox()
        {
            if (weaponHitbox != null)
            {
                weaponHitbox.DisableHitbox();
            }
            else
            {
                Debug.LogWarning("KatanaAnimationEvents: weaponHitbox 未指定！");
            }
        }
    }
}
