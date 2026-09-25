using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Script.Player;

/// <summary>
/// 刀光调参面板：选槽位 → 拉滑条 → （Play 中）实时预览 → 停止 Play 后"写入场景并保存"定格。
/// 检视模式（Play 中）：自动补录一次慢速挥刀 → 动画定格 → 预览刀光按虚拟时钟重建，
///   可跳伤害帧起点/终点、帧步进、定格最亮半程、循环播放刀光。
/// 手动刀气：在玩家处生成一把刀光循环播放，不依赖录制，纯手动调位置/角度/大小。
/// 参数持久化：显式"保存"把当前所有参数写进 SlashTuning.json；"导入"从该文件读回（退出 Play 后点导入即可拿回）。
/// 菜单：Tools/刀光特效/刀光调参面板
/// </summary>
public class SwordSlashTunerWindow : EditorWindow
{
    static readonly GUIContent[] SlotLabels =
    {
        new GUIContent("A1（第一刀）"),
        new GUIContent("A2（第二刀）"),
        new GUIContent("A3 第一刀"),
        new GUIContent("A3 第二刀"),
        new GUIContent("A4（收尾）"),
    };
    static readonly int[] SlotKeys = { 0, 2, 4, 5, 6 };

    [System.Serializable]
    class SlotTune
    {
        public int key;
        public float spawnDelay, duration, phaseDeg;
        public Vector3 positionOffset, rotationOffset, scale;
        public bool flipSweep;
        public string prefabPath;
        // 检视录制自动烘焙的模板弧（玩家局部）；旧文件无此字段 → hasBakedPose=false，导入时不清空场景已有烘焙
        public bool hasBakedPose;
        public Vector3 bakedCenter, bakedNormal, bakedStart;
        public float bakedRadius;
        public float bakedSweepDeg;   // 扫程角（自动弧长裁切用）
        public float arcFrac;         // 手动弧长比例（0=自动）
        public float smokeBoost = 1f; // 烟气浓度增幅（alpha×，旧JSON缺字段=0→应用时钳到1）
        public float revealSeedFrac;  // 揭示保底比例（首帧烟量+反摆后残烟留存）
        public float[] revealCurve;   // 揭示运动曲线（刀尖扫角归一 11 点，烘焙自动写）
        public float sweepHeadNorm, sweepStartNorm, sweepEndNorm, sweepSpanNorm; // 挥刀全程窗（动画进度，烘焙自动写）
    }

    [System.Serializable]
    class TuneSnap
    {
        public SlotTune[] slots = new SlotTune[0];
        public float centerFollowRate = -1f, planeFollowRate = -1f, maxTrackRadius = -1f, attackSlowMo = -1f;
        /// <summary>攻击自动生成刀光总开关：-1=旧文件未记录（导入时不改），0=关，1=开。</summary>
        public float autoSlashMark = -1f;
    }

    // 参数文件：项目根（与 Assets 平级），不进资产库
    static string TuneFilePath =>
        System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "SlashTuning.json");

    SwordSlashController target;
    int slotKey = 0;
    Vector2 scroll;
    string notice = "";

    static SwordSlashTunerWindow instance;

    [MenuItem("Tools/刀光特效/刀光调参面板")]
    static void Open()
    {
        var w = GetWindow<SwordSlashTunerWindow>("刀光调参");
        w.minSize = new Vector2(330, 560);
    }

    void OnEnable()
    {
        if (target == null) TryFind();
        instance = this;
        EditorApplication.update -= RepaintHook;
        EditorApplication.update += RepaintHook;
        // 模板烘焙结果落地：FinishBake 用 EditorPrefs 传过域重载，这里检测并写回场景
        if (EditorPrefs.GetInt("SwordSlash.TemplateBake.apply", 0) == 1)
        {
            EditorPrefs.DeleteKey("SwordSlash.TemplateBake.apply");
            ApplyBakedTemplate();
        }
    }

    void OnDisable()
    {
        EditorApplication.update -= RepaintHook;
        if (instance == this) instance = null;
        // 关窗时若仍在 Play 中：清理检视与手动刀气（退出 Play 时 target 随场景销毁，自然跳过）
        if (target != null && EditorApplication.isPlaying)
        {
            target.EndInspect();
            target.RemoveManualSlash();
        }
    }

    static void RepaintHook()
    {
        if (instance != null && EditorApplication.isPlaying && instance.target != null) instance.Repaint();
    }

    void TryFind()
    {
        target = UnityEngine.Object.FindFirstObjectByType<Script.Player.SwordSlashController>();
        if (target == null)
            target = UnityEngine.Object.FindAnyObjectByType<Script.Player.SwordSlashController>();
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        target = EditorGUILayout.ObjectField("控制器（Player）", target, typeof(SwordSlashController), true) as SwordSlashController;
        if (target == null)
        {
            EditorGUILayout.HelpBox("没有 SwordSlashController 引用。点“从场景查找”，或把 Player 拖进此栏。", MessageType.Info);
            if (GUILayout.Button("从场景查找")) TryFind();
            EditorGUILayout.EndScrollView();
            return;
        }

        bool playing = EditorApplication.isPlaying;
        EditorGUILayout.HelpBox(playing
            ? "Play 中：改滑条立即作用于当前刀光（实时预览）。停 Play 会回滚——调满意后点底部“保存参数到文件”，停止 Play 再点“从文件导入”即可拿回。"
            : "编辑模式：改完点底部“写入场景并保存”定格到场景；或“保存参数到文件”备份。检视/刀气调参请进 Play 模式。",
            MessageType.Info);

        if (target.presets == null || slotKey >= target.presets.Length)
        {
            EditorGUILayout.EndScrollView();
            return;
        }
        slotKey = EditorGUILayout.IntPopup(new GUIContent("槽位"), slotKey, SlotLabels, SlotKeys);
        var preset = target.presets[slotKey];
        if (preset == null)
        {
            EditorGUILayout.HelpBox("该槽位还没有预设。", MessageType.Warning);
            if (GUILayout.Button("创建预设（默认参数）"))
            {
                target.presets[slotKey] = new SwordSlashController.SlashPreset();
                EditorUtility.SetDirty(target);
            }
            EditorGUILayout.EndScrollView();
            return;
        }

        EditorGUILayout.LabelField("资产", EditorStyles.boldLabel);
        preset.prefab = (GameObject)EditorGUILayout.ObjectField("刀光预制体", preset.prefab, typeof(GameObject), false);

        EditorGUILayout.LabelField("出现时机", EditorStyles.boldLabel);
        preset.spawnDelay = EditorGUILayout.Slider(
            new GUIContent("出现时机（秒）", "0=伤害帧第一帧即出；>0=延后出现；<0=提前刀光。\n" +
                "负值是出场瞬间把特效粒子时钟快进 |此值| 秒：弧光在伤害帧开始时已生长到对应进度——" +
                "只提前刀光本身，伤害帧、动作、判定都不动。\n" +
                "现在弧光生长已铺满整个伤害窗口（粒子时间轴重建），感觉\"慢半拍\"就往负调一点，让它压着刀尖走。"),
            preset.spawnDelay, -0.3f, 0.3f);
        preset.duration = EditorGUILayout.Slider(
            new GUIContent("存续时间（秒）", "刀光的存在时长（动画时间）：出现后存活这么久即消失。\n手动刀气/循环播放里 = 每轮的存在时长；同时决定跟随环覆盖最近多少秒的刀尖轨迹。\n伤害帧实测：A1≈0.09 A2≈0.19 A3≈0.15/0.18 A4≈0.25"),
            preset.duration, 0.02f, 1.5f);

        EditorGUILayout.LabelField("位置 / 角度 / 大小", EditorStyles.boldLabel);
        preset.positionOffset = EditorGUILayout.Vector3Field(
            new GUIContent("局部偏移", "玩家坐标系下平移整个环（跟随已对齐，一般保持 0；0.05 以内微调）"),
            preset.positionOffset);
        preset.rotationOffset = EditorGUILayout.Vector3Field(
            new GUIContent("角度微调", "欧拉角，叠加在实时跟随结果上（在环自身坐标系：Y=沿环滚转）"),
            preset.rotationOffset);
        preset.phaseDeg = EditorGUILayout.Slider(
            new GUIContent("相位（度）", "弧光沿轨迹圆滑动：0=弧起点对准轨迹起点。弧整体偏前/偏后调它（±15 起步）"),
            preset.phaseDeg, -180f, 180f);
        preset.scale = EditorGUILayout.Vector3Field(
            new GUIContent("半径乘数", "X 为主乘数：1=刀尖精确落在弧光外缘（自动定标）"),
            preset.scale);
        preset.arcFrac = EditorGUILayout.Slider(
            new GUIContent("弧长比例（0=自动）", "伤害帧结束时，弧画到全弧张角的这个比例。\n" +
                "0=自动＝烘焙扫程角÷资产全弧角（组件 fullArcDeg，Paint1 遮罩实测 340°）——弧尾应恰好压在刀尖上。\n" +
                "资产全弧(~340°)远大于挥刀扫程(A1≈131°)，不裁切弧尾就会冲过刀尖绕到背后。\n" +
                "观感微调：弧尾超出刀尖→调小；结束时弧还没够到刀尖→调大。"),
            preset.arcFrac, 0f, 1f);
        preset.smokeBoost = EditorGUILayout.Slider(
            new GUIContent("烟气浓度增幅", "弧类粒子亮度渐变 alpha 整体乘此值（1=原样，>1=更浓，饱和截1）。只改透明度不改几何"),
            preset.smokeBoost, 1f, 2f);
        preset.revealSeedFrac = EditorGUILayout.Slider(
            new GUIContent("揭示保底(0=纯刀尖)", "出场首帧即有 扫程×此比例 的烟；反摆熄灭后已扫区域留同量残烟到窗口释放（烟气留存）"),
            preset.revealSeedFrac, 0f, 1f);

        preset.flipSweep = EditorGUILayout.Toggle(
            new GUIContent("翻转扫掠", "false=弧光从伤害帧起点沿挥刀方向生长（烘焙模板实测）；true=从轨迹末端反向亮起"),
            preset.flipSweep);

        EditorGUILayout.LabelField("总开关", EditorStyles.boldLabel);
        target.autoSlashEnabled = EditorGUILayout.ToggleLeft(
            new GUIContent("攻击时自动生成刀光", "开启后攻击时才生成跟随轨迹的真实刀光。\n" +
                "关闭（默认）= 播放任何攻击动画都零刀光——即使槽位已绑定 prefab（绑定只服务手动刀气/检视预览）。"),
            target.autoSlashEnabled);

        EditorGUILayout.LabelField("跟随手感（全局，影响所有槽位）", EditorStyles.boldLabel);
        target.centerFollowRate = EditorGUILayout.Slider(
            new GUIContent("环心/半径速率", "越大越紧跟刀尖、越小越平滑。抖动→降到 20；脱节→升到 50"),
            target.centerFollowRate, 5f, 80f);
        target.planeFollowRate = EditorGUILayout.Slider(
            new GUIContent("环面速率", "挥刀平面收敛速度，慢一点防翻滚抖动"),
            target.planeFollowRate, 2f, 40f);
        target.maxTrackRadius = EditorGUILayout.Slider(
            new GUIContent("退化半径上限", "拟合半径超过它视为直线段，冻结位姿"),
            target.maxTrackRadius, 2f, 20f);

        var pa = target.GetComponent<PlayerAttack>();
        if (pa != null)
        {
            EditorGUILayout.LabelField("测试", EditorStyles.boldLabel);
            pa.attackSlowMo = EditorGUILayout.Slider(
                new GUIContent("攻击慢动作", "1=正常；0.3=攻击与刀光同倍放慢（只影响攻击期间）。检视录制沿用进入前的这个速度"),
                pa.attackSlowMo, 0.05f, 1f);
        }

        DrawManualSection(playing, preset);
        DrawInspectSection(playing, preset, pa);

        if (GUI.changed)
        {
            EditorUtility.SetDirty(target);
            if (pa != null) EditorUtility.SetDirty(pa);
            // 检视中：滑条改动立刻重建定格位姿（延迟/存续改变同时刷新虚拟时钟上限）
            if (playing && target.inspectReady) target.RefreshInspect();
        }

        EditorGUILayout.Space(8);
        if (!string.IsNullOrEmpty(notice))
        {
            EditorGUILayout.HelpBox(notice, MessageType.Info);
            if (GUILayout.Button("清除提示")) notice = "";
        }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("保存参数到文件", "把当前所有槽位+全局参数写进 SlashTuning.json（Play 中/编辑期都可点）。Play 中保存的就是刚调的实时值。"), GUILayout.Height(28)))
            SaveToFile();
        if (GUILayout.Button(new GUIContent("从文件导入", "读取 SlashTuning.json 并写回当前 Player（退出 Play 后点它即可拿回刚才调的参数）。"), GUILayout.Height(28)))
            ImportFromFile();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = !playing;
        if (GUILayout.Button("写入场景并保存", GUILayout.Height(26)))
        {
            EditorUtility.SetDirty(target);
            if (pa != null) EditorUtility.SetDirty(pa);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }
        GUI.enabled = true;
        if (GUILayout.Button("选中 Player", GUILayout.Height(26)))
            Selection.activeGameObject = target.gameObject;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField("参数文件：" + TuneFilePath, EditorStyles.miniLabel);
        if (playing)
            EditorGUILayout.HelpBox("Play 中“写入场景并保存”不可用：先“保存参数到文件”，停止 Play 后“从文件导入”→再“写入场景并保存”。", MessageType.Warning);

        EditorGUILayout.EndScrollView();
    }

    // ———————————— 手动刀气（循环预览，不依赖录制） ————————————

    void DrawManualSection(bool playing, SwordSlashController.SlashPreset preset)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("手动刀气（在玩家处生成，循环播放）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "对当前槽位点“添加刀气”：在玩家刀尖处生成一把刀光循环播放，用上方“位置/角度/大小/相位”滑条实时调整（每帧生效）。\n" +
            "循环由虚拟时钟驱动：每轮 = 延迟出现 + 存续时间。“存续时间”即刀光每轮的存在时长，“延迟出现”=每轮起手后的隐身段。\n" +
            "不需要进入检视、不会自动挥刀。检视中不需要它：检视区自带“循环播放刀光＋播放速度”，调的就是检视里唯一那把预览刀光。", MessageType.Info);
        if (!playing)
        {
            EditorGUILayout.LabelField("（进入 Play 模式后可用）");
            return;
        }
        if (target.inspectReady || target.inspectRecording || target.InspectArmed)
        {
            EditorGUILayout.HelpBox("检视进行中：全场只有检视预览这一把刀光，调试请用下方检视区的“循环播放刀光＋播放速度”。退出检视后此处才可用。", MessageType.Info);
            return;
        }
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = preset != null && preset.prefab != null;
        if (GUILayout.Button("添加 / 重置刀气", GUILayout.Height(26)))
            target.AddManualSlash(slotKey);
        GUI.enabled = target.manualActive;
        if (GUILayout.Button("移除刀气", GUILayout.Height(26)))
            target.RemoveManualSlash();
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField(target.manualActive
            ? "● 刀气播放中（改滑条即时看效果）"
            : "○ 未生成", EditorStyles.miniLabel);
        if (target.manualActive)
        {
            target.manualSpeed = EditorGUILayout.Slider(
                new GUIContent("播放速度", "手动刀气循环的时钟倍速：1=原速，调小慢放观察每一帧的形态。\n注意上方“存续时间/延迟出现”滑条会直接改变每轮的存在时长与出场时机。"),
                target.manualSpeed, 0.05f, 3f);
        }
        if (preset != null && preset.prefab == null)
            EditorGUILayout.HelpBox("当前槽位没有刀光预制体，无法添加刀气。", MessageType.Warning);
    }

    // ———————————— 检视模式 ————————————

    // 连续帧步拖动条状态：按下点为原点，拖动像素距离换算成帧数增量
    bool frameDragActive;
    float frameDragOriginX;
    int frameDragApplied;
    const float FrameDragPx = 8f; // 每 8 像素 = 1 帧

    void DrawInspectSection(bool playing, SwordSlashController.SlashPreset preset, PlayerAttack pa)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("检视模式（Play 中：定格/步进/循环）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "进入后自动慢速挥一刀并录制刀尖轨迹 → 动画定格 → 预览刀光由虚拟时钟 vt（0=伤害帧起点）精确重建。\n" +
            "跳「伤害帧起点/结束」以动作为基准：姿势跳到该时机，刀光是否一起跳由“刀光随帧播放”决定；「定格最亮刀光」以刀光为基准：只把刀光定到最大最亮时刻，姿势不动。\n" +
            "检视期间改滑条 → 预览立刻更新；帧步/跳帧/定格都会自动停掉循环，刀光不会自行变化。按「伤害帧起点/结束」会在刀尖起点/终点位置放绿/红校准标记球。退出检视后正常 Play 不受影响。\n" +
            "单刀光保证：进入检视会自动移除手动刀气并清掉所有存活刀光，全程只有这一把预览刀光——所有滑条修改都以它为基准；\n" +
            "想循环观察：勾下方“循环播放刀光”＋调“播放速度”，就是检视版的刀气调试，不需要手动刀气。", MessageType.Info);

        if (!playing)
        {
            EditorGUILayout.LabelField("（进入 Play 模式后可用）");
            return;
        }

        if (target.inspectReady)
        {
            float delay = target.SecOf(preset.spawnDelay);
            float dur = target.SecOf(preset.duration);
            float vt = target.inspectVt;

            EditorGUILayout.LabelField("vt = " + vt.ToString("F3") + " s   |   出现 " + delay.ToString("F3") +
                " s   消失 " + (delay + dur).ToString("F3") + " s   样本 " + target.InspectSampleCount);
            vt = GUILayout.HorizontalSlider(vt, 0f, target.InspectVtMax);

            // 伤害帧起点/结束：以动作为基准 —— 姿势跳到该时机，刀光是否跟由“刀光随帧播放”决定；
            // 定格最亮刀光：以刀光为基准 —— 只把刀光定到窗口中点（最大最亮），不动姿势。三者都会自动停循环。
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("伤害帧起点", "玩家姿势跳到伤害帧起点时刻。\n勾选下方“刀光随帧播放”时刀光一起跳过来；未勾选则刀光冻结、只换姿势。"), GUILayout.Height(24)))
                target.InspectJumpVt(0f);
            if (GUILayout.Button(new GUIContent("伤害帧结束", "玩家姿势跳到伤害帧窗口结束时刻（延迟+存续）。动作基准同上。"), GUILayout.Height(24)))
                target.InspectJumpVt(delay + dur);
            if (GUILayout.Button(new GUIContent("定格最亮刀光", "只定刀光：vt 跳到窗口中点（弧最大、最亮的时刻），玩家姿势不动、与随帧开关无关。\n调试刀光外观就用它。"), GUILayout.Height(24)))
                target.InspectFreezeSlash(delay + dur * 0.5f);
            EditorGUILayout.EndHorizontal();
            // 跳帧按钮可能已在本帧内改写了 vt：同步本地值，防止下方写回旧值把跳帧覆盖掉
            vt = target.inspectVt;

            // 帧步进：驱动玩家动画姿势 scrub；刀光是否跟随由右侧开关决定（对上方三个跳帧按钮同样生效）
            target.inspectScrubSlashFollows = EditorGUILayout.ToggleLeft(
                new GUIContent("刀光随帧播放",
                    "勾选：帧步/跳帧时玩家动作与刀光一起推进（逐渐出现/消失）。\n" +
                    "不勾选：帧步与跳帧只动动作姿势、刀光冻结在当前 vt（方便对着某个姿势调位置）。"),
                target.inspectScrubSlashFollows);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("◀", "单步后退一帧（动作）。快速连续步进请用中间的拖动条。"), GUILayout.Height(24), GUILayout.Width(28)))
            { target.InspectStepFrames(-1); vt = target.inspectVt; }
            // 连续帧步拖动条：按住左右拖动按距离推进/回退动画姿势（FrameDragPx 像素 = 1 帧），随帧开关同样生效
            Rect dragRect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            GUI.Box(dragRect, GUIContent.none);
            GUI.Label(dragRect, "◀ 按住左右拖动＝连续帧步（动作）▶", EditorStyles.centeredGreyMiniLabel);
            Event ev = Event.current;
            bool onDragBar = frameDragActive || dragRect.Contains(ev.mousePosition);
            if (ev.type == EventType.MouseDown && ev.button == 0 && dragRect.Contains(ev.mousePosition))
            {
                frameDragActive = true;
                frameDragOriginX = ev.mousePosition.x;
                frameDragApplied = 0;
                ev.Use();
            }
            else if (frameDragActive && ev.type == EventType.MouseDrag && onDragBar)
            {
                int total = Mathf.RoundToInt((ev.mousePosition.x - frameDragOriginX) / FrameDragPx);
                int d = total - frameDragApplied;
                if (d != 0)
                {
                    target.InspectStepFrames(d);
                    frameDragApplied = total;
                    vt = target.inspectVt;
                    Repaint();
                }
                ev.Use();
            }
            else if (frameDragActive && ev.type == EventType.MouseUp)
            {
                frameDragActive = false;
                ev.Use();
            }
            if (GUILayout.Button(new GUIContent("▶", "单步前进一帧（动作）。快速连续步进请用中间的拖动条。"), GUILayout.Height(24), GUILayout.Width(28)))
            { target.InspectStepFrames(1); vt = target.inspectVt; }
            EditorGUILayout.EndHorizontal();

            bool loop = EditorGUILayout.Toggle(
                new GUIContent("循环播放刀光", "vt 从起点自动推进、播完短暂停顿后重来；姿势保持定格。\n检视调试就用这个：它作用的就是检视里唯一这把预览刀光，滑条改动实时生效。\n帧步/跳帧/定格按钮会自动停循环。"),
                target.inspectLoop);
            if (loop != target.inspectLoop)
            {
                target.inspectLoop = loop;
                if (loop) { vt = 0f; } // 从头开始循环
            }
            if (loop)
            {
                target.inspectLoopSpeed = EditorGUILayout.Slider(
                    new GUIContent("播放速度", "循环的时钟倍速：1=原速，调小慢放观察刀光逐渐出现/消失。\n“延迟出现/存续时间”滑条改变每轮的存在时长与出场时机。"),
                    target.inspectLoopSpeed, 0.05f, 3f);
            }

            vt = Mathf.Clamp(vt, 0f, target.InspectVtMax);
            if (loop) target.inspectVt = vt;               // 循环：LateUpdate 逐帧驱动，写 vt 即可
            else if (!Mathf.Approximately(vt, target.inspectVt)) target.SetInspectVt(vt);

            if (GUILayout.Button("退出检视", GUILayout.Height(26))) target.EndInspect();
        }
        else if (target.inspectRecording)
        {
            EditorGUILayout.LabelField("正在录制挥刀轨迹…（已录 " + target.InspectSampleCount + " 帧）");
        }
        else if (target.InspectArmed)
        {
            EditorGUILayout.LabelField("等待伤害帧…（已强制出刀；若玩家正在上一段收招，稍候或点重试）");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重试强制出刀")) RequestForceAttack(preset, pa);
            if (GUILayout.Button("取消")) target.EndInspect();
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            GUI.enabled = preset.prefab != null;
            if (GUILayout.Button("进入检视（当前槽位，自动补录一刀）", GUILayout.Height(28)))
            {
                target.slowMotion = pa != null ? pa.attackSlowMo : target.slowMotion; // 沿用面板设定的慢速
                target.BeginInspect(slotKey);
                RequestForceAttack(preset, pa);
            }
            GUI.enabled = true;
            if (preset.prefab == null)
                EditorGUILayout.HelpBox("该槽位没有刀光预制体，无法检视。", MessageType.Warning);
        }
    }

    /// <summary>下一帧强制出刀（若玩家正在收招则隔帧重试一次）；录制由 PlaySlash 伤害帧事件自动确认起点。</summary>
    void RequestForceAttack(SwordSlashController.SlashPreset preset, PlayerAttack pa)
    {
        if (pa == null) return;
        int seg = slotKey == 0 ? 1 : slotKey == 2 ? 2 : slotKey == 6 ? 4 : 3; // 槽位 → 段（A3 两刀同段）
        EditorApplication.delayCall += () =>
        {
            if (target == null || !target.InspectArmed || pa == null) return;
            if (pa.DebugForceAttack(seg)) return;
            EditorApplication.delayCall += () =>
            {
                if (target != null && pa != null && target.InspectArmed) pa.DebugForceAttack(seg);
            };
        };
    }

        // ———————————— 模板烘焙：自动逐槽补录+拟合+写回（菜单） ————————————

        [MenuItem("Tools/刀光特效/生成模板刀光（Play中自动补录5槽并拟合）")]
        static void GenerateTemplateMenu()
        {
            var w = GetWindow<SwordSlashTunerWindow>("刀光调参");
            w.minSize = new Vector2(330, 560);
            w.StartTemplateBake();
        }

        SwordSlashController bakeTarget;
        int bakeIdx = -1;
        int bakeSeg;
        double bakeDeadline, bakeRetryAt;
        readonly List<string> bakeReport = new List<string>();
        const string TemplatePrefabPath = "Assets/Artwork/Slash Effect/Hovl Studio/Sword slash VFX/Prefabs/Sword Slash 11W.prefab";
        const float TemplatePhaseDeg = 0f;  // 烘焙固定弧语义：0 = 弧光起点对准伤害帧起点刀尖，沿挥刀方向生长
        const bool TemplateFlipSweep = false; // Newell 时间序法线不做镜像（曾为 true：旧约定下扫掠反向，A1 实测"从下往上"）

        void StartTemplateBake()
        {
            if (!EditorApplication.isPlaying)
            {
                notice = "请先按菜单提示进入 Play 后再点“生成模板刀光”。";
                TryFind();
                return;
            }
            TryFind();
            if (target == null) { notice = "场景里没有 SwordSlashController。"; return; }
            bakeTarget = target;
            bakeIdx = 0;
            bakeReport.Clear();
            EditorApplication.update -= BakeStep;
            EditorApplication.update += BakeStep;
            notice = "模板烘焙开始：逐槽自动补录一刀并拟合。";
        }

        void BakeStep()
        {
            if (bakeTarget == null || bakeIdx < 0 || !EditorApplication.isPlaying) return;
            var t = bakeTarget;

            if (t.inspectReady)
            {
                string line = t.InspectTemplateFit(TemplatePhaseDeg, TemplateFlipSweep);
                bakeReport.Add(line);
                t.EndInspect();
                bakeIdx++;
                bakeDeadline = 0f;
                return;
            }
            if (t.inspectRecording || t.InspectArmed)
            {
                // 等不到伤害帧（玩家上一段收招中）：3 秒后重试强制出刀
                if (!t.inspectRecording && Time.realtimeSinceStartupAsDouble > bakeRetryAt)
                {
                    var pa2 = t.GetComponent<PlayerAttack>();
                    if (pa2 != null) pa2.DebugForceAttack(bakeSeg);
                    bakeRetryAt = Time.realtimeSinceStartupAsDouble + 2.5;
                }
                if (Time.realtimeSinceStartupAsDouble > bakeDeadline)
                {
                    bakeReport.Add("slot" + SlotKeys[bakeIdx] + " TIMEOUT，跳过");
                    t.EndInspect();
                    bakeIdx++;
                    bakeDeadline = 0f;
                }
                return;
            }

            if (bakeIdx >= SlotKeys.Length) { FinishBake(); return; }

            // 开新槽：绑模板 prefab（Play 副本上绑，停 Play 前结果已写进 preset 值）→ 武装 → 强制出刀
            int slot = SlotKeys[bakeIdx];
            var pref = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePrefabPath);
            if (t.presets[slot] == null) t.presets[slot] = new SwordSlashController.SlashPreset();
            t.presets[slot].prefab = pref;
            t.slowMotion = 0.3f; // 密集采样
            t.BeginInspect(slot);
            var pa = t.GetComponent<PlayerAttack>();
            bakeSeg = slot == 0 ? 1 : slot == 2 ? 2 : slot == 6 ? 4 : 3;
            if (pa != null) pa.DebugForceAttack(bakeSeg);
            double now = Time.realtimeSinceStartupAsDouble;
            bakeDeadline = now + 10.0;
            bakeRetryAt = now + 3.0;
            Repaint();
        }

        void FinishBake()
        {
            EditorApplication.update -= BakeStep;
            var pa = bakeTarget != null ? bakeTarget.GetComponent<PlayerAttack>() : null;
            string json = JsonUtility.ToJson(BuildSnap(bakeTarget, pa), true);
            EditorPrefs.SetString("SwordSlash.TemplateBake", json);
            EditorPrefs.SetString("SwordSlash.TemplateBake.report", string.Join("\n", bakeReport.ToArray()));
            EditorPrefs.SetInt("SwordSlash.TemplateBake.apply", 1); // 域重载后由 OnEnable 写回场景
            bakeIdx = -1;
            bakeTarget = null;
            Debug.Log("[刀光模板] 5 槽拟合完成：\n" + string.Join("\n", bakeReport.ToArray()));
            EditorApplication.isPlaying = false;
        }

        void ApplyBakedTemplate()
        {
            string json = EditorPrefs.GetString("SwordSlash.TemplateBake", "");
            string report = EditorPrefs.GetString("SwordSlash.TemplateBake.report", "");
            if (string.IsNullOrEmpty(json)) { notice = "烘焙结果缺失，无法写回。\n" + report; return; }
            TryFind();
            if (target == null) { notice = "场景找不到 SwordSlashController，无法写回烘焙结果。"; return; }
            var snap = JsonUtility.FromJson<TuneSnap>(json);
            var pref = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePrefabPath);
            foreach (var st in snap.slots)
            {
                if (st.key < 0 || st.key >= target.presets.Length) continue;
                var p = target.presets[st.key];
                if (p == null) { p = new SwordSlashController.SlashPreset(); target.presets[st.key] = p; }
                p.spawnDelay = st.spawnDelay;
                p.duration = st.duration;
                p.phaseDeg = st.phaseDeg;
                p.positionOffset = st.positionOffset;
                p.rotationOffset = st.rotationOffset;
                p.scale = st.scale;
                p.flipSweep = st.flipSweep;
                if (st.hasBakedPose) { p.hasBakedPose = true; p.bakedCenter = st.bakedCenter; p.bakedNormal = st.bakedNormal; p.bakedStart = st.bakedStart; p.bakedRadius = st.bakedRadius; p.bakedSweepDeg = st.bakedSweepDeg; }
                if (st.revealCurve != null && st.revealCurve.Length == 11) p.revealCurve = st.revealCurve;
                if (st.sweepSpanNorm > 0f) { p.sweepHeadNorm = st.sweepHeadNorm; p.sweepStartNorm = st.sweepStartNorm; p.sweepEndNorm = st.sweepEndNorm; p.sweepSpanNorm = st.sweepSpanNorm; }
                if (pref != null) p.prefab = pref;
            }
            if (snap.autoSlashMark >= 0f) target.autoSlashEnabled = snap.autoSlashMark >= 0.5f;
            EditorUtility.SetDirty(target);
            EditorSceneManager.SaveScene(target.gameObject.scene);
            File.WriteAllText(TuneFilePath, json);
            notice = "模板已写入 5 槽并保存场景+SlashTuning.json。\n拟合报告：\n" + report;
            Debug.Log("[刀光模板] 已写入 5 槽并保存场景。\n" + report);
            Repaint();
        }

        // ———————————— 参数持久化：保存到文件 / 从文件导入 ————————————

    TuneSnap BuildSnap(SwordSlashController t, PlayerAttack pa)
    {
        var list = new List<SlotTune>();
        if (t.presets != null)
            for (int i = 0; i < t.presets.Length; i++)
            {
                var p = t.presets[i];
                if (p == null) continue;
                list.Add(new SlotTune
                {
                    key = i,
                    spawnDelay = p.spawnDelay,
                    duration = p.duration,
                    phaseDeg = p.phaseDeg,
                    positionOffset = p.positionOffset,
                    rotationOffset = p.rotationOffset,
                    scale = p.scale,
                    flipSweep = p.flipSweep,
                    prefabPath = p.prefab != null ? AssetDatabase.GetAssetPath(p.prefab) : "",
                    hasBakedPose = p.hasBakedPose,
                    bakedCenter = p.bakedCenter,
                    bakedNormal = p.bakedNormal,
                    bakedStart = p.bakedStart,
                    bakedRadius = p.bakedRadius,
                    bakedSweepDeg = p.bakedSweepDeg,
                    arcFrac = p.arcFrac,
                    smokeBoost = p.smokeBoost,
                    revealSeedFrac = p.revealSeedFrac,
                    revealCurve = p.revealCurve,
                    sweepHeadNorm = p.sweepHeadNorm,
                    sweepStartNorm = p.sweepStartNorm,
                    sweepEndNorm = p.sweepEndNorm,
                    sweepSpanNorm = p.sweepSpanNorm
                });
            }
        return new TuneSnap
        {
            slots = list.ToArray(),
            centerFollowRate = t.centerFollowRate,
            planeFollowRate = t.planeFollowRate,
            maxTrackRadius = t.maxTrackRadius,
            attackSlowMo = pa != null ? pa.attackSlowMo : -1f,
            autoSlashMark = t.autoSlashEnabled ? 1f : 0f
        };
    }

    void SaveToFile()
    {
        if (target == null) TryFind();
        if (target == null) { notice = "没有 SwordSlashController，无法保存。"; return; }
        var pa = target.GetComponent<PlayerAttack>();
        try
        {
            File.WriteAllText(TuneFilePath, JsonUtility.ToJson(BuildSnap(target, pa), true));
            notice = "已保存全部参数到 " + TuneFilePath + "（Play 中保存的即当前实时值）。";
            Debug.Log("[刀光调参] 已保存 " + TuneFilePath);
        }
        catch (System.Exception e) { notice = "保存失败：" + e.Message; }
    }

    void ImportFromFile()
    {
        if (target == null) TryFind();
        if (target == null) { notice = "没有 SwordSlashController，无法导入。"; return; }
        if (!File.Exists(TuneFilePath)) { notice = "找不到文件：" + TuneFilePath; return; }
        TuneSnap snap;
        try { snap = JsonUtility.FromJson<TuneSnap>(File.ReadAllText(TuneFilePath)); }
        catch (System.Exception e) { notice = "解析失败：" + e.Message; return; }
        if (snap == null || snap.slots == null) { notice = "文件内容为空。"; return; }

        for (int s = 0; s < snap.slots.Length; s++)
        {
            SlotTune st = snap.slots[s];
            if (target.presets == null || st.key < 0 || st.key >= target.presets.Length) continue;
            var p = target.presets[st.key];
            if (p == null) { p = new SwordSlashController.SlashPreset(); target.presets[st.key] = p; }
            p.spawnDelay = st.spawnDelay;
            p.duration = st.duration;
            p.phaseDeg = st.phaseDeg;
            p.positionOffset = st.positionOffset;
            p.rotationOffset = st.rotationOffset;
            p.scale = st.scale;
            p.flipSweep = st.flipSweep;
            p.arcFrac = st.arcFrac;
            p.smokeBoost = Mathf.Max(1f, st.smokeBoost);
            p.revealSeedFrac = Mathf.Clamp01(st.revealSeedFrac);
            if (st.hasBakedPose) { p.hasBakedPose = true; p.bakedCenter = st.bakedCenter; p.bakedNormal = st.bakedNormal; p.bakedStart = st.bakedStart; p.bakedRadius = st.bakedRadius; p.bakedSweepDeg = st.bakedSweepDeg; }
            if (st.revealCurve != null && st.revealCurve.Length == 11) p.revealCurve = st.revealCurve;
            if (st.sweepSpanNorm > 0f) { p.sweepHeadNorm = st.sweepHeadNorm; p.sweepStartNorm = st.sweepStartNorm; p.sweepEndNorm = st.sweepEndNorm; p.sweepSpanNorm = st.sweepSpanNorm; }
            if (!string.IsNullOrEmpty(st.prefabPath))
            {
                var pref = AssetDatabase.LoadAssetAtPath<GameObject>(st.prefabPath);
                if (pref != null) p.prefab = pref;
            }
        }
        if (snap.centerFollowRate >= 0f) target.centerFollowRate = snap.centerFollowRate;
        if (snap.planeFollowRate >= 0f) target.planeFollowRate = snap.planeFollowRate;
        if (snap.maxTrackRadius >= 0f) target.maxTrackRadius = snap.maxTrackRadius;
        if (snap.autoSlashMark >= 0f) target.autoSlashEnabled = snap.autoSlashMark >= 0.5f; // 旧文件 -1：保持现状
        var pa = target.GetComponent<PlayerAttack>();
        if (pa != null && snap.attackSlowMo >= 0f) pa.attackSlowMo = snap.attackSlowMo;

        EditorUtility.SetDirty(target);
        if (pa != null) EditorUtility.SetDirty(pa);
        if (target.inspectReady) target.RefreshInspect();
        if (!EditorApplication.isPlaying) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        notice = "已从文件导入 " + snap.slots.Length + " 个槽位参数（Play 中导入立即生效；编辑期记得“写入场景并保存”）。";
    }
}
