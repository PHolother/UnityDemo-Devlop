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

        // Hurtbox Layer索引
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
            // 获取Hurtbox的Layer索引
            hurtBoxLayerIndex = LayerMask.NameToLayer("Hurtbox");
            if (hurtBoxLayerIndex == -1)
            {
                Debug.LogWarning("[HitboxController] 未找到 'Hurtbox' Layer，请确保已在Tag Manager中创建该Layer！", this);
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
        /// 攻击轨迹探针（临时）：攻击动画期间每 0.05s 记录 hitbox 到玩家距离和 hitbox Y 高度，
        /// 找出剑接近玩家（Y<玩家高度）的时刻（用于校准伤害帧）。
        /// </summary>
        private float probeLastTime = -1f;
        private void Update()
        {
            try
            {
                if (!Application.isPlaying) return;
                // 全程记录攻击状态（不只 hitbox 启用）——找剑劈到玩家高度的时刻
                var anim = transform.root.GetComponent<Animator>();
                if (anim == null) return;

                // Play 模式可视化：每帧画 hitbox 胶囊轮廓（Scene 视图 Debug.DrawLine）
                DrawHitboxDebugLines();

                var clips = anim.GetCurrentAnimatorClipInfo(0);
                if (clips.Length == 0) return;
                string stateName = clips[0].clip.name;
                // 只记录 BOSS 攻击动画（FocusEnergy/Attack 等）
                bool isAttack = stateName.Contains("Attack") || stateName.Contains("FocusEnergy") || stateName.Contains("Spike") || stateName.Contains("Sweep");
                if (!isAttack) return;
                if (Time.time - probeLastTime < 0.05f) return;
                probeLastTime = Time.time;

                Vector3 hurtboxPos = Vector3.zero;
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    foreach (var c in player.GetComponentsInChildren<Collider>(true))
                        if (c.gameObject.layer == LayerMask.NameToLayer("Hurtbox")) { hurtboxPos = c.transform.position; break; }
                }
                float minDist = 999f;
                float minY = 999f;
                Vector3 hb0 = Vector3.zero;
                foreach (var c in cachedColliders)
                {
                    Vector3 wp = c.transform.position;
                    hb0 = wp;
                    minY = Mathf.Min(minY, wp.y);
                    minDist = Mathf.Min(minDist, Vector3.Distance(wp, hurtboxPos));
                }

                string rootDir = System.IO.Directory.GetParent(Application.dataPath).FullName;
                string dir = System.IO.Path.Combine(rootDir, "ProbeLogs");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "hitbox_trajectory_log.txt");
                string line = "t=" + Time.time.ToString("0.000")
                    + " state=" + stateName
                    + " hitboxToPlayer=" + minDist.ToString("0.00")
                    + " minY=" + minY.ToString("0.00")
                    + " hb1=" + hb0.ToString("0.00");
                System.IO.File.AppendAllText(path, line + System.Environment.NewLine);
            }
            catch (System.Exception) { }
        }

        /// <summary>
        /// Play 模式 hitbox 可视化：用 Debug.DrawLine 画胶囊线框。
        /// 绿色=启用(伤害帧)，红色=禁用。
        /// </summary>
        private void DrawHitboxDebugLines()
        {
            if (cachedColliders == null) return;
            foreach (Collider collider in cachedColliders)
            {
                if (collider == null || !(collider is CapsuleCollider)) continue;
                var cap = (CapsuleCollider)collider;
                Vector3 pos = cap.transform.position;
                Quaternion rot = cap.transform.rotation;
                float radius = cap.radius;
                float halfH = cap.height * 0.5f - radius;
                if (halfH < 0) halfH = 0;
                Vector3 offset = Vector3.zero;
                if (cap.direction == 0) offset = new Vector3(halfH, 0, 0);
                else if (cap.direction == 1) offset = new Vector3(0, halfH, 0);
                else offset = new Vector3(0, 0, halfH);

                Vector3 p1 = pos + rot * (cap.center + offset);
                Vector3 p2 = pos + rot * (cap.center - offset);
                Color col = collider.enabled ? Color.green : new Color(1f, 0.3f, 0.3f, 0.8f);

                // 两端球
                DrawSphereWire(p1, radius, col);
                DrawSphereWire(p2, radius, col);
                // 连接圆柱（四根线）
                Vector3 up = rot * Vector3.up * radius;
                Vector3 right = rot * Vector3.right * radius;
                Vector3 fwd = rot * Vector3.forward * radius;
                Vector3[] dirs = { up, -up, right, -right, fwd, -fwd };
                for (int i = 0; i < 6; i += 2)
                {
                    Debug.DrawLine(p1 + dirs[i], p2 + dirs[i], col);
                    Debug.DrawLine(p1 + dirs[i+1], p2 + dirs[i+1], col);
                }
            }
        }

        private void DrawSphereWire(Vector3 center, float radius, Color col)
        {
            Vector3 up = Vector3.up * radius, right = Vector3.right * radius, fwd = Vector3.forward * radius;
            Debug.DrawLine(center - up, center + up, col);
            Debug.DrawLine(center - right, center + right, col);
            Debug.DrawLine(center - fwd, center + fwd, col);
            // 三个圆（简化八边形）
            for (int i = 0; i < 8; i++)
            {
                float a0 = i * 45f * Mathf.Deg2Rad;
                float a1 = (i + 1) * 45f * Mathf.Deg2Rad;
                Debug.DrawLine(center + new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius, 0),
                               center + new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, 0), col);
                Debug.DrawLine(center + new Vector3(Mathf.Cos(a0) * radius, 0, Mathf.Sin(a0) * radius),
                               center + new Vector3(Mathf.Cos(a1) * radius, 0, Mathf.Sin(a1) * radius), col);
            }
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

            ProbeHitboxPositions();

            Debug.Log($"[HitboxController] Hitbox已启用，当前伤害值: {currentDamageProvider.GetDamage()}", this);
        }

        /// <summary>
        /// 临时探针：记录 hitbox 开启瞬间的世界位置 + Animator 状态 + 玩家 Hurtbox 位置到 ProbeLogs/hitbox_probe_log.txt。
        /// </summary>
        private void ProbeHitboxPositions()
        {
            try
            {
                // 当前 Animator 状态名（区分下劈 FocusR_Attack02 等）
                string stateName = "?";
                var anim = transform.root.GetComponent<Animator>();
                if (anim != null)
                {
                    var st = anim.GetCurrentAnimatorStateInfo(0);
                    stateName = st.shortNameHash.ToString();
                    // 尝试映射常用状态
                    foreach (var clip in anim.GetCurrentAnimatorClipInfo(0))
                    {
                        stateName = clip.clip.name;
                        break;
                    }
                }
                string rootDir = System.IO.Directory.GetParent(Application.dataPath).FullName;
                string dir = System.IO.Path.Combine(rootDir, "ProbeLogs");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "hitbox_probe_log.txt");
                string line = "t=" + Time.time.ToString("0.000")
                    + " state=" + stateName
                    + " attackerPos=" + transform.root.position.ToString("0.00")
                    + " fwd=" + transform.root.forward.ToString("0.00");
                Vector3 playerHurtboxPos = Vector3.zero;
                float minDist = 999f;
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    foreach (var c in player.GetComponentsInChildren<Collider>(true))
                    {
                        if (c.gameObject.layer == LayerMask.NameToLayer("Hurtbox"))
                        {
                            playerHurtboxPos = c.transform.position;
                            line += " | Hurtbox=" + c.transform.position.ToString("0.00");
                        }
                    }
                    line += " | dist=" + Vector3.Distance(player.transform.position, transform.root.position).ToString("0.00");
                }
                foreach (Collider collider in cachedColliders)
                {
                    if (!collider.enabled) continue;
                    line += " | " + collider.name + "=" + collider.transform.position.ToString("0.00");
                    if (player != null)
                        minDist = Mathf.Min(minDist, Vector3.Distance(collider.transform.position, playerHurtboxPos));
                }
                line += " | hitboxToPlayer=" + (minDist < 900f ? minDist.ToString("0.00") : "?");
                System.IO.File.AppendAllText(path, line + System.Environment.NewLine);
            }
            catch (System.Exception) { }
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

            // 命中探针：记录每次 OnChildTrigger 触发（无论是否通过过滤）
            ProbeTrigger(other, "ENTER");

            // Layer过滤（仅Hurtbox）
            if (other.gameObject.layer != hurtBoxLayerIndex)
            {
                return;
            }

            // 获取受击目标的根物体
            GameObject targetRoot = other.transform.root.gameObject;

            // 不能命中攻击者自身（挥剑扫到自己的 Hurtbox）
            if (targetRoot == attacker)
            {
                return;
            }

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

            // 命中探针：OnHit 触发前记录
            ProbeTrigger(other, "HIT targetRoot=" + targetRoot.name + " damage=" + damage);

            // 触发OnHit事件
            if (OnHit != null)
            {
                OnHit.Invoke(attacker, currentBattleAttribute, targetRoot, damage);
            }

            Debug.Log($"[HitboxController] 命中目标: {targetRoot.name}, 伤害: {damage}", this);
        }

        /// <summary>
        /// 命中链路探针（临时）：记录 OnChildTrigger 触发情况到 Temp/hit_chain_log.txt。
        /// 区分"hitbox 没碰到玩家"（无 ENTER）vs"碰到了但被过滤/无伤害"（有 ENTER 无 HIT）。
        /// </summary>
        private void ProbeTrigger(Collider other, string tag)
        {
            try
            {
                string dir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "Temp");
                string path = System.IO.Path.Combine(dir, "hit_chain_log.txt");
                string line = "t=" + Time.time.ToString("0.000")
                    + " " + tag
                    + " other=" + other.name
                    + " layer=" + LayerMask.LayerToName(other.gameObject.layer)
                    + " otherRoot=" + other.transform.root.name
                    + " hitboxPos=" + transform.position.ToString("0.00")
                    + " otherPos=" + other.transform.position.ToString("0.00");
                System.IO.File.AppendAllText(path, line + System.Environment.NewLine);
            }
            catch (System.Exception) { }
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

            // 在攻击者根节点下查找名为"BattleAttributes"的子物体
            Transform battleAttrTransform = attacker.transform.Find("BattleAttributes");
            
            if (battleAttrTransform == null)
            {
                currentBattleAttribute = null;
                currentDamageProvider = null;
                Debug.LogError($"[HitboxController] 未在攻击者根节点 '{attacker.name}' 下找到 'BattleAttributes' 子物体！请确保预制体结构正确：Root/BattleAttributes", this);
                return;
            }

            currentBattleAttribute = battleAttrTransform.gameObject;
            currentDamageProvider = currentBattleAttribute.GetComponent<IDamageProvider>();

            if (currentDamageProvider == null)
            {
                Debug.LogError($"[HitboxController] BattleAttribute '{currentBattleAttribute.name}' 上未找到实现IDamageProvider的组件！请挂载CharacterCombatStats脚本。", this);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Gizmos 常驻绘制（不选中也显示）：hitbox 胶囊线框，启用=绿色，禁用=灰色。
        /// 用于观察 hitbox 与剑/玩家的相对位置。
        /// </summary>
        private void OnDrawGizmos()
        {
            DrawHitboxGizmos();
        }

        /// <summary>
        /// Gizmos绘制（选中物体时显示）
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            DrawHitboxGizmos();
        }

        private void DrawHitboxGizmos()
        {
            if (cachedColliders == null) return;
            foreach (Collider collider in cachedColliders)
            {
                if (collider == null) continue;
                Color gizmoColor = collider.enabled ? new Color(0f, 1f, 0f, 0.9f) : new Color(0.5f, 0.5f, 0.5f, 0.5f);
                Gizmos.color = gizmoColor;

                // CapsuleCollider
                if (collider is CapsuleCollider capsule)
                {
                    DrawCapsuleGizmo(capsule);
                }
                // BoxCollider
                else if (collider is BoxCollider box)
                {
                    Matrix4x4 rotationMatrix = Matrix4x4.TRS(box.transform.position, box.transform.rotation, Vector3.one);
                    Gizmos.matrix = rotationMatrix;
                    Gizmos.DrawWireCube(box.center, box.size);
                    Gizmos.matrix = Matrix4x4.identity;
                }
                // SphereCollider
                else if (collider is SphereCollider sphere)
                {
                    Gizmos.DrawWireSphere(sphere.transform.position + sphere.center, sphere.radius);
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

        /// <summary>
        /// Game 视图可视化：把每个 hitbox 胶囊中心投影到屏幕画标记（十字+框）。
        /// 启用=绿色，禁用=红色。Play 模式下 Game/Scene 视图都可见。
        /// </summary>
        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            if (cachedColliders == null || cachedColliders.Count == 0) return;
            if (Camera.main == null) return;
            try
            {
                foreach (Collider collider in cachedColliders)
                {
                    if (collider == null) continue;
                    // 胶囊中心世界坐标
                    Vector3 worldPos = collider.transform.position;
                    if (collider is CapsuleCollider cap)
                        worldPos = collider.transform.TransformPoint(cap.center);

                    Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
                    if (screenPos.z <= 0) continue; // 在相机背后
                    float x = screenPos.x;
                    float y = Screen.height - screenPos.y; // GUI 原点左下

                    Color col = collider.enabled ? Color.green : new Color(1f, 0.3f, 0.3f, 0.9f);
                    var prevColor = GUI.color;
                    GUI.color = col;
                    // 十字 + 方框
                    GUI.DrawTexture(new Rect(x - 6, y - 1, 12, 2), WhiteTex);
                    GUI.DrawTexture(new Rect(x - 1, y - 6, 2, 12), WhiteTex);
                    GUI.DrawTexture(new Rect(x - 9, y - 9, 18, 18), WhiteTex); // 方框用白色纹理但半透明
                    GUI.color = prevColor;
                }
            }
            catch (System.Exception) { }
        }

        private static Texture2D WhiteTex
        {
            get
            {
                if (whiteTex == null)
                {
                    whiteTex = new Texture2D(1, 1);
                    whiteTex.SetPixel(0, 0, Color.white);
                    whiteTex.Apply();
                }
                return whiteTex;
            }
        }
        private static Texture2D whiteTex;
    }
}
