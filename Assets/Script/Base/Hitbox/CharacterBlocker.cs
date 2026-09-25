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

        [Header("动态贴合 Hurtbox（独立阻挡胶囊）")]
        [Tooltip("启用后驱动子物体 BlockerCapsule 的非 trigger 胶囊每帧贴合 Hurtbox 世界尺寸。" +
                 "注意：实体胶囊跟随攻击动画扫动会与玩家 CharacterController 发生物理推挤（表现为突进/击飞），" +
                 "与 CC 互相推挤的阻挡体系冲突，仅在明确需要物理阻挡贴合时启用。")]
        public bool fitToHurtbox = false;
        [Tooltip("贴合胶囊最小半径（防止动画极端姿态把胶囊挤没）")]
        public float minRadius = 0.2f;
        [Tooltip("贴合胶囊最小高度")]
        public float minHeight = 0.5f;
        [Tooltip("贴合胶囊最大高度")]
        public float maxHeight = 4f;

        private CharacterController controller;
        private CapsuleCollider hurtbox;
        private CapsuleCollider blockerCapsule;   // 子物体上的阻挡胶囊（非 trigger）
        private Vector3 lastFit;

        // 注意：CharacterController 胶囊参与地面检测，动态改尺寸/中心会与重力/贴地逻辑冲突
        // （胶囊抬离地面→GravityScript 判定悬空→浮空）。本组件只负责角色间阻挡，
        // CC 胶囊保持场景里的固定静态值；动态贴合由独立 BlockerCapsule 承担。
        
        void Awake()
        {
            controller = GetComponent<CharacterController>();

            // 如果blockLayers未设置,自动使用当前物体的Layer
            if (blockLayers == 0)
            {
                blockLayers = 1 << gameObject.layer;
            }

            hurtbox = FindHurtbox();
            lastFit = Vector3.negativeInfinity;
        }

        /// <summary>
        /// 在子层级中查找名为 Hurtbox 的胶囊碰撞体（受击盒约定名）。
        /// </summary>
        private CapsuleCollider FindHurtbox()
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name == "Hurtbox")
                {
                    var cap = t.GetComponent<CapsuleCollider>();
                    if (cap != null) return cap;
                }
            }
            return null;
        }

        /// <summary>
        /// 查找/创建子物体 BlockerCapsule：非 trigger 胶囊 + kinematic Rigidbody，
        /// 专用于角色间阻挡（与地面无关，不影响 GravityScript 的地面检测）。
        /// 放在 Default 层即可与其它角色的 CC/胶囊产生物理推挤。
        /// </summary>
        private CapsuleCollider FindOrCreateBlockerCapsule()
        {
            if (blockerCapsule != null) return blockerCapsule;
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name == "BlockerCapsule")
                {
                    blockerCapsule = t.GetComponent<CapsuleCollider>();
                    if (blockerCapsule != null) return blockerCapsule;
                }
            }
            // 创建
            var node = new GameObject("BlockerCapsule");
            node.layer = gameObject.layer;
            node.transform.SetParent(transform, false);
            var rb = node.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
            blockerCapsule = node.AddComponent<CapsuleCollider>();
            blockerCapsule.isTrigger = false;
            return blockerCapsule;
        }

        void Update()
        {
            if (fitToHurtbox) FitBlockerToHurtbox();
            BlockCharacters();
        }

        /// <summary>
        /// 每帧把 BlockerCapsule 贴合到 Hurtbox 的世界胶囊位置与尺寸。
        /// Hurtbox 跟随骨骼动画变形（上挑/倒地等），阻挡范围因此贴合实际体型；
        /// 尺寸与位置都未变化时跳过写入。
        /// </summary>
        private void FitBlockerToHurtbox()
        {
            if (hurtbox == null)
            {
                hurtbox = FindHurtbox();
                if (hurtbox == null) return;
            }
            var blocker = FindOrCreateBlockerCapsule();

            // Hurtbox 世界胶囊（沿其本地 Y 轴）
            Transform ht = hurtbox.transform;
            Vector3 ws = ht.lossyScale;
            float worldRadius = hurtbox.radius * Mathf.Max(Mathf.Abs(ws.x), Mathf.Abs(ws.z));
            float worldHeight = hurtbox.height * Mathf.Abs(ws.y);
            Vector3 worldCenter = ht.TransformPoint(hurtbox.center);
            Vector3 worldUp = ht.up;

            // 钳制
            float radius = Mathf.Clamp(worldRadius, minRadius, 10f);
            float height = Mathf.Clamp(worldHeight, minHeight, maxHeight);
            height = Mathf.Max(height, radius * 2f); // 胶囊约束 height >= 2*radius

            // 位置：世界中心；朝向：胶囊轴向对齐 Hurtbox 本地 up
            Vector3 centerOffsetLocal = Vector3.zero; // 胶囊 center 用本地 (0,0,0)，把节点摆在中心-半高*轴
            Vector3 axis = worldUp.normalized;
            Vector3 nodePos = worldCenter;

            // 未变化则跳过
            Vector3 fit = new Vector3(radius, height, 0f);
            bool posChanged = (nodePos - lastFit).sqrMagnitude > 0.000001f;
            bool sizeChanged = (new Vector3(blocker.radius, blocker.height, 0f) - fit).sqrMagnitude > 0.000001f;
            if (!posChanged && !sizeChanged) return;
            lastFit = nodePos;

            blocker.radius = radius;
            blocker.height = height;
            blocker.direction = CapsuleToDirection(worldUp);
            blocker.center = centerOffsetLocal;
            blocker.transform.position = nodePos;
            blocker.transform.rotation = Quaternion.FromToRotation(Vector3.up, axis);
        }

        /// <summary>
        /// 把世界方向向量映射到 CapsuleCollider.direction 枚举（0=X 1=Y 2=Z，按本地轴最近者）。
        /// Hurtbox 胶囊沿本地 Y，若其骨骼大幅旋转则用最近轴近似（阻挡用途足够）。
        /// </summary>
        private int CapsuleToDirection(Vector3 worldUp)
        {
            // 节点旋转已对齐轴，collider 本身用 Y 轴即可
            return 1;
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
