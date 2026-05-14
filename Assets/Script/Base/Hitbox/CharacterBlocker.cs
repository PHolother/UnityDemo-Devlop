using UnityEngine;

namespace Script.Base.Hitbox
{
    /// <summary>
    /// 角色碰撞阻挡器
    /// 防止玩家和敌人互相穿过,通过CharacterController的Move方法实现平滑阻挡
    /// 只阻挡不推开,保持角色间的最小距离
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class CharacterBlocker : MonoBehaviour
    {
        [Header("阻挡设置")]
        [Tooltip("检测周围角色的距离半径")]
        public float blockDistance = 0.8f;
        
        [Tooltip("阻挡强度系数,值越大阻挡越强硬(建议0.5-2.0)")]
        public float blockStrength = 1.0f;
        
        [Tooltip("最小保持距离,角色间至少保持这个距离")]
        public float minDistance = 0.5f;
        
        [Header("层级设置")]
        [Tooltip("哪些层级的角色会互相阻挡")]
        public LayerMask blockLayers;
        
        private CharacterController controller;
        
        void Awake()
        {
            controller = GetComponent<CharacterController>();
            
            // 如果blockLayers未设置,自动使用当前物体的Layer
            if (blockLayers == 0)
            {
                blockLayers = 1 << gameObject.layer;
            }
        }
        
        void Update()
        {
            BlockCharacters();
        }
        
        /// <summary>
        /// 执行角色阻挡逻辑
        /// </summary>
        private void BlockCharacters()
        {
            if (controller == null || !controller.enabled) return;
            
            // 检测周围的碰撞体
            Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, blockDistance, blockLayers);
            
            foreach (Collider other in nearbyColliders)
            {
                // 跳过自己
                if (other.transform.root == transform.root) continue;
                
                // 检查是否有CharacterController组件
                CharacterController otherController = other.GetComponentInParent<CharacterController>();
                if (otherController == null || !otherController.enabled) continue;
                
                // 计算水平方向的距离和方向(忽略Y轴)
                Vector3 myPos = transform.position;
                Vector3 otherPos = other.transform.position;
                
                myPos.y = 0;
                otherPos.y = 0;
                
                Vector3 direction = myPos - otherPos;
                float distance = direction.magnitude;
                
                // 如果距离小于最小距离,施加阻挡
                if (distance < minDistance && distance > 0.01f)
                {
                    // 计算阻挡向量:方向归一化 * 阻挡强度 * (最小距离 - 当前距离)
                    Vector3 blockDirection = direction.normalized;
                    float blockAmount = (minDistance - distance) * blockStrength;
                    
                    // 使用CharacterController.Move进行阻挡移动
                    Vector3 blockVector = blockDirection * blockAmount * Time.deltaTime;
                    controller.Move(blockVector);
                }
            }
        }
        
#if UNITY_EDITOR
        /// <summary>
        /// Gizmos绘制(选中物体时显示检测范围)
        /// </summary>
        void OnDrawGizmosSelected()
        {
            // 绘制检测范围球体
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, blockDistance);
            
            // 绘制最小距离球体
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, minDistance);
        }
#endif
    }
}
