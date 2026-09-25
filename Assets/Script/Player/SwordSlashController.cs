using System.Collections.Generic;
using UnityEngine;

namespace Script.Player
{
    /// <summary>
    /// 玩家分段刀光播放器（Hovl Sword Slash 特效）—— 实时轨迹跟随版。
    /// 挂载位置：Player（与 PlayerAttack 同物体）。
    ///
    /// 时序：由 PlayerAttack.OnAttackSwing（= EnableHitbox 动画事件，伤害帧开始）触发；
    ///       刀光存续时长 = 该刀伤害窗口，到期立即归池 —— 只有伤害帧才有刀光。
    ///
    /// 对齐原理（v2，实时跟随；取代旧的"录制三点静态圆"方案）：
    ///   每帧取刀尖(trajectorySource)最近 W 秒的玩家局部轨迹（W=该刀伤害窗口=duration），
    ///   三点拟合圆 → 环心=圆心、环面=路径平面、环外径=当前刀尖到圆心距离（刀尖=弧光外缘）、
    ///   弧起点对准窗口最老点（phaseDeg 可滑）。拟合结果做指数平滑后写入实例的局部变换：
    ///   实例挂在 Player 之下，随体动（转身/轴转/位移）天然同步。
    ///   退化保护：窗口近共线（直劈）或半径异常时冻结当前位姿，不乱跳。
    ///
    /// 环网格（Slash2/3/4.fbx 实测）几何事实：整环位于局部 XZ 平面（法线=局部 +Y），
    ///   内径 1.36 / 外径 2.5；uv.x = 角度/360°，u=0 锚在局部 -Z，朝局部 +X 推进；
    ///   角向揭示在 HS_SwordSlash_Wipe shadergraph（_RevealFrac 裁弧长 / _FlipWipe 镜像扫向，
    ///   揭示时钟=粒子顶点色 r，由 FitParticlesToCycle 写为随寿命线性 0→1）。flipSweep 手动翻转扫掠方向。
    ///
    /// 池化：按(段,刀)槽位分桶，实例 SetActive 复用，零 Instantiate/Destroy、零 GC。
    ///
    /// 检视模式（编辑器调参，见 BeginInspect/SetInspectVt/InspectStep）：
    ///   自动补录一次慢速挥刀 → 刀尖轨迹+动画进度逐帧存档 → 动画定格、预览实例由"虚拟时钟 vt"
    ///   精确重建位姿（无平滑），可帧步进/循环/停在任意时刻调参。真实池刀光在检视期间让位。
    /// </summary>
    public class SwordSlashController : MonoBehaviour
    {
        [System.Serializable]
        public class SlashPreset
        {
            [Tooltip("刀光预制体（留空 = 本刀不出刀光）")]
            public GameObject prefab;
            [Tooltip("位置微调（玩家局部轴；跟随已对齐，一般保持 0）")]
            public Vector3 positionOffset;
            [Tooltip("朝向微调（欧拉角，叠加在跟随结果上）")]
            public Vector3 rotationOffset;
            [Tooltip("半径乘数：1 = 刀尖精确落在弧光外缘（跟随自动定标）")]
            public Vector3 scale = Vector3.one;
            [Tooltip("刀光存续时长（秒，动画时间）= 该刀伤害窗口；同时是跟随窗口：环覆盖最近 duration 秒的刀尖轨迹")]
            public float duration = 0.15f;
            [Tooltip("出现时机（秒，动画时间）：0=伤害帧起手即出；>0=延后出现；<0=提前刀光——出场瞬间粒子时钟已快进 |此值|，弧光在伤害帧开始时已生长过半程。只提前刀光特效，不影响伤害帧本身")]
            [Range(-0.3f, 0.3f)] public float spawnDelay = 0f;
            [Tooltip("翻转扫掠：false=弧光从弧起点（伤害帧起点刀尖）沿挥刀前进方向生长（烘焙模板实测）；true=反向镜像")]
            public bool flipSweep;
            [Tooltip("相位：刀光弧沿轨迹圆滑动（度）。0=弧起点对准轨迹起点")]
            public float phaseDeg = 0f;
            [Tooltip("弧长比例＝伤害帧结束时弧光画到全弧的百分之几（0=自动：烘焙扫程角/资产全弧角）。" +
                     "弧尾冲过刀尖绕到背后→调小；结束时弧还没够到刀尖→调大")]
            [Range(0f, 1f)] public float arcFrac = 0f;
            [Tooltip("烟气浓度增幅：弧类粒子亮度渐变的 alpha 整体乘此值（1=原样，>1=更浓，饱和截到 1）。只改透明度，不改几何")]
            public float smokeBoost = 1f;
            [Tooltip("揭示保底比例（0~1）：出场第一帧起弧光至少已画到 扫程×此比例，且挥刀反摆熄灭后仍在已扫区域留一段残烟（烟气留存）。0=纯刀尖驱动")]
            [Range(0f, 1f)] public float revealSeedFrac = 0f;

            [Header("模板弧线（检视录制完成时自动烘焙，玩家局部）")]
            [Tooltip("已有烘焙弧线 = 实时刀光从出场第一帧起就用这条固定弧（与刀尖轨迹同平面同心），不再逐步收敛")]
            public bool hasBakedPose;
            public Vector3 bakedCenter;   // 弧心＝挥刀内角顶点（枢轴，一般在玩家模型附近）
            public Vector3 bakedNormal;   // 环面法线：按时间序 Newell 定向，flipSweep 作用于它
            public Vector3 bakedStart;    // 伤害帧起点刀尖位置（弧起点方向 = 弧心指向它）
            public float bakedRadius;     // 轨迹圆半径
            public float bakedSweepDeg;   // 伤害窗内刀尖扫过程角（度）→ 自动弧长裁切用

            [Header("挥刀全程窗（检视录制自动烘，动画 normalizedTime）")]
            [Tooltip("刀尖起动时刻（-1=未烘）。特效窗口起点：早于伤害窗起点→负 spawnDelay 提前出烟")]
            public float sweepHeadNorm = -1f;
            [Tooltip("EnableHitbox（PlaySlash 触发）时刻＝录制起点")]
            public float sweepStartNorm = -1f;
            [Tooltip("刀尖前向扫程停息（峰值回落）时刻＝特效释放点，晚于伤害窗终点")]
            public float sweepEndNorm = -1f;
            [Tooltip("起动→停息内首次到达扫程峰值-3° 的动画进度：实战释放=出场时进度+该跨度（对慢速恢复/取消免疫）")]
            public float sweepSpanNorm = -1f;

            [Tooltip("（旧时间预测曲线，已停用）揭示现由刀尖即时扫角逐帧驱动（RevealClock 写 _RevealFrac），本字段仅保留兼容")]
            public float[] revealCurve;
        }

        [Header("引用")]
        [Tooltip("兜底挂点（武器下 SlashSocket），仅作有效性检查")]
        public Transform socket;
        [Tooltip("轨迹采样点 = 用户标定的刀尖参考 BladeEdgeMark（SM_Katana01 子物体）：环贴的是它扫出的轨迹。留空回退 socket。")]
        public Transform trajectorySource;
        [Tooltip("生成时禁用的子物体（受击闪光与刀弧不同位置）")]
        public string[] disableOnSpawn = { "Hit" };

        [Header("分段分刀预设（槽位 = (段-1)*2 + (刀-1)）")]
        [Tooltip("0=A1 2=A2 4=A3第一刀 5=A3第二刀 6=A4；奇数槽预留给该段的额外刀数")]
        public SlashPreset[] presets = new SlashPreset[8];

        [Header("总开关")]
        [Tooltip("开启后攻击时才自动生成跟随轨迹的刀光。关闭 = 播放动作时完全无刀光（" +
                 "检视录制、手动刀气预览不受影响，可照常绑定 prefab 单独预览）。默认关，彻底清除旧特效行为。")]
        public bool autoSlashEnabled = false;

        [Header("跟随品质")]
        [Tooltip("环心/半径收敛速率（越大越紧跟刀尖，越小越平滑）")]
        [Range(5f, 80f)] public float centerFollowRate = 35f;
        [Tooltip("环面法线收敛速率（慢一点抑制平面翻滚抖动）")]
        [Range(2f, 40f)] public float planeFollowRate = 14f;
        [Tooltip("环心到刀尖距离超过该值视为窗口退化（近直线），冻结位姿")]
        public float maxTrackRadius = 6f;

        [Header("测试")]
        [Tooltip("慢动作系数（由 PlayerAttack.DisableControl 同步 attackSlowMo）：存续/跟随窗口按 1/此倍数 拉长，平滑收敛也按同比例放缓")]
        [Range(0.05f, 1f)] public float slowMotion = 1f;

        [Tooltip("资产全弧张角（度）：遮罩实测 Paint1≈340°。自动弧长比例 = 扫程角/此值；换遮罩资产再调")]
        [Range(30f, 360f)] public float fullArcDeg = 340f;

        [Header("检视（编辑器）")]
        [Tooltip("录制刀尖轨迹样本上限：约 60 秒实际时长，正常一刀远用不满")]
        public int inspectMaxSamples = 3600;

        // 环网格常量（Slash2/3/4.fbx 实测：局部 XZ 平面、法线 +Y、内径1.36/外径2.5、u0 在 -Z）
        const float RingOuterRadius = 2.5f;
        const float RingKeep = 0.5f;   // 局部轨迹环形缓存保持时长（覆盖最大跟随窗口）
        const float MinSin = 0.15f;    // 相对共线阈值：低于视为直线，冻结

        struct LocalSample { public Vector3 p; public float t; }

        class ActiveSlash
        {
            public GameObject instance;
            public float releaseTime;
            public int stateHash = -1;    // 出场时动画状态（-1=未烘，不做进度释放）
            public float releaseNorm = -1f; // 释放进度：当前状态 normalizedTime≥此值 或 状态切换 → 收
            public int poolKey;
            public SlashPreset preset;
            public bool hasState;
            public bool bakedPose; // 该刀光使用预设烘焙弧：出场即固定位姿，不再逐帧拟合收敛
            public Vector3 center;   // 玩家局部：平滑后的环心
            public Vector3 normal;   // 玩家局部：平滑后的环面法线（未 flip）
            public float radius;     // 平滑后的路径半径

            // 刀尖即时揭示：出场即把弧起点边重锚到当前刀尖方向（烘焙参考系相对实战出场帧有陈旧角），
            // 弧头角度=刀尖相对锚点的当前扫角（出场=0），越界即灭、弧长上限封顶。
            public Vector3 revealN;    // 揭示平面法线（含 flip）
            public Vector3 revealC;    // 揭示平面环心
            public Vector3 anchorDir;  // 弧起点边方向（出场重锚到刀尖后）
            public Material[] wipeMats;
            public float prevTipDeg;
        }

        private readonly Dictionary<int, Queue<GameObject>> pools = new Dictionary<int, Queue<GameObject>>();
        private readonly List<ActiveSlash> actives = new List<ActiveSlash>();
        private readonly List<LocalSample> ringBuf = new List<LocalSample>();

        class PendingSlash { public float spawnTime; public int key; public SlashPreset preset; }
        private readonly List<PendingSlash> pendings = new List<PendingSlash>();

        /// <summary>检视录制样本：刀尖玩家局部位置 + 记录时刻 + 该时刻动画归一化进度（定格显示姿势用）。</summary>
        struct TipSample { public Vector3 p; public float t; public float animNorm; }

        // ———————————————————— 检视模式（编辑器调参） ————————————————————
        [System.NonSerialized] public bool inspectRecording;
        [System.NonSerialized] public bool inspectReady;
        [System.NonSerialized] public bool inspectLoop;
        /// <summary>检视循环播放的时钟倍速（面板滑条）：1=原速，调小慢放观察。</summary>
        [System.NonSerialized] public float inspectLoopSpeed = 1f;
        /// <summary>当前检视虚拟时钟 vt（实际秒，相对伤害帧起点；只读，窗口用面板设置）。</summary>
        [System.NonSerialized] public float inspectVt;
        public float InspectVtMax { get; private set; }
        /// <summary>检视定格显示用的动画归一化进度（录制结束时所在帧）。</summary>
        public float InspectAnimNorm { get; private set; }

        /// <summary>面板是否已武装录制（等待该槽位伤害帧事件）。</summary>
        public bool InspectArmed => pendingSlot >= 0;
        /// <summary>动画秒 → 实际秒 的公开包装（面板显示延迟/存续用）。</summary>
        public float SecOf(float animSec) => Sec(animSec);
        /// <summary>已录制的轨迹样本数（录制中进度显示用）。</summary>
        public int InspectSampleCount => inspSamples.Count;

        private Animator anim;
        private int pendingSlot = -1;          // 等待 EnableHitbox 事件（PlaySlash 被调用）确认的槽位
        private float pendingSince;
        private float recordStart;             // = 伤害帧起点 Time.time（eventT）
        private float recordDelay;             // 该槽 spawnDelay（实际秒）
        private float recordDur;               // 该槽 duration（实际秒）
        private readonly List<TipSample> inspSamples = new List<TipSample>();
        private GameObject previewGo;
        private Material[] previewWipeMats;   // 预览实例的 Wipe 遮罩材质（检视揭示随时钟写入）
        private float previewPrevTipDeg = -999f;

        // 手动刀气预览（在玩家处生成、循环播放，不依赖检视录制）
        [System.NonSerialized] public bool manualActive;
        private GameObject manualGo;
        private int manualSlot = -1;
        private Vector3 manualBaseLocal;
        const float ManualBaseScale = 0.4f; // 手动模式初始环大小（无轨迹定标，给个可看起手值）
        private float manualVt;              // 手动循环虚拟时钟（实际秒）
        private float manualPauseUntil;      // 每轮结束后短暂停顿的解除时刻
        [Tooltip("手动刀气循环的时钟倍速：1=原速，<1 慢放观察")]
        public float manualSpeed = 1f;

        // 检视帧步进：驱动动画姿势 scrub +（可选）刀光跟随
        [System.NonSerialized] public bool inspectScrubSlashFollows = true; // 勾选：刀光随帧一起出现/消失
        private int inspCursor;
        private int inspStateHash;

        // 检视视角接管：MaxSpeed 置 0 掐断被动鼠标转视角，右键拖拽由本组件直接驱动 FreeLook 轴
        private Cinemachine.CinemachineFreeLook inspFreeLook;
        private Behaviour inspCamReset;      // PlayerCameraReset（检视期间禁用，避免其俯仰钳制与拖拽打架）
        private float savedXMax, savedYMax;
        private bool inspCamApplied;
        [Tooltip("检视右键拖拽灵敏度：水平=度/像素，垂直=归一化/像素。方向反了就调负")]
        [System.NonSerialized] public float inspectYawPerPx = 0.15f;
        [System.NonSerialized] public float inspectPitchPerPx = 0.0016f;

        /// <summary>进入检视：武装录制窗口，等待该槽位的一次伤害帧事件（面板负责强制出刀）。自动补录一刀的真实轨迹后定格。</summary>
        public void BeginInspect(int slot)
        {
            if (presets == null || slot < 0 || slot >= presets.Length) return;
            SlashPreset p = presets[slot];
            if (p == null || p.prefab == null) return;
            EndInspect();
            // 单刀光不变式：检视期间全场只允许预览这一把 ——
            // 自动移除手动刀气，并把所有还在存活的实时刀光/待生成刀光一次清掉
            RemoveManualSlash();
            for (int i = actives.Count - 1; i >= 0; i--) Release(actives[i]);
            actives.Clear();
            pendings.Clear();
            ApplyInspectCamera();
            pendingSlot = slot;
            pendingSince = Time.time;
            recordDelay = Sec(p.spawnDelay);
            recordDur = Sec(p.duration);
            slowMotion = Mathf.Max(0.05f, slowMotion); // 录制期间维持慢速：样本更密、观察更细
            if (anim == null) anim = GetComponent<Animator>();
        }

        /// <summary>退出检视：销毁预览实例、解除动画冻结、还原视角控制、清理录制状态。</summary>
        public void EndInspect()
        {
            pendingSlot = -1;
            inspectRecording = false; inspectReady = false; inspectLoop = false;
            ClearTipMarkers();
            inspSamples.Clear();
            if (previewGo != null) Destroy(previewGo);
            previewGo = null;
            previewWipeMats = null; previewPrevTipDeg = -999f;
            if (anim != null) anim.speed = 1f; // 之后每次攻击由 DisableControl 重新设慢动作
            RestoreInspectCamera();
        }

        // ——— 检视视角接管 ———

        private void ApplyInspectCamera()
        {
            if (inspCamApplied) return;
            if (inspFreeLook == null)
                inspFreeLook = UnityEngine.Object.FindFirstObjectByType<Cinemachine.CinemachineFreeLook>();
            if (inspFreeLook == null) return; // 无 FreeLook（可能用了别的相机方案）：仅冻结轨道输入失败，退化为原样
            savedXMax = inspFreeLook.m_XAxis.m_MaxSpeed;
            savedYMax = inspFreeLook.m_YAxis.m_MaxSpeed;
            inspFreeLook.m_XAxis.m_MaxSpeed = 0f; // 掐断被动鼠标转视角
            inspFreeLook.m_YAxis.m_MaxSpeed = 0f;
            if (inspCamReset == null) inspCamReset = UnityEngine.Object.FindFirstObjectByType<PlayerCameraReset>();
            if (inspCamReset != null) inspCamReset.enabled = false;
            inspCamApplied = true;
        }

        private void RestoreInspectCamera()
        {
            if (!inspCamApplied) return;
            if (inspFreeLook != null)
            {
                inspFreeLook.m_XAxis.m_MaxSpeed = savedXMax;
                inspFreeLook.m_YAxis.m_MaxSpeed = savedYMax;
            }
            if (inspCamReset != null) inspCamReset.enabled = true;
            inspFreeLook = null; inspCamReset = null; inspCamApplied = false;
        }

        /// <summary>检视期间：仅右键按住时按拖拽量直接驱动 FreeLook 轨道轴（像 Scene 一样）。</summary>
        private void TickInspectCamera()
        {
            if (!inspCamApplied || inspFreeLook == null) return;
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null || !m.rightButton.isPressed) return;
            Vector2 d = m.delta.ReadValue();
            inspFreeLook.m_XAxis.Value += d.x * inspectYawPerPx;
            inspFreeLook.m_YAxis.Value = Mathf.Clamp01(inspFreeLook.m_YAxis.Value + d.y * inspectPitchPerPx);
        }

        /// <summary>帧步进：vt 增减 dt（实际秒），面板用 1/60 秒步长。</summary>
        public void InspectStep(float dt)
        {
            if (inspectReady) SetInspectVt(Mathf.Clamp(inspectVt + dt, 0f, InspectVtMax));
        }

        /// <summary>
        /// 设置检视虚拟时钟 vt（实际秒，0=伤害帧起点）：
        /// 刀光环位姿 = 用 vt 时刻仍在窗口内的录制样本三点拟合圆（精确拟合、无平滑）；
        /// 粒子以重启方式模拟到 vt 对应特效时长（定格/步进/循环都是这一条路径）；
        /// 姿势 = 样本记录的动画进度（速度 0，面板 Play(0, norm) 定格）。
        /// </summary>
        public void SetInspectVt(float vt)
        {
            inspectVt = vt;
            if (!inspectReady || previewGo == null || pendingSlot < 0) return;
            SlashPreset preset = presets[pendingSlot];
            if (preset == null) return;

            float window = Mathf.Max(0.02f, recordDur);
            float t = Mathf.Max(0f, vt);
            bool visible = vt >= recordDelay && vt <= recordDelay + recordDur;
            previewGo.SetActive(visible);
            if (!visible) return;

            // 预览 = 实战：有烘焙弧时直接摆那条固定弧（与实时生成完全同一位姿，自全程不变）；
            // 无烘焙弧退化为 vt 滑窗拟合（与实时跟随同语义）。
            if (preset.hasBakedPose)
            {
                ApplyBakedPose(preset, previewGo.transform);
            }
            else
            {
                Vector3 arcC; Vector3 arcN; float arcR; Vector3 arcOld;
                if (TryFitArcWindow(t - window, t, out arcC, out arcN, out arcR, out arcOld))
                {
                    Vector3 w2 = preset.flipSweep ? -arcN : arcN;
                    Vector3 w1 = Vector3.ProjectOnPlane(arcOld - arcC, w2);
                    if (w1.sqrMagnitude < 1e-6f) w1 = Vector3.ProjectOnPlane(transform.forward, w2);
                    if (w1.sqrMagnitude < 1e-6f) w1 = Vector3.right;
                    else w1.Normalize();
                    Transform tr = previewGo.transform;
                    tr.localRotation = RingBasis(w1, w2)
                        * Quaternion.AngleAxis(preset.phaseDeg, Vector3.up)
                        * Quaternion.Euler(preset.rotationOffset);
                    tr.localPosition = arcC + preset.positionOffset;
                    float s = arcR / RingOuterRadius * preset.scale.x;
                    tr.localScale = new Vector3(s, s, s);
                }
                else // 直劈/退化：沿用当前位姿，只移动不重拟合（与实时跟随同样的冻结策略）
                {
                    previewGo.transform.localPosition = SampleAt(t) + preset.positionOffset;
                }
            }

            // 粒子按实际秒推进（拟合时间轴同为实际秒）：弧生长周期与伤害帧窗口严格一致；
            // spawnDelay<0（提前刀光）→ recordDelay 为负，vt=0 时粒子时钟已带 |delay| 的进度。
            float el = vt - recordDelay;                    // 粒子时钟进度（也=刀尖扫角采样进度）
            el = Mathf.Clamp(el, 0f, recordDur);
            // 检视预览与实战同一语义：出场帧刀尖=弧起点边（重锚），揭示=该 vt 时刻刀尖自锚点起的
            // 带符号累计扫角（回摆会缩短）。累计起点=粒子出场时刻（负 delay 钳到 0）。
            if (preset.hasBakedPose && previewWipeMats != null && previewWipeMats.Length > 0)
            {
                Vector3 n = preset.flipSweep ? -preset.bakedNormal : preset.bakedNormal;
                float tSpawn = Mathf.Max(0f, recordDelay);
                // ApplyBakedPose 每次调用都重置位姿 → 重锚幂等
                Vector3 anchor = ReanchorArcToTip(previewGo.transform, preset.bakedCenter, n,
                    AtTime(tSpawn));
                float sweep = SweepAtTimeDeg(anchor, preset, n, tSpawn, tSpawn + el);
                RevealClock(previewWipeMats, preset, ref previewPrevTipDeg, sweep, false);
            }
            SimulateParticles(previewGo, el); // recordDur=Fit 时同一实际秒周期，防中途倍率变化错位
        }

        /// <summary>检视：从 tFrom 起以 1/60 步长累计刀尖相对锚点（v0，出场重锚方向）的带符号扫角（帧间解包络，回摆为负增长）。</summary>
        private float SweepAtTimeDeg(Vector3 v0, SlashPreset p, Vector3 n, float tFrom, float tTo)
        {
            if (v0.sqrMagnitude < 1e-8f) return 0f;
            const float dt = 1f / 60f;
            int steps = Mathf.Max(1, Mathf.CeilToInt((tTo - tFrom) / dt));
            float acc = 0f, prev = 0f;
            for (int i = 0; i <= steps; i++)
            {
                Vector3 c = Vector3.ProjectOnPlane(AtTime(Mathf.Min(tFrom + i * dt, tTo)) - p.bakedCenter, n);
                if (c.sqrMagnitude < 1e-10f) continue;
                float a = Vector3.SignedAngle(v0, c, n);
                if (i > 0) acc += a - prev - Mathf.Round((a - prev) / 360f) * 360f;
                prev = a;
            }
            return acc;
        }

        /// <summary>
        /// 跳帧（伤害帧起点/结束）：以动作为基准 —— 玩家姿势 scrub 到 vt 对应的录制时刻
        /// （inspCursor 同步，之后帧步进从该处继续）；勾选“刀光随帧播放”时刀光 vt 一起跳过去，
        /// 未勾选则刀光冻结在原处，只换姿势。手动导航会先停掉循环，刀光不再自行推进。
        /// </summary>
        public void InspectJumpVt(float targetVt)
        {
            if (!inspectReady) return;
            inspectLoop = false;
            float vt = Mathf.Clamp(targetVt, 0f, InspectVtMax);
            if (inspSamples.Count > 0)
            {
                inspCursor = IndexNear(vt);
                float norm = Mathf.Max(0f, inspSamples[inspCursor].animNorm);
                if (anim != null && inspStateHash != 0)
                {
                    anim.Play(inspStateHash, 0, norm);
                    anim.Update(0f);
                }
            }
            if (inspectScrubSlashFollows) SetInspectVt(vt);
            InspectPlaceTipMarkers(); // 伤害帧起点/结束：刀尖位置留校准标记
        }

        /// <summary>
        /// 定格最亮刀光：只把刀光 vt 定到指定时刻（面板传窗口中点=弧最大最亮），不碰玩家姿势、
        /// 与“刀光随帧播放”开关无关；自动停循环。检视里刀光是唯一可控对象时就靠它。
        /// </summary>
        public void InspectFreezeSlash(float targetVt)
        {
            if (!inspectReady) return;
            inspectLoop = false;
            SetInspectVt(Mathf.Clamp(targetVt, 0f, InspectVtMax));
        }

        /// <summary>面板滑条改了预设参数：刷新窗口/上限并重建当前 vt 位姿（循环中的延迟到下一帧生效）。</summary>
        public void RefreshInspect()
        {
            if (!inspectReady) return;
            SlashPreset p = presets[pendingSlot];
            if (p != null)
            {
                recordDelay = Sec(p.spawnDelay);
                recordDur = Sec(p.duration);
                InspectVtMax = recordDelay + recordDur + 0.08f;
                BakeArcFromRecording(); // 窗口时长变了 → 模板弧同步重烘（退化则保留旧值）
                if (previewGo != null) FitParticlesToCycle(previewGo, recordDur, RevealFrac(p), p); // 粒子时间轴跟着滑条实时重建
            }
            SetInspectVt(Mathf.Min(inspectVt, InspectVtMax));
        }

        /// <summary>
        /// 检视帧步进：把动画姿势 scrub 到第 dir 帧（改 inspCursor → 该帧 animNorm）。
        /// inspectScrubSlashFollows=true 时刀光 vt 也跟到该帧（一起出现/消失）；
        /// =false 时只动姿势、刀光冻结在当前位置（方便对着某个姿势调摆放）。
        /// </summary>
        public void InspectStepFrames(int dir)
        {
            if (!inspectReady || inspSamples.Count == 0) return;
            inspectLoop = false; // 手动步进：刀光只随操作变化，先停循环
            inspCursor = Mathf.Clamp(inspCursor + dir, 0, inspSamples.Count - 1);
            float t = inspSamples[inspCursor].t;
            float norm = Mathf.Max(0f, inspSamples[inspCursor].animNorm);
            if (anim != null && inspStateHash != 0)
            {
                anim.Play(inspStateHash, 0, norm);
                anim.Update(0f);
            }
            if (inspectScrubSlashFollows) SetInspectVt(Mathf.Clamp(t, 0f, InspectVtMax));
        }

        // ———————————————————— 模板烘焙（用检视录制数据算贴合参数） ————————————————————

        /// <summary>
        /// 模板拟合：取本次检视录制的刀尖轨迹在 伤害帧起点/中点/终点 的三个位置定圆（虚拟弧线，仅供计算），
        /// 计算刀光环对齐该圆的残差（径向/面内/扫掠角/共线度），并把模板参数写进该槽预设：
        /// scale=1（刀尖恒在弧光外缘）、偏移/旋转=0、phaseDeg 与 flipSweep 由参数给定。
        /// 返回一行报告（面板/探针用）。退化轨迹（近直线槽位）只报数据不覆盖已有相位。
        /// </summary>
        public string InspectTemplateFit(float templatePhaseDeg, bool templateFlipSweep)
        {
            if (!inspectReady || pendingSlot < 0 || inspSamples.Count < 4) return "fit skipped: no inspect data";
            SlashPreset p = presets[pendingSlot];
            if (p == null) return "fit skipped: no preset";

            p.flipSweep = templateFlipSweep;
            p.scale = Vector3.one;
            p.positionOffset = Vector3.zero;
            p.rotationOffset = Vector3.zero;

            Vector3 c; Vector3 nrm; float r; Vector3 oldP; float err;
            bool ok = TryFitArcWindow(0f, recordDelay + recordDur, out c, out nrm, out r, out oldP, out err);
            if (!ok)
            {
                Vector3 p0 = AtTime(0f), p1 = AtTime(recordDelay + recordDur);
                return "slot" + pendingSlot + " DEGENERATE(近直线/张角不足) keep phase"
                    + " | tip起=" + Fmt(p0) + " 终=" + Fmt(p1)
                    + " | flip/scale/offset written";
            }
            p.phaseDeg = templatePhaseDeg;

            float mid = recordDelay + recordDur * 0.5f;
            return "slot" + pendingSlot
                + " | 内角顶点(弧心,玩家局部)=" + Fmt(c) + " 距原点=" + c.magnitude.ToString("F2")
                + " R=" + r.ToString("F3") + " 平均径向误差=" + err.ToString("F4")
                + " | tip起/中/终=" + Fmt(AtTime(0f)) + "/" + Fmt(AtTime(mid)) + "/" + Fmt(AtTime(recordDelay + recordDur))
                + " | TEMPLATE WRITTEN (phase=" + templatePhaseDeg.ToString("F0") + " flip=" + templateFlipSweep + ")";
        }

        private static string Fmt(Vector3 v)
        {
            return "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2") + ")";
        }

        /// <summary>
        /// 录制完成即烘焙：对整段伤害窗口 [0, delay+dur] 做最小二乘定圆，把固定弧几何
        /// （弧心/法线/起点/半径）写进该槽预设。退化则保留旧烘焙值。
        /// </summary>
        private void BakeArcFromRecording()
        {
            if (pendingSlot < 0 || presets == null || pendingSlot >= presets.Length) return;
            SlashPreset p = presets[pendingSlot];
            if (p == null) return;
            Vector3 c; Vector3 n; float r; Vector3 oldP; float err;
            // 窗口＝伤害帧起点 → 刀光消失时刻；提前刀光（recordDelay<0）时消失虽早，但固定弧仍要盖满整段挥刀轨迹
            float winEnd = recordDelay > 0f ? recordDelay + recordDur : recordDur;
            if (!TryFitArcWindow(0f, winEnd, out c, out n, out r, out oldP, out err)) return;
            p.hasBakedPose = true;
            p.bakedCenter = c; p.bakedNormal = n; p.bakedStart = oldP; p.bakedRadius = r;
            Vector3 endP = AtTime(winEnd);
            p.bakedSweepDeg = Mathf.Abs(Vector3.SignedAngle(oldP - c, endP - c, n)); // 起点刀尖→终点刀尖对弧心的张角
            BakeRevealCurve(p, winEnd);
            BakeSweepWindow(p); // 挥刀起动/停息动画进度 → 派生 spawnDelay(负)/duration=全程窗（廿五轮：特效与刀运动全程一致）
        }

        /// <summary>
        /// 烘"挥刀全程窗"：在录制样本上做刀尖累计前向扫角曲线（解包络，dt=clip/30 实际秒），
        /// 找"升≥8° 后回落≥15°"的所有局部峰中抬起幅值最大者＝该刀主挥程（取最大幅值而非第一个：起手回摆小峰幅值远小于主挥）；
        /// 停息=峰后首次回落到 峰-3°；起动=首次抬起 3°。累计角方向无关（正/负扫角取 |抬起| 大的一侧为正向）。
        /// 派生写回既有旋钮（运行公式原样复用）：spawnDelay=-(起动→伤害窗起点)（负=提前刀光，出场烟即中段密度）、
        /// duration=(起动→停息)、sweepSpanNorm=(伤害窗起点→停息)（实战释放=出场时进度+此跨度，秒表兜底）。
        /// 仅在录制刚完成（当前状态=inspStateHash）时可读 clip 长度，面板改时长路径会跳过、保留旧烘值。
        /// </summary>
        private void BakeSweepWindow(SlashPreset p)
        {
            if (anim == null || inspSamples.Count < 8 || !p.hasBakedPose) return;
            AnimatorStateInfo st = anim.GetCurrentAnimatorStateInfo(0);
            if (st.fullPathHash != inspStateHash) return;
            float clipLen = st.length;
            if (clipLen < 0.1f) return;
            float clipReal = clipLen / Mathf.Max(0.05f, slowMotion);     // 进度 1 = clipReal 实际秒
            Vector3 c = p.bakedCenter;
            Vector3 n = p.flipSweep ? -p.bakedNormal : p.bakedNormal;
            Vector3 v0 = Vector3.ProjectOnPlane(p.bakedStart - c, n);
            if (v0.sqrMagnitude < 1e-8f) return;

            float dt = clipReal / 30f;
            float tMin = Mathf.Max(inspSamples[0].t, -0.35f * clipReal);
            float tMax = Mathf.Min(inspSamples[inspSamples.Count - 1].t,
                                   recordDelay + recordDur + 0.35f * clipReal);
            int pts = Mathf.Max(8, Mathf.CeilToInt((tMax - tMin) / dt) + 1);
            var ts = new float[pts];
            var cum = new float[pts];
            float prevA = 0f;
            for (int k = 0; k < pts; k++)
            {
                float t = Mathf.Min(tMin + k * dt, tMax);
                ts[k] = t;
                Vector3 v = Vector3.ProjectOnPlane(AtTime(t) - c, n);
                if (v.sqrMagnitude < 1e-10f) { cum[k] = k > 0 ? cum[k - 1] : 0f; continue; }
                float a = Vector3.SignedAngle(v0, v, n);
                if (k > 0) cum[k] = cum[k - 1] + (a - prevA - Mathf.Round((a - prevA) / 360f) * 360f);
                prevA = a;
            }
            // 方向无关：刀尖前向扫角可正可负，取 |抬起| 更大的一侧作为"正向"（廿五轮修：A1 因符号反了整窗烘不出）
            float maxPos = cum[0], minNeg = cum[0];
            for (int k = 1; k < pts; k++) { if (cum[k] > maxPos) maxPos = cum[k]; if (cum[k] < minNeg) minNeg = cum[k]; }
            if (maxPos < -minNeg) for (int k = 0; k < pts; k++) cum[k] = -cum[k];
            // 主挥峰＝所有"升≥8° 后回落≥15°"局部峰里峰顶抬起最大者（起手回摆的小峰幅值远小于主挥，不取第一个）
            int kPeak = -1; float bestTop = 8f;
            for (int k = 0; k < pts; k++)
            {
                if (cum[k] < 8f) continue;
                int top = k, j = k;
                while (j + 1 < pts && cum[j + 1] >= cum[top] - 2f) { j++; if (cum[j] > cum[top]) top = j; }
                if (j + 1 < pts && cum[j + 1] < cum[top] - 15f && cum[top] > bestTop) { kPeak = top; bestTop = cum[top]; }
                k = j;
            }
            if (kPeak < 0) { kPeak = 0; for (int k = 1; k < pts; k++) if (cum[k] > cum[kPeak]) kPeak = k; } // 兜底=全局峰
            float peak = cum[kPeak];
            if (peak < 20f) return; // 无明确主挥抬起（<20°，含起手噪声/近直线槽）：本槽保留旧 duration/spawnDelay，不烘
            int kEnd = kPeak;
            while (kEnd + 1 < pts && cum[kEnd + 1] > peak - 3f) kEnd++;             // 停息=峰后首次回落 3°
            int kHead = 0;
            while (kHead < kPeak && cum[kHead] < 3f) kHead++;                        // 起动=首次抬起 3°
            if (kHead >= kPeak) kHead = 0;
            if (ts[kEnd] <= 0f) return; // 停息早于伤害帧起点＝检测异常（会致 span 钳最小、特效秒释放），本槽不烘

            // t=0 录制起点＝PlaySlash(EnableHitbox) 帧；其动画进度直接取首个正时刻样本记录值
            float norm0 = 0f;
            for (int i = 0; i < inspSamples.Count; i++)
                if (inspSamples[i].t >= 0f) { norm0 = inspSamples[i].animNorm; break; }

            p.sweepStartNorm = norm0;
            p.sweepHeadNorm = norm0 + ts[kHead] / clipReal;
            p.sweepEndNorm = norm0 + ts[kEnd] / clipReal;
            p.sweepSpanNorm = Mathf.Max(0.02f, (ts[kEnd] - 0f) / clipReal);
            p.bakedSweepDeg = Mathf.Max(p.bakedSweepDeg, peak - cum[kHead]); // 弧长=起动→峰实扫角（不缩于伤害窗瞬时值）

            float preRoll = (p.sweepStartNorm - p.sweepHeadNorm) * clipLen;
            p.spawnDelay = -Mathf.Clamp(preRoll, 0f, 0.3f);
            // 时长=实际出场（伤害窗起点+delay，delay 可能被 0.3s 钳短）→停息。head 晚于 start 时出场钳在 start，
            // 若仍按 (end-head) 算会把寿命缩短 (head-start)，特效早于刀尖停息释放＝"早于攻击结束"回归。
            float spawnNorm = p.sweepStartNorm + p.spawnDelay / clipLen;
            p.duration = Mathf.Clamp(p.sweepEndNorm - spawnNorm, 0.05f, 1.5f) * clipLen;
        }

        const int RevealCurvePts = 11;

        /// <summary>
        /// 从录制样本烘"揭示运动曲线"：刀尖对弧心的累计扫角（起点=0，挥刀方向为正）按粒子时钟
        /// 等距采 11 点，再除以终点值归一到 0~1。FitParticlesToCycle 用它重排 r 通道
        /// —— 揭示进度=刀尖当前扫角，不再=线性时间：前半窗弧跟着刀的爆发加速、后半窗刀尖回摆
        /// 时弧同步停/收，不会在轨迹另一侧继续延伸。
        /// 累计角用帧间增量累加（解包络）：SignedAngle 直接取值在扫角跨过 ±180° 时会折回
        /// （A3-2 实测 171° 附近中窗假性归零、曲线成阶跃）；离线数据单帧增量 ~4°，累加精确。
        /// 注意时钟对齐：粒子出场在动画时间 recordDelay（负 delay=提前出场），曲线覆盖
        /// [出场, 出场+粒子寿命]，与池实例存活窗一一对应。
        /// </summary>
        private void BakeRevealCurve(SlashPreset p, float winEnd)
        {
            Vector3 c = p.bakedCenter, n = p.bakedNormal;
            Vector3 v0 = p.bakedStart - c;
            if (v0.sqrMagnitude < 1e-8f || inspSamples.Count < 6 || p.bakedSweepDeg < 1f) return;
            float tFrom = recordDelay;                    // 粒子出场时刻（相对伤害帧起点，动画秒）
            // 覆盖窗与池实例存活窗严格一致（SpawnSlash 同式）：出场 → 出场+（duration 含负 delay 扣减）
            float span = Mathf.Max(0.01f, p.duration + Mathf.Min(0f, p.spawnDelay));
            var cum = new float[RevealCurvePts];
            Vector3 prev = v0;
            for (int k = 0; k < RevealCurvePts; k++)
            {
                Vector3 t = AtTime(tFrom + span * k / (RevealCurvePts - 1)) - c;
                if (t.sqrMagnitude < 1e-10f) t = prev;
                // 累计 |路径转角|：揭示"已扫过的弧长"本质是路径量，不能用起终点瞬时角——
                // 瞬时角在跨 ±180° 时折回、反摆时跳负（slot5 实测阶跃/失真），路径量天然连续且单调。
                if (k > 0) cum[k] = cum[k - 1] + Mathf.Abs(Vector3.SignedAngle(prev, t, n));
                prev = t;
            }
            float norm = cum[RevealCurvePts - 1];         // 窗内总扫角 = 归一分母
            if (Mathf.Abs(norm) < 1f) return;             // 几乎没转（原地回摆/退化），曲线不可信
            var curve = new float[RevealCurvePts];
            for (int k = 0; k < RevealCurvePts; k++)
                curve[k] = Mathf.Clamp01(cum[k] / norm);
            p.revealCurve = curve;
        }

        /// <summary>
        /// 用预设的烘焙弧给一个刀光实例摆固定位姿：环面=轨迹平面、环心=内角顶点、
        /// 半径=轨迹圆半径，自始至终不变（实例挂在玩家下，随玩家移动/转向）。
        /// </summary>
        private void ApplyBakedPose(SlashPreset preset, Transform tr)
        {
            Vector3 w2 = preset.flipSweep ? -preset.bakedNormal : preset.bakedNormal;
            Vector3 w1 = Vector3.ProjectOnPlane(preset.bakedStart - preset.bakedCenter, w2);
            if (w1.sqrMagnitude < 1e-6f) w1 = Vector3.ProjectOnPlane(transform.forward, w2);
            if (w1.sqrMagnitude < 1e-6f) w1 = Vector3.right;
            else w1.Normalize();
            tr.localRotation = RingBasis(w1, w2)
                * Quaternion.AngleAxis(preset.phaseDeg, Vector3.up)
                * Quaternion.Euler(preset.rotationOffset);
            tr.localPosition = preset.bakedCenter + preset.positionOffset;
            float s = preset.bakedRadius / RingOuterRadius * preset.scale.x;
            tr.localScale = new Vector3(s, s, s);
        }

        // ———————————————————— 刀尖校准标记（伤害帧起点/终点） ————————————————————

        private GameObject tipMarkerStart, tipMarkerEnd;

        /// <summary>
        /// 在 伤害帧起点/终点 的刀尖位置放置校准标记球（绿=起点、红=终点），随检视销毁。
        /// 按"伤害帧起点/结束"按钮时自动调用，供对照刀光弧线端点是否落在刀尖轨迹上。
        /// </summary>
        public void InspectPlaceTipMarkers()
        {
            if (!inspectReady || inspSamples.Count == 0) return;
            PlaceTipMarker(ref tipMarkerStart, "TipMarker(伤害帧起点)", AtTime(0f), new Color(0.2f, 1f, 0.3f));
            PlaceTipMarker(ref tipMarkerEnd, "TipMarker(伤害帧终点)", AtTime(recordDelay + recordDur), new Color(1f, 0.25f, 0.2f));
        }

        private void PlaceTipMarker(ref GameObject go, string name, Vector3 localTip, Color c)
        {
            if (go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                go.transform.SetParent(transform, true);
            }
            go.name = name;
            go.transform.localPosition = localTip;
            go.transform.localScale = Vector3.one * 0.05f;
            Renderer rd = go.GetComponent<Renderer>();
            if (rd != null)
            {
                if (rd.sharedMaterial == null)
                {
                    Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                    if (sh == null) sh = Shader.Find("Standard");
                    var m = new Material(sh);
                    m.SetColor("_BaseColor", c);
                    m.SetColor("_Color", c);
                    rd.sharedMaterial = m;
                }
                rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rd.receiveShadows = false;
            }
        }

        private void ClearTipMarkers()
        {
            if (tipMarkerStart != null) Destroy(tipMarkerStart);
            if (tipMarkerEnd != null) Destroy(tipMarkerEnd);
            tipMarkerStart = tipMarkerEnd = null;
        }

        // ———————————————————— 手动刀气预览（循环） ————————————————————

        /// <summary>在玩家处（刀尖初始位置）生成一把刀光并循环播放，供面板滑条手动调整位置/角度/大小。</summary>
        public void AddManualSlash(int slot)
        {
            if (presets == null || slot < 0 || slot >= presets.Length) return;
            SlashPreset p = presets[slot];
            if (p == null || p.prefab == null) return;
            // 单刀光不变式：刀气与检视预览互斥，添加刀气先退出检视定格
            EndInspect();
            RemoveManualSlash();
            manualGo = Instantiate(p.prefab);
            manualGo.name = p.prefab.name + "(刀气·手动)";
            ApplySpawnVisibility(manualGo);
            manualGo.transform.SetParent(transform, true);
            manualBaseLocal = transform.InverseTransformPoint(SamplePos); // 初始落在刀尖
            manualSlot = slot;
            manualActive = true;
            manualVt = 0f;
            manualPauseUntil = 0f;
            SetParticlesLoop(manualGo, false); // 关掉粒子自身循环，播放完全由本组件虚拟时钟驱动
            FitParticlesToCycle(manualGo, Sec(p.duration), RevealFrac(p), p); // 粒子时间轴 = 伤害窗口的真实时长（提前量由负 delay 自动带出）
            TickManual();
        }

        public void RemoveManualSlash()
        {
            manualActive = false; manualSlot = -1;
            if (manualGo != null) Destroy(manualGo);
            manualGo = null;
        }

        private void ApplyManualPose()
        {
            if (!manualActive || manualGo == null || manualSlot < 0 || manualSlot >= presets.Length) return;
            SlashPreset p = presets[manualSlot];
            if (p == null) return;
            Transform tr = manualGo.transform;
            tr.localPosition = manualBaseLocal + p.positionOffset;
            tr.localRotation = Quaternion.Euler(p.rotationOffset) * Quaternion.AngleAxis(p.phaseDeg, Vector3.up);
            tr.localScale = new Vector3(ManualBaseScale * p.scale.x, ManualBaseScale * p.scale.y, ManualBaseScale * p.scale.z);
        }

        /// <summary>
        /// 手动刀气时钟：每轮 = 延迟出现 + 存续时间（动画秒按慢动作换算成实际秒），
        /// 存续段内刀光存在、粒子按虚拟时钟推进——"存续时间"就是刀光的存在时长。
        /// manualSpeed 倍速控制整轮播放快慢；轮间 0.35s 短停顿便于看清一轮的起终点。
        /// </summary>
        private void TickManual()
        {
            ApplyManualPose();
            if (manualGo == null || manualSlot < 0 || manualSlot >= presets.Length) return;
            SlashPreset p = presets[manualSlot];
            if (p == null) return;
            float delay = Sec(p.spawnDelay); // 负=提前刀光：一轮提前 |delay| 出场、粒子时钟起点带进度
            float dur = Mathf.Max(0.02f, Sec(p.duration));
            float cycle = Mathf.Max(0.05f, delay + dur);
            if (Time.time < manualPauseUntil)
            {
                manualGo.SetActive(false);
                return;
            }
            manualVt += Time.deltaTime * Mathf.Max(0.05f, manualSpeed);
            if (manualVt >= cycle)
            {
                manualVt = 0f;
                manualPauseUntil = Time.time + 0.35f;
                manualGo.SetActive(false);
                SimulateParticles(manualGo, 0f);
                return;
            }
            bool visible = manualVt >= delay;
            manualGo.SetActive(visible);
            if (visible)
            {
                float elM = manualVt - delay; // 实际秒轴：与重建后的粒子时间轴同单位；负 delay 在 vt=0 即带进度
                SlashPreset pm = presets[manualSlot];
                SimulateParticles(manualGo, Mathf.Clamp(elM, 0f, pm != null ? Sec(pm.duration) : elM));
            }
        }

        private static void SetParticlesLoop(GameObject go, bool loop)
        {
            ParticleSystem[] list = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < list.Length; i++)
            {
                ParticleSystem.MainModule main = list[i].main;
                main.loop = loop;
                if (loop) list[i].Play(true);
            }
        }

        // ——— 检视内部 ———

        private void TickInspect()
        {
            // 等待伤害帧事件确认录制起点；20 秒没等到则取消（面板会显示超时）。就绪后 pendingSlot 需保留（拟合按槽位取预设）
            if (pendingSlot >= 0 && !inspectRecording && !inspectReady && Time.time - pendingSince > 20f) pendingSlot = -1;

            if (inspectRecording)
            {
                float vt = Time.time - recordStart;
                float norm = anim != null ? anim.GetCurrentAnimatorStateInfo(0).normalizedTime : 0f;
                inspSamples.Add(new TipSample { p = transform.InverseTransformPoint(SamplePos), t = vt, animNorm = norm });
                if (inspSamples.Count > inspectMaxSamples) inspSamples.RemoveAt(0);
                // 尾窗 0.6 实际秒：必须盖到挥刀停息（伤害窗结束后的随挥/回落），BakeSweepWindow 才找得到峰回落
                if (vt > recordDelay + recordDur + 0.6f) CompleteRecording();
            }

            if (inspectReady && inspectLoop)
            {
                inspectVt += Time.deltaTime * Mathf.Max(0.05f, inspectLoopSpeed);
                if (inspectVt > InspectVtMax)
                {
                    inspectLoopPauseUntil = Time.time + 0.5f;
                    inspectVt = 0f;
                    SetInspectVt(0f); // 轮间短停：先收掉刀光，下轮从"未出现"开始
                }
                if (Time.time >= inspectLoopPauseUntil) SetInspectVt(inspectVt);
            }
        }

        private float inspectLoopPauseUntil;

        private void StartInspectRecording()
        {
            inspectRecording = true;
            inspSamples.Clear();
            recordStart = Time.time;
            // 起点之前：用实时轨迹缓存回填（覆盖刀已挥出的前半段）；起点之后：TickInspect 高频直录
            for (int i = 0; i < ringBuf.Count; i++)
            {
                float t = ringBuf[i].t - recordStart;
                if (t >= 0f) break;
                inspSamples.Add(new TipSample { p = ringBuf[i].p, t = t, animNorm = 0f });
            }
        }

        private void CompleteRecording()
        {
            inspectRecording = false;
            if (inspSamples.Count < 4) return; // 数据不足：留 pending 让用户重试
            SlashPreset p0 = presets[pendingSlot];
            if (p0 != null) // 统一为定格时刻的实际秒（BeginInspect 时慢速倍率可能还是旧值）
            {
                recordDelay = Sec(p0.spawnDelay);
                recordDur = Sec(p0.duration);
            }
            InspectVtMax = recordDelay + recordDur + 0.08f;
            if (anim != null) inspStateHash = anim.GetCurrentAnimatorStateInfo(0).fullPathHash; // 先锁状态：BakeSweepWindow 用它校验"录制刚完成"（廿五轮修正：原来在烘焙之后才赋值，全程窗永远烘不出）
            BakeArcFromRecording();            // 全窗口最小二乘弧 → 写进预设（实时/检视共用这条固定弧）
            if (anim != null)
            {
                InspectAnimNorm = inspSamples[inspSamples.Count - 1].animNorm;
                anim.speed = 0f; // 定格；面板帧步时改用 Play(state,0,norm) scrub
            }
            inspCursor = 0;
            for (int i = 0; i < inspSamples.Count; i++) if (inspSamples[i].t >= 0f) { inspCursor = i; break; }
            if (previewGo == null)
            {
                SlashPreset p = presets[pendingSlot];
                previewGo = Instantiate(p.prefab);
                previewGo.name = p.prefab.name + "(inspect)";
                ApplySpawnVisibility(previewGo);
                previewGo.transform.SetParent(transform, true);
                FitParticlesToCycle(previewGo, recordDur, RevealFrac(p), p); // 预览与实战同一粒子时间轴（实际秒 = 伤害窗口真实时长）
            }
            previewWipeMats = FindWipeMats(previewGo);
            previewPrevTipDeg = -999f;
            inspectReady = true;
            SetInspectVt(0f);
        }

        private readonly Vector3[] arcBuf = new Vector3[48]; // 窗口拟合的均匀采样缓冲

        /// <summary>圆弧径向平均偏差容忍（×半径）：超出＝该段不是圆弧（如 A4 复合大横扫），收缩滑窗改取可圆表示的最长子段。</summary>
        const float ArcErrRatio = 0.25f;

        /// <summary>
        /// 对 [tFrom,tTo] 窗口内的刀尖样本（玩家局部）做最小二乘定圆：
        /// 法线＝时间序 Newell 累积（与实时跟随 old→mid→new 叉积同一定向约定）；
        /// 圆心＝挥刀弧的"内角顶点"（旋转枢轴，通常落在玩家模型附近）。
        /// 整窗不合格时按 75%/55%/40%/28% 逐步收缩滑窗，取最长合格弧段（张角最大者优先）。
        /// false＝退化（样本过少/近共线/所有子段张角过小或明显非圆），调用方应保持位姿。
        /// </summary>
        private bool TryFitArcWindow(float tFrom, float tTo, out Vector3 center, out Vector3 normal,
            out float radius, out Vector3 oldestPt)
        {
            return TryFitArcWindow(tFrom, tTo, out center, out normal, out radius, out oldestPt, out _);
        }

        private bool TryFitArcWindow(float tFrom, float tTo, out Vector3 center, out Vector3 normal,
            out float radius, out Vector3 oldestPt, out float meanErr)
        {
            center = Vector3.zero; normal = Vector3.up; radius = 0f; oldestPt = Vector3.zero; meanErr = 0f;
            int n = inspSamples.Count;
            int lo = 0, hi = n - 1;
            while (lo < n && inspSamples[lo].t < tFrom) lo++;
            while (hi >= 0 && inspSamples[hi].t > tTo) hi--;
            int span = hi - lo + 1;
            if (span < 6) return false;

            float[] ratios = { 1f, 0.75f, 0.55f, 0.4f, 0.28f };
            float bestSweep = -1f;
            int hitLo = -1;
            Vector3 hc = Vector3.zero; Vector3 hn = Vector3.up; Vector3 ho = Vector3.zero;
            float hr = 0f, herr = 0f;
            for (int k = 0; k < ratios.Length && hitLo < 0; k++)
            {
                int len = Mathf.Max(8, Mathf.RoundToInt(span * ratios[k]));
                int starts = span - len + 1;
                int step = Mathf.Max(1, starts / 6);
                for (int s = 0; s < starts; s += step)
                {
                    Vector3 c; Vector3 nm; float r; float sweep; float err;
                    if (!FitSampleRange(lo + s, lo + s + len - 1, out c, out nm, out r, out sweep, out err)) continue;
                    if (sweep > bestSweep)
                    {
                        bestSweep = sweep; hitLo = lo + s;
                        hc = c; hn = nm; ho = inspSamples[lo + s].p; hr = r; herr = err;
                    }
                }
            }
            if (hitLo < 0) return false;
            center = hc; normal = hn; radius = hr; meanErr = herr; oldestPt = ho;
            return true;
        }

        /// <summary>对样本下标区间 [iLo,iHi] 做 Newell 法线 + 平面内 Kasa 最小二乘圆；张角≥8°且径向误差≤25%R 才算合格圆弧。</summary>
        private bool FitSampleRange(int iLo, int iHi, out Vector3 center, out Vector3 normal,
            out float radius, out float absSweep, out float meanErr)
        {
            center = Vector3.zero; normal = Vector3.up; radius = 0f; absSweep = 0f; meanErr = 0f;
            int span = iHi - iLo + 1;
            int cnt = Mathf.Min(arcBuf.Length, span);
            for (int j = 0; j < cnt; j++)
            {
                int idx = iLo + Mathf.RoundToInt((float)j * (iHi - iLo) / (cnt - 1));
                arcBuf[j] = inspSamples[idx].p;
            }
            Vector3 mean = Vector3.zero;
            for (int j = 0; j < cnt; j++) mean += arcBuf[j];
            mean /= cnt;

            Vector3 nrm = Vector3.zero;
            for (int j = 1; j < cnt - 1; j++)
                nrm += Vector3.Cross(arcBuf[j] - arcBuf[0], arcBuf[j + 1] - arcBuf[0]);
            if (nrm.sqrMagnitude < 1e-10f) return false;
            normal = nrm.normalized;

            Vector3 u = Vector3.ProjectOnPlane(arcBuf[cnt - 1] - arcBuf[0], normal);
            if (u.sqrMagnitude < 1e-8f)
            {
                u = Vector3.ProjectOnPlane(arcBuf[cnt / 2] - mean, normal);
                if (u.sqrMagnitude < 1e-8f) u = Vector3.ProjectOnPlane(Vector3.up, normal);
                if (u.sqrMagnitude < 1e-8f) u = Vector3.ProjectOnPlane(Vector3.right, normal);
                if (u.sqrMagnitude < 1e-8f) return false;
            }
            u.Normalize();
            Vector3 v = Vector3.Cross(normal, u);

            float Suu = 0, Suv = 0, Svv = 0, SuSS = 0, SvSS = 0, S = 0;
            for (int j = 0; j < cnt; j++)
            {
                Vector3 d = arcBuf[j] - mean;
                float x = Vector3.Dot(d, u), y = Vector3.Dot(d, v);
                float s2 = x * x + y * y;
                Suu += x * x; Suv += x * y; Svv += y * y;
                SuSS += x * s2; SvSS += y * s2; S += s2;
            }
            float det = Suu * Svv - Suv * Suv;
            if (Mathf.Abs(det) < 1e-8f) return false;
            float D = (-SuSS * Svv + SvSS * Suv) / det;
            float E = (-Suu * SvSS + Suv * SuSS) / det;
            float cx = -D * 0.5f, cy = -E * 0.5f;
            Vector3 c = mean + u * cx + v * cy;
            float r2 = cx * cx + cy * cy + S / cnt;
            if (r2 <= 0.0025f) return false;
            float r = Mathf.Sqrt(r2);
            if (r > maxTrackRadius) return false;

            Vector3 a0 = Vector3.ProjectOnPlane(arcBuf[0] - c, normal);
            Vector3 a1 = Vector3.ProjectOnPlane(arcBuf[cnt - 1] - c, normal);
            absSweep = Mathf.Abs(Vector3.SignedAngle(a0, a1, normal));
            if (absSweep < 8f) return false; // 张角过小：半径不可信

            float err = 0f;
            for (int j = 0; j < cnt; j++) err += Mathf.Abs((arcBuf[j] - c).magnitude - r);
            err /= cnt;
            if (err > r * ArcErrRatio) return false; // 明显不是圆弧不硬拟合

            center = c; radius = r; meanErr = err;
            return true;
        }

        private Vector3 AtTime(float t)
        {
            int n = inspSamples.Count;
            if (n == 0) return Vector3.zero;
            if (t <= inspSamples[0].t) return inspSamples[0].p;
            if (t >= inspSamples[n - 1].t) return inspSamples[n - 1].p;
            // 线性插值（旧版"就近样本±1平均"会在样本中点处整窗跳格 → 拟合抖动的来源）
            for (int i = 1; i < n; i++)
            {
                if (inspSamples[i].t >= t)
                {
                    float f = (t - inspSamples[i - 1].t) / Mathf.Max(1e-5f, inspSamples[i].t - inspSamples[i - 1].t);
                    return Vector3.Lerp(inspSamples[i - 1].p, inspSamples[i].p, f);
                }
            }
            return inspSamples[n - 1].p;
        }

        private Vector3 SampleAt(float t)
        {
            return inspSamples[Mathf.Clamp(IndexNear(t), 0, inspSamples.Count - 1)].p;
        }

        private int IndexNear(float t)
        {
            int best = 0;
            float bestD = Mathf.Infinity;
            for (int i = 0; i < inspSamples.Count; i++)
            {
                float d = Mathf.Abs(inspSamples[i].t - t);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private static void SimulateParticles(GameObject go, float t)
        {
            ParticleSystem[] list = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < list.Length; i++) list[i].Simulate(t, true, true);
        }

        /// <summary>三点外接圆：圆心/半径/法线；近共线返回 false。</summary>
        private static bool TryFitCircle(Vector3 pa, Vector3 pb, Vector3 pc,
            out Vector3 center, out float radius, out Vector3 normal)
        {
            center = Vector3.zero; radius = 0f; normal = Vector3.zero;
            Vector3 ab = pb - pa, ac = pc - pa;
            Vector3 n = Vector3.Cross(ab, ac);
            if (n.sqrMagnitude < 1e-8f) return false;
            center = pa + (Vector3.Cross(n, ab) * ac.sqrMagnitude
                          + Vector3.Cross(ac, n) * ab.sqrMagnitude) / (2f * n.sqrMagnitude);
            radius = (pc - center).magnitude;
            normal = n.normalized;
            return radius >= 0.05f;
        }


        /// <summary>轨迹采样世界坐标：优先刀刃（玩家看到的挥刀轨迹是刀刃扫的，不是刀根）。</summary>
        private Vector3 SamplePos => trajectorySource != null ? trajectorySource.position : socket.position;

        /// <summary>动画秒 → 实际秒（慢动作放大）。</summary>
        private float Sec(float animSec) => animSec / Mathf.Max(0.05f, slowMotion);

        /// <summary>环形缓存保持时长（实际秒）：≥ 最长跟随窗口 + 余量。</summary>
        private float KeepWindow()
        {
            float k = RingKeep;
            if (presets != null)
                for (int i = 0; i < presets.Length; i++)
                    if (presets[i] != null) k = Mathf.Max(k, Sec(presets[i].duration) + 0.1f);
            return Mathf.Min(k, 4f);
        }

        private float keepWindow = RingKeep; // 上一帧缓存的保持时长（UpdateTracking 取窗口用）

        private void Update()
        {
            // 检视右键拖拽转视角：须在 Cinemachine brain(LateUpdate) 前写入轴向值
            if (inspCamApplied) TickInspectCamera();
        }

        private void LateUpdate()
        {
            if (socket == null) return;
            keepWindow = KeepWindow();
            ringBuf.Add(new LocalSample { p = transform.InverseTransformPoint(SamplePos), t = Time.time });
            while (ringBuf.Count > 0 && Time.time - ringBuf[0].t > keepWindow)
                ringBuf.RemoveAt(0);

            AnimatorStateInfo liveState = default;
            bool haveState = anim != null;
            if (haveState) liveState = anim.GetCurrentAnimatorStateInfo(0);

            for (int i = actives.Count - 1; i >= 0; i--)
            {
                ActiveSlash a = actives[i];
                // 释放双保险：秒表到点（慢速恒定时的常规路径）或 动画进度到达停息点/状态已切换
                //（段末 speed 恢复 1、连段取消等秒表算不准的场合由进度线兜住）
                bool ended = Time.time >= a.releaseTime;
                if (!ended && a.releaseNorm >= 0f && haveState &&
                    (liveState.fullPathHash != a.stateHash || liveState.normalizedTime >= a.releaseNorm))
                    ended = true;
                if (ended)
                {
                    Release(a);
                    actives.RemoveAt(i);
                    continue;
                }
                if (!a.bakedPose) UpdateTracking(a); // 烘焙弧：出场即定，全程不再逐帧拟合
                else if (a.wipeMats != null)
                {
                    // 揭示=刀尖相对锚点（出场重锚=刀尖）的当前扫角：向前长、越界即灭，弧头永不超前刀尖
                    float tip = TipSweepDeg(a.anchorDir, a.revealC, a.revealN,
                        transform.InverseTransformPoint(SamplePos));
                    RevealClock(a.wipeMats, a.preset, ref a.prevTipDeg, tip, false);
                }
            }

            for (int i = pendings.Count - 1; i >= 0; i--)
            {
                if (Time.time < pendings[i].spawnTime) continue;
                PendingSlash p = pendings[i];
                pendings.RemoveAt(i);
                SpawnSlash(p.key, p.preset);
            }

            TickInspect();

            if (manualActive) TickManual(); // 位姿实时反映滑条 + 驱动循环时钟
        }

        /// <summary>
        /// 播放一刀的刀光。attackIndex 1~4，swingIndex 从 1 起（A3 有 1/2 两刀）。
        /// </summary>
        public void PlaySlash(int attackIndex, int swingIndex)
        {
            int key = (attackIndex - 1) * 2 + (swingIndex - 1);
            if (presets == null || key < 0 || key >= presets.Length) return;
            SlashPreset preset = presets[key];
            if (preset == null || preset.prefab == null) return;
            if (socket == null)
            {
                Debug.LogWarning("SwordSlashController: socket 未指定，跳过本次刀光。", this);
                return;
            }

            // 检视录制：目标槽位的伤害帧事件到达 → 从此刻起高频录制刀尖轨迹；真实池刀光让位给预览实例
            if (key == pendingSlot && !inspectRecording && !inspectReady) StartInspectRecording();
            if (inspectRecording || inspectReady) return;

            // 自动刀光总开关：关闭时攻击动画完全不生成刀光（清静状态）；
            // 检视录制、手动刀气预览不占用此路径，仍可用。
            if (!autoSlashEnabled) return;

            if (preset.spawnDelay > 0.0001f)
            {
                pendings.Add(new PendingSlash { spawnTime = Time.time + Sec(preset.spawnDelay), key = key, preset = preset });
                return;
            }
            SpawnSlash(key, preset);
        }

        private void SpawnSlash(int key, SlashPreset preset)
        {
            // 提前刀光：spawnDelay<0 → 出场瞬间把粒子时钟快进 |delay|（只提前特效进度，不动伤害帧）
            float adv = preset.spawnDelay < -0.0001f ? Sec(-preset.spawnDelay) : 0f;
            ActiveSlash a = new ActiveSlash
            {
                preset = preset,
                poolKey = key,
                releaseTime = Time.time + Sec(Mathf.Max(0.02f, preset.duration + Mathf.Min(0f, preset.spawnDelay)))
            };
            a.instance = Rent(key, preset.prefab);
            FitParticlesToCycle(a.instance, Sec(preset.duration), RevealFrac(preset), preset); // 粒子周期=本刀伤害窗口（实际秒）：随刀尖同步生长、结束弧尾恰达刀尖
            if (adv > 0f)
            {
                SimulateParticles(a.instance, Mathf.Min(adv, Sec(preset.duration)));
                // Simulate 会把粒子系统留在暂停态（廿六轮探针实锤：ps.time 全程恒定）→ 立即 Play 续跑时钟，
                // 亮度包络继续推进；Play 不重启，从已模拟时刻恢复。
                ParticleSystem rootPs = a.instance.GetComponentInChildren<ParticleSystem>(true);
                if (rootPs != null) rootPs.Play(true);
            }
            actives.Add(a);
            if (preset.hasBakedPose)
            {
                // 提前算准的模板弧：出场第一帧就与刀尖轨迹同平面、同心、同半径，全程不变；
                // 揭示几何交给刀尖即时扫角（弧头=刀尖在哪弧就到哪），粒子时钟只管淡出。
                a.bakedPose = true;
                a.hasState = true;
                ApplyBakedPose(preset, a.instance.transform);
                a.wipeMats = FindWipeMats(a.instance);
                // 出场即以当前刀尖重锚弧起点边：烘焙 bakedStart 是录制帧的刀尖方位，实战出场瞬间
                // 刀尖可能已扫过一大段（p49 实测 slot4 陈旧约 107°）→ 不重锚则首帧弧头直接顶满上限、
                // 随即被越界规则掐灭＝闪现闪烁。重锚＝把整条弧刚体旋转到"起点边=当前刀尖"。
                a.revealN = preset.flipSweep ? -preset.bakedNormal : preset.bakedNormal;
                a.revealC = preset.bakedCenter;
                Vector3 tipSpawn = transform.InverseTransformPoint(SamplePos);
                a.anchorDir = ReanchorArcToTip(a.instance.transform, a.revealC, a.revealN, tipSpawn);
                float tip0 = TipSweepDeg(a.anchorDir, a.revealC, a.revealN, tipSpawn); // 重锚后≈0
                a.prevTipDeg = tip0;
                RevealClock(a.wipeMats, preset, ref a.prevTipDeg, tip0, true);
                // 释放双保险之进度线：挥刀停息=动画进度起点+sweepSpanNorm（秒表 Sec() 在段末
                // animator.speed 恢复 1 后会算错，进度量不受速度切换影响）
                if (anim != null && preset.sweepSpanNorm > 0.01f)
                {
                    AnimatorStateInfo st = anim.GetCurrentAnimatorStateInfo(0);
                    a.stateHash = st.fullPathHash;
                    a.releaseNorm = st.normalizedTime + preset.sweepSpanNorm;
                }
                return;
            }
            UpdateTracking(a, true); // 无模板弧：起手帧吸附到当前窗口拟合（旧实时跟随路径，保留兜底）
        }

        // ———————————————————— 实时跟随 ————————————————————

        private void UpdateTracking(ActiveSlash a, bool snap = false)
        {
            SlashPreset preset = a.preset;
            float w = Mathf.Min(Sec(preset.duration), Mathf.Max(0.05f, keepWindow - 0.02f));
            float tNew = Time.time, tMid = Time.time - w * 0.5f, tOld = Time.time - w;
            if (!TryTriple(tOld, tMid, tNew, out Vector3 pa, out Vector3 pb, out Vector3 pc)) return;

            Vector3 ab = pb - pa, ac = pc - pa;
            Vector3 n = Vector3.Cross(ab, ac);
            float sin = n.magnitude / Mathf.Max(0.001f, ab.magnitude * ac.magnitude);
            if (sin < MinSin) return; // 直劈/退化：保持当前位姿不乱跳
            if (!TryFitCircle(pa, pb, pc, out Vector3 fitCenter, out float fitRadius, out Vector3 fitNormal)) return;
            if (fitRadius > maxTrackRadius) return;

            if (!a.hasState || snap)
            {
                a.center = fitCenter; a.normal = fitNormal; a.radius = fitRadius;
                a.hasState = true;
            }
            else
            {
                // 慢动作下挥刀在实际时间里变慢：收敛速率同乘 slowMotion，保持动画时间维度上相同的平滑特性
                float k = 1f - Mathf.Exp(-centerFollowRate * Mathf.Max(0.05f, slowMotion) * Time.deltaTime);
                float kp = 1f - Mathf.Exp(-planeFollowRate * Mathf.Max(0.05f, slowMotion) * Time.deltaTime);
                if (Vector3.Dot(fitNormal, a.normal) < 0f) fitNormal = -fitNormal; // 法线不定向，防 180° 翻转
                a.center = Vector3.Lerp(a.center, fitCenter, k);
                a.radius = Mathf.Lerp(a.radius, fitRadius, k);
                a.normal = Vector3.Slerp(a.normal, fitNormal, kp).normalized;
            }
            ApplyPose(a, pa);
        }

        private void ApplyPose(ActiveSlash a, Vector3 arcStartPt)
        {
            SlashPreset preset = a.preset;
            Vector3 w2 = preset.flipSweep ? -a.normal : a.normal; // 环面法线（局部）
            Vector3 w1 = Vector3.ProjectOnPlane(arcStartPt - a.center, w2); // 弧起点→轨迹窗口最老点
            if (w1.sqrMagnitude < 1e-6f) w1 = Vector3.ProjectOnPlane(transform.forward, w2);
            if (w1.sqrMagnitude < 1e-6f) w1 = Vector3.right;
            w1.Normalize();

            Transform tr = a.instance.transform;
            tr.localRotation = RingBasis(w1, w2)
                * Quaternion.AngleAxis(preset.phaseDeg, Vector3.up)
                * Quaternion.Euler(preset.rotationOffset);
            tr.localPosition = a.center + preset.positionOffset;
            float s = a.radius / RingOuterRadius * preset.scale.x; // 外径=当前刀尖圆半径 → 刀尖恒在弧光外缘
            tr.localScale = new Vector3(s, s, s);
        }

        /// <summary>
        /// 环基：局部 XZ 平面环，弧起点(u0)在局部 -Z、扫掠推进朝局部 +X。
        /// 令 局部-Z→u0Dir、局部+Y→planeNormal，则 局部+X=Cross(u0Dir,planeNormal)（右手系自洽）。
        /// </summary>
        private static Quaternion RingBasis(Vector3 u0Dir, Vector3 planeNormal)
        {
            Matrix4x4 m = Matrix4x4.identity;
            m.SetColumn(0, Vector3.Cross(u0Dir, planeNormal));
            m.SetColumn(1, planeNormal);
            m.SetColumn(2, -u0Dir);
            return m.rotation;
        }

        /// <summary>按时间就近取 老/中/新 三点（各 3 邻域平均抗噪）；窗口未满返回 false。</summary>
        private bool TryTriple(float tOld, float tMid, float tNew,
            out Vector3 pa, out Vector3 pb, out Vector3 pc)
        {
            pa = pb = pc = Vector3.zero;
            if (ringBuf.Count < 8) return false;
            if (ringBuf[0].t > tOld + 0.02f) return false; // 缓存没覆盖整个窗口
            int iOld = NearestIndex(tOld), iMid = NearestIndex(tMid), iNew = NearestIndex(tNew);
            pa = AvgAt(iOld); pb = AvgAt(iMid); pc = AvgAt(iNew);
            return (pc - pa).sqrMagnitude > 0.0001f;
        }

        private int NearestIndex(float t)
        {
            int best = 0;
            float bestD = Mathf.Infinity;
            for (int i = 0; i < ringBuf.Count; i++)
            {
                float d = Mathf.Abs(ringBuf[i].t - t);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private Vector3 AvgAt(int i)
        {
            int lo = Mathf.Max(0, i - 1), hi = Mathf.Min(ringBuf.Count - 1, i + 1);
            Vector3 sum = Vector3.zero;
            for (int k = lo; k <= hi; k++) sum += ringBuf[k].p;
            return sum / (hi - lo + 1);
        }

        // ———————————————————— 池 ————————————————————

        private GameObject Rent(int key, GameObject prefab)
        {
            Queue<GameObject> pool;
            if (!pools.TryGetValue(key, out pool))
            {
                pool = new Queue<GameObject>();
                pools[key] = pool;
            }
            GameObject go;
            if (pool.Count > 0)
            {
                go = pool.Dequeue();
                go.SetActive(true);
            }
            else
            {
                go = Instantiate(prefab);
                go.name = prefab.name + "(pooled)";
                ApplySpawnVisibility(go);
            }
            // 挂在玩家之下：拟合与位姿全在玩家局部空间 → 转身/轴转/位移时环与刀尖恒定同步。
            if (go.transform.parent != transform)
                go.transform.SetParent(transform, true);
            RestartParticles(go);
            return go;
        }

        private void ApplySpawnVisibility(GameObject go)
        {
            if (disableOnSpawn == null) return;
            foreach (var name in disableOnSpawn)
            {
                var t = FindDeep(go.transform, name);
                if (t != null) t.gameObject.SetActive(false);
            }
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == name) return c;
                var deep = FindDeep(c, name);
                if (deep != null) return deep;
            }
            return null;
        }

        private void Release(ActiveSlash a)
        {
            a.instance.SetActive(false);
            Queue<GameObject> pool;
            if (pools.TryGetValue(a.poolKey, out pool)) pool.Enqueue(a.instance);
        }

        private static void RestartParticles(GameObject go)
        {
            ParticleSystem[] list = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < list.Length; i++)
            {
                ParticleSystem ps = list[i];
                ps.Clear();
                ParticleSystem.MainModule main = ps.main;
                main.loop = false;
                main.playOnAwake = false;
                ps.Play(true);
            }
        }

        /// <summary>揭示终点比例：伤害帧结束时弧画到全弧的这个比例。槽位手动值优先；否则自动=烘焙扫程角/资产全弧角（资产遮罩实测 Paint1≈340° 张角，
        /// 而 A1 扫程仅 ~131°——不裁切则弧尾冲过刀尖绕到玩家背后）。</summary>
        private float RevealFrac(SlashPreset p)
        {
            if (p == null) return 1f;
            if (p.arcFrac > 0f) return Mathf.Clamp(p.arcFrac, 0.05f, 1f);
            if (p.hasBakedPose && p.bakedSweepDeg > 1f)
                return Mathf.Clamp(p.bakedSweepDeg / Mathf.Max(30f, fullArcDeg), 0.05f, 1f);
            return 1f;
        }

        // ———————————————————— 刀尖即时揭示（弧头=刀尖当前扫角） ————————————————————

        /// <summary>收集本刀光实例全部 Wipe 遮罩材质（renderer.material=实例材质，槽位间互不串扰）。</summary>
        private static Material[] FindWipeMats(GameObject go)
        {
            ParticleSystemRenderer[] rs = go.GetComponentsInChildren<ParticleSystemRenderer>(true);
            var lm = new List<Material>(rs.Length);
            for (int i = 0; i < rs.Length; i++)
            {
                Material sm = rs[i].sharedMaterial;
                if (sm == null || !sm.shader.name.Contains("Wipe")) continue;
                Material m = rs[i].material; // 实例材质：_RevealFrac 逐帧写入只影响本刀光
                if (m != null && m.HasProperty("_RevealFrac")) lm.Add(m);
            }
            return lm.ToArray();
        }

        /// <summary>
        /// 把 Wipe 遮罩的揭示量写为 刀尖当前扫角 比例（纯几何，与粒子时钟无关）：
        /// 扫角=弧起点边→刀尖方向 在烘焙平面上的带符号连续角（帧间 ±180° 解包络，防高速大挥瞬移）。
        /// ≤0（刀尖回到起点之后/反侧）→ 全灭；上限封在弧长比例×资产全弧角 → 永不画出轨迹之外。
        /// 回摆时弧随之缩短 —— 刀尖在哪，弧头就到哪。
        /// </summary>
        private void RevealClock(Material[] mats, SlashPreset p, ref float prevTipDeg, float tipDeg, bool force)
        {
            if (mats == null || mats.Length == 0 || p == null) return;
            float arcEndDeg = Mathf.Clamp01(RevealFrac(p)) * Mathf.Max(30f, fullArcDeg);
            // 单调峰值跟踪：每帧只加"帧间最小角差"（±180 解包络），刀尖前进→弧长增，
            // 回摆→保持（弧尾半程起本来就在渐隐）；越出 [0,弧长] 容差＝不在轨迹上 → 直接熄灭。
            // 防棘轮的关键：prev 永远被钳在 [-1, 弧长+60] 内，噪声/反侧瞬时角无法沿 ±360 阶梯爬升
            //（旧版"按最近值解包络"实测把 -138° 的起点侧瞬时角棘轮成 106.8° 整弧瞬现）。
            float a = force ? tipDeg : prevTipDeg + Mathf.DeltaAngle(prevTipDeg, tipDeg);
            if (a < -12f || a > arcEndDeg + 120f) a = 0f;
            a = Mathf.Min(a, arcEndDeg);
            prevTipDeg = a;
            // 显示层保底（廿七轮）：seed×弧长 下限——出场首帧即有烟、反摆熄灭(a=0)后仍在已扫区域
            // 留一段残烟到窗口释放。prev 记未保底的 a，跟踪包络不受影响；像素恒在扫过程角范围内。
            float v = Mathf.Max(Mathf.Max(0f, a), p.revealSeedFrac * arcEndDeg) / 360f;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null)
                {
                    // _FlipWipe=1：掩码基准取 1-uv.x。网格 uv 推进方向实测＝绕面法线负角向
                    //（uv0.2 顶点 = -72°），而刀尖前进扫角为正 ⇒ flip=0 画出的楔形在刀尖反侧＝镜像弧根因。
                    mats[i].SetFloat("_FlipWipe", 1f);
                    mats[i].SetFloat("_RevealFrac", v);
                }
        }

        /// <summary>
        /// 出场重锚：把弧起点边（环局部 -Z=u0）在揭示平面内刚体旋转到当前刀尖方向，
        /// 返回归一化锚点方向；刀尖投影退化时不旋转、退回原起点边方向。
        /// </summary>
        private static Vector3 ReanchorArcToTip(Transform tr, Vector3 c, Vector3 n, Vector3 tipLocal)
        {
            Vector3 t = Vector3.ProjectOnPlane(tipLocal - c, n);
            Vector3 u0 = Vector3.ProjectOnPlane(tr.localRotation * new Vector3(0f, 0f, -1f), n);
            if (t.sqrMagnitude < 1e-6f)
                return u0.sqrMagnitude > 1e-6f ? u0.normalized : Vector3.zero;
            if (u0.sqrMagnitude > 1e-6f)
            {
                float dA = Vector3.SignedAngle(u0, t, n);
                if (Mathf.Abs(dA) > 0.01f)
                    tr.localRotation = Quaternion.AngleAxis(dA, n) * tr.localRotation;
            }
            return t.normalized;
        }

        /// <summary>刀尖相对弧起点边（锚点方向）的当前扫角（度，带符号，揭示平面内）。</summary>
        private static float TipSweepDeg(Vector3 anchorDir, Vector3 c, Vector3 n, Vector3 tipLocal)
        {
            if (anchorDir.sqrMagnitude < 1e-8f) return 0f;
            Vector3 t = Vector3.ProjectOnPlane(tipLocal - c, n);
            if (t.sqrMagnitude < 1e-8f) return 0f;
            return Vector3.SignedAngle(anchorDir, t, n);
        }

        /// <summary>
        /// 粒子时间轴重建（cycleSeconds 用实际秒 = Sec(动画时长)，与伤害窗口的真实时长一致）：
        /// ① 全部粒子：duration=窗口、寿命=窗口、startDelay=0、burst 出场时刻按窗口等比前移
        ///    —— 所有特效成分与刀轨迹共存亡（不早逝、不在释放瞬间留半截被硬切）。
        /// ② 弧类（Mesh+Wipe shader，根环与烟波弧）：colOL 的 r 通道恒 1 —— 揭示几何不由时间预测，
        ///    由 RevealClock 每帧按刀尖当前扫角写实例材质 _RevealFrac（弧头=刀尖，回摆即缩短）；
        ///    g/b/a 保留资产淡出曲线（归一化寿命 ⇒ 自动铺满整个窗口）。
        /// 首次见到某粒子系统时缓存其原始时间轴（实例池复用，改过的值必须从原值重算）。
        /// </summary>
        private class PsiOrig
        {
            public float dur;
            public float life;   // 原始 startLifetime（常数），非弧粒子按窗口等比缩用
            public bool arc;     // 弧类 = Mesh 渲染 + Wipe 遮罩 shader（根环与烟波弧同权：寿命=窗口、揭示由刀尖驱动）
            public Color[] samples; // 原始 colOL 渐变按归一化寿命 25 点等距采样（g/b/a 保留原曲线，r 恒写 1）
            public ParticleSystem.Burst[] bursts;
        }
        private static readonly System.Collections.Generic.Dictionary<int, PsiOrig> sPsiOrig =
            new System.Collections.Generic.Dictionary<int, PsiOrig>();

        /// <summary>揭示曲线等距 11 点线性插值（x=寿命比例 0~1）。</summary>
        static float EvalRevealCurve(float[] curve, float x)
        {
            float f = Mathf.Clamp01(x) * (RevealCurvePts - 1);
            int i = (int)f;
            if (i >= RevealCurvePts - 1) return curve[RevealCurvePts - 1];
            return Mathf.Lerp(curve[i], curve[i + 1], f - i);
        }

        private static void FitParticlesToCycle(GameObject go, float cycleSeconds, float revealFrac, SlashPreset preset)
        {
            bool flipWipe = preset != null && preset.flipSweep;
            float d = Mathf.Max(0.02f, cycleSeconds);
            ParticleSystem[] list = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < list.Length; i++)
            {
                ParticleSystem ps = list[i];
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // 先停：播放中改参数会被忽略并告警
                int id = ps.GetInstanceID();
                PsiOrig o;
                if (!sPsiOrig.TryGetValue(id, out o))
                {
                    o = new PsiOrig { dur = ps.main.duration, life = ps.main.startLifetime.constant };
                    var sr = ps.GetComponent<ParticleSystemRenderer>();
                    // 弧类判定：Mesh 渲染 + Wipe 遮罩 shader（根环与烟波弧同权吃揭示时钟）——
                    // 按 shader 而非材质名：换遮罩贴图/改材质名都不掉队。
                    o.arc = sr != null && sr.renderMode == ParticleSystemRenderMode.Mesh
                        && ps.colorOverLifetime.enabled
                        && sr.sharedMaterial != null && sr.sharedMaterial.shader.name.Contains("Wipe");
                    if (o.arc)
                    {
                        Gradient src = ps.colorOverLifetime.color.gradient;
                        o.samples = new Color[25];
                        for (int k = 0; k <= 24; k++) o.samples[k] = src.Evaluate(k / 24f);
                    }
                    var em0 = ps.emission;
                    if (em0.enabled && em0.burstCount > 0)
                    {
                        o.bursts = new ParticleSystem.Burst[em0.burstCount];
                        em0.GetBursts(o.bursts);
                    }
                    sPsiOrig[id] = o;
                }

                ParticleSystem.MainModule main = ps.main;
                main.duration = d;
                main.startDelay = new ParticleSystem.MinMaxCurve(0f);
                if (o.arc)
                {
                    // 弧类（根环+烟波弧）：寿命=窗口；r 通道恒 1 —— 揭示几何完全由刀尖即时扫角
                    // 逐帧写 _RevealFrac（RevealClock），时间轴只负责亮度淡出包络。
                    main.startLifetime = new ParticleSystem.MinMaxCurve(d);
                    var col = ps.colorOverLifetime;
                    if (col.enabled)
                    {
                        // Unity 梯度硬上限 8 个色键：SetKeys 超 8 会静默失败（写入被整体丢弃、读回仍是原资产渐变）
                        // 烟气浓度增幅（廿七轮）：alpha 键整体 ×smokeBoost，从原始采样重算故可反复调参不叠加
                        float boost = Mathf.Max(1f, preset != null ? preset.smokeBoost : 1f);
                        Gradient g = new Gradient();
                        var ck = new GradientColorKey[8];
                        var ak = new GradientAlphaKey[8];
                        for (int k = 0; k < 8; k++)
                        {
                            float x = k / 7f;
                            Color c = o.samples[Mathf.RoundToInt(x * 24f)];
                            c.a = Mathf.Clamp01(c.a * boost);
                            ck[k] = new GradientColorKey(new Color(1f, c.g, c.b, c.a), x);
                            ak[k] = new GradientAlphaKey(c.a, x);
                        }
                        g.SetKeys(ck, ak);
                        var cc = col.color;
                        cc.mode = ParticleSystemGradientMode.Gradient;
                        cc.gradient = g;
                        col.color = cc;
                    }
                    var psr = ps.GetComponent<ParticleSystemRenderer>();
                    var inst = psr != null ? psr.material : null;
                    if (inst != null && inst.HasProperty("_RevealFrac"))
                    {
                        inst.SetFloat("_RevealFrac", 0f); // 出场=0，首帧由 RevealClock 接管
                        inst.SetFloat("_FlipWipe", 1f);   // 恒 1：楔形须沿绕面法线正角向推进＝刀尖前进方向（见 RevealClock）
                    }
                }
                else if (o.life > 0.0001f)
                {
                    // 非弧子粒子（火星/烟柱/闪光）：寿命同样=窗口（各自曲线按归一化寿命自动铺满窗口），
                    // 特效整体与刀轨迹共存亡——不早逝、不在释放瞬间被硬切一半。
                    main.startLifetime = new ParticleSystem.MinMaxCurve(d);
                }
                var em = ps.emission;
                if (o.bursts != null)
                {
                    var bs = (ParticleSystem.Burst[])o.bursts.Clone();
                    float k2 = d / Mathf.Max(0.05f, o.dur);
                    for (int b = 0; b < bs.Length; b++) bs[b].time *= k2;
                    em.SetBursts(bs);
                }
            }
            RestartParticles(go);
        }

#if UNITY_EDITOR
        /// <summary>选中 Player：黄线=刀尖局部轨迹缓存；每把活跃刀光画白线 环心→刀尖（应恒等于外径）。</summary>
        private void OnDrawGizmosSelected()
        {
            for (int i = 1; i < ringBuf.Count; i++)
                Gizmos.DrawLine(transform.TransformPoint(ringBuf[i - 1].p), transform.TransformPoint(ringBuf[i].p));
            Gizmos.color = Color.white;
            for (int i = 0; i < actives.Count; i++)
            {
                ActiveSlash a = actives[i];
                if (!a.hasState) continue;
                Gizmos.DrawLine(transform.TransformPoint(a.center), transform.TransformPoint(a.center + a.normal * 0.15f));
                Gizmos.DrawWireSphere(transform.TransformPoint(a.center), a.radius);
            }
        }
#endif
    }
}
