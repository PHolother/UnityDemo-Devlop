using Script.Base.Hitbox;
using UnityEngine;

namespace Script.Player
{
    /// <summary>
    /// Katana 动画事件包装器
    /// 挂载位置：Player（有 Animator 的物体）
    /// 作用：将 Animator 的 Animation Event 转发到武器上的 HitboxController
    /// </summary>
    public class KatanaAnimationEvents : MonoBehaviour
    {
        [Header("引用设置")]
        [Tooltip("武器下的 HitboxController 组件")]
        public HitboxController hitboxController;

        /// <summary>
        /// 启用 Hitbox（由 Animation Event 调用）
        /// </summary>
        public void EnableHitbox()
        {
            if (hitboxController != null)
            {
                hitboxController.EnableHitbox();
            }
            else
            {
                Debug.LogWarning("KatanaAnimationEvents: HitboxController 未指定！");
            }

            // 每次挥刀（开 hitbox）通知 PlayerAttack：扣体力（按段每次挥刀消耗）+ 设伤害
            // A3 有两次挥刀 → 每次独立扣/设
            var playerAttack = GetComponent<PlayerAttack>();
            if (playerAttack != null)
            {
                playerAttack.OnAttackSwing();
            }
        }

        /// <summary>
        /// 禁用 Hitbox（由 Animation Event 调用）
        /// </summary>
        public void DisableHitbox()
        {
            if (hitboxController != null)
            {
                hitboxController.DisableHitbox();
            }
            else
            {
                Debug.LogWarning("KatanaAnimationEvents: HitboxController 未指定！");
            }
        }
    }
}
