using System.Collections.Generic;
using Script.Config;
using UnityEngine;

/// <summary>
/// BOSS 招式数据表：从 Config/BossConfig.xlsx 的多个 Sheet 构建招式/序列/条件/冷却数据。
/// 表结构（xlsx 单文件多 Sheet）：
///   BossBase      基础属性：移动/阻挡/受击（键值对）
///   BossAI        出招决策（键值对）+ 权重序列槽位（表格式）
///   BossSkills    组式技能表：SkillBase 默认值 + 每技能一组参数，子攻击（首列空）归属上一技能
///   BossConditions 条件定义（选项卡）
///   BossCooldown  减冷却规则
/// 权重序列（用户设计）：固定骨架 + 权重插槽，如 1>(1/2/4)>(2/4/5)>4。
/// 减冷却：条件由假变真（边沿触发）时一次性减去数值。
/// </summary>
public class BossSkillTable
{
    /// <summary>招式定义</summary>
    public class SkillDef
    {
        public string id;          // 招式ID（队列/调试用）
        public string name;        // 显示名
        public string range;       // 可用档位：中/近/贴（可组合如"中近"）
        public float cooldown;     // 基础冷却
        public int weight;         // 权重（pick 槽用，0=不参与）
        public float maxMove;      // 位移上限（<=0 表示未配置，用 SkillBase 默认）
        public bool hasMaxMove;    // 是否显式配置了位移上限
        // 回身斩子攻击参数（仅 spike 用）
        public bool hasTurnBack;   // 是否配置了回身斩
        public float tbBehindMaxDistance;
        public float tbExtraLungeDistance;
        public float tbLungeStartNormalized;
        public float tbLungeEndNormalized;
        // defense 防御专属参数
        public int defenseTriggerHits = 3;   // 被打几次触发防御
        public float defenseResetTime = 5f;  // 惩罚重置时间
    }

    /// <summary>序列槽位</summary>
    public class SlotDef
    {
        public int index;             // 槽位序号（1 起）
        public bool isPick;           // true=pick 权重插槽 / false=fixed 固定招
        public string fixedSkill;     // fixed 时的招式ID
        public List<string> pickSkills; // pick 时的候选招式ID列表
    }

    /// <summary>条件定义（可配置选项卡内容）</summary>
    public class ConditionDef
    {
        public string id;          // 条件ID（如 playerHits）
        public string varName;     // 比较变量（playerHitStreak/bossHpRatio/playerDist）
        public string op;          // 比较方式（>= / <= / > / < / ==）
    }

    /// <summary>减冷却规则</summary>
    public class CooldownRule
    {
        public string skillId;     // 目标招式ID
        public string conditionId; // 条件ID（引用 ConditionDef）
        public float threshold;    // 条件阈值
        public float reduceAmount; // 达成时减冷却数值
    }

    public Dictionary<string, SkillDef> skills = new Dictionary<string, SkillDef>();
    public List<SlotDef> sequence = new List<SlotDef>();
    public Dictionary<string, ConditionDef> conditions = new Dictionary<string, ConditionDef>();
    public List<CooldownRule> cooldownRules = new List<CooldownRule>();

    // SkillBase 默认值（技能未配置时兜底）
    public float skillDefaultMaxMove = 4f;
    public float skillDefaultCooldown = 5f;

    // 行为开关（BossAI Sheet 决策键值区）
    public bool openingSpike;      // 开局必突刺：进战后接近到突刺范围，第一次出手必定突刺
    public string fallbackSkill = "defense"; // 空档兜底招式ID
    public float fallbackDelay = 3f;         // 空档兜底延迟（秒）

    /// <summary>条件当前值缓存（边沿触发用）</summary>
    private readonly Dictionary<CooldownRule, bool> ruleWasMet = new Dictionary<CooldownRule, bool>();

    /// <summary>读取全部 5 个 Sheet。</summary>
    public void LoadAll()
    {
        LoadSkills();
        LoadSequence();
        LoadConditions();
        LoadCooldownRules();
        LoadDecisionFlags();
        ruleWasMet.Clear();
    }

    /// <summary>
    /// 读 BossAI Sheet 的决策键值区（第一列=参数名, 第二列=值），提取行为开关。
    /// 键值区在权重序列槽位之前，首列非数字的行即键值对。
    /// </summary>
    private void LoadDecisionFlags()
    {
        openingSpike = false;
        fallbackSkill = "defense";
        fallbackDelay = 3f;
        var (header, rows) = XlsxConfigLoader.LoadRows("BossConfig.xlsx", "BossAI");
        if (header == null) return;
        foreach (var row in rows)
        {
            if (row.Length < 2) continue;
            string k = row[0].Trim();
            if (k.Length == 0 || k.StartsWith("#")) continue;
            if (int.TryParse(k, out _)) continue; // 跳过槽位行（首列数字）
            if (k == "spikeAtFirstAttack")
            {
                openingSpike = row[1].Trim() == "1" || row[1].Trim().ToLower() == "true";
            }
            else if (k == "fallbackSkill")
            {
                fallbackSkill = row[1].Trim();
            }
            else if (k == "fallbackDelay")
            {
                float.TryParse(row[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fallbackDelay);
            }
        }
    }

    /// <summary>
    /// 组式解析 BossSkills Sheet：
    ///   行 = [招式ID, 参数名, 值]；首列非空 = 新技能/分组开始；首列为空 = 上一技能的子攻击参数。
    ///   SkillBase 分组存默认值；defense 的专属参数存回 SkillDef。
    /// </summary>
    private void LoadSkills()
    {
        skills.Clear();
        var (header, rows) = XlsxConfigLoader.LoadRows("BossConfig.xlsx", "BossSkills");
        if (header == null) { Debug.LogWarning("[BossSkillTable] BossSkills Sheet 不存在或为空"); return; }
        int cId = XlsxConfigLoader.ColumnIndex(header, "招式ID");
        int cName = XlsxConfigLoader.ColumnIndex(header, "参数名");
        int cVal = XlsxConfigLoader.ColumnIndex(header, "值");

        SkillDef current = null;
        string currentId = null;

        foreach (var row in rows)
        {
            string id = XlsxConfigLoader.Cell(row, cId);
            string param = XlsxConfigLoader.Cell(row, cName);
            string val = XlsxConfigLoader.Cell(row, cVal);
            if (param.Length == 0) continue;

            // 首列非空 = 新技能/分组；同一技能的后续行（首列重复出现）复用已有实例，
            // 避免每次重建覆盖之前已写入的参数（range/cd/weight 丢失的根因）。
            if (id.Length > 0)
            {
                currentId = id;
                if (id == "SkillBase")
                {
                    current = null;
                }
                else if (skills.TryGetValue(id, out var existing))
                {
                    current = existing;
                }
                else
                {
                    current = new SkillDef { id = id, name = id };
                    skills[id] = current;
                }
            }

            // 参数写入（SkillBase 或当前技能）
            if (id == "SkillBase")
            {
                if (param == "默认位移上限") skillDefaultMaxMove = ParseFloat(val, 4f);
                else if (param == "默认冷却") skillDefaultCooldown = ParseFloat(val, 5f);
                continue;
            }

            if (current == null) continue; // SkillBase 行后无技能

            switch (param)
            {
                case "可用档位": current.range = val; break;
                case "冷却": current.cooldown = ParseFloat(val, skillDefaultCooldown); break;
                case "权重": current.weight = ParseInt(val, 10); break;
                case "位移上限":
                    if (val.Length > 0) { current.maxMove = ParseFloat(val, 4f); current.hasMaxMove = true; }
                    else current.hasMaxMove = false;
                    break;
                // 回身斩子攻击参数
                case "回身斩判定距离": current.hasTurnBack = true; current.tbBehindMaxDistance = ParseFloat(val, 4f); break;
                case "回身斩附加前冲": current.tbExtraLungeDistance = ParseFloat(val, 0.8f); break;
                case "回身斩前冲起始": current.tbLungeStartNormalized = ParseFloat(val, 0.5086f); break;
                case "回身斩前冲结束": current.tbLungeEndNormalized = ParseFloat(val, 0.8389f); break;
                // defense 专属
                case "被打次数触发防御": current.defenseTriggerHits = ParseInt(val, 3); break;
                case "惩罚重置时间": current.defenseResetTime = ParseFloat(val, 5f); break;
            }
        }

        // 未配置位移上限的技能 → 用 SkillBase 默认
        foreach (var kv in skills)
        {
            if (!kv.Value.hasMaxMove) kv.Value.maxMove = skillDefaultMaxMove;
            if (kv.Value.cooldown <= 0f) kv.Value.cooldown = skillDefaultCooldown;
        }
    }

    private void LoadSequence()
    {
        sequence.Clear();
        var (header, rows) = XlsxConfigLoader.LoadRows("BossConfig.xlsx", "BossAI");
        if (header == null) { Debug.LogWarning("[BossSkillTable] BossAI Sheet 不存在或为空"); return; }

        // 注意：BossAI Sheet 混用两种结构——决策键值对(参数名|值) + 权重序列(槽位|类型|内容)。
        // 槽位行固定：第一列=序号(数字)，第二列=类型，第三列起=内容/候选。
        // pick 槽支持两种写法：
        //   旧：第三列 "combo1|spike|jump"（| 分隔）
        //   新：第三列起多列，每列一个技能ID（最多 7 个候选，空列忽略）
        foreach (var row in rows)
        {
            if (row.Length < 3) continue;
            string idx = row[0].Trim();
            if (!int.TryParse(idx, out int slotIdx)) continue; // 跳过决策键值对行
            string type = row[1].Trim().ToLower();
            string content = row[2].Trim();
            if (content.Length == 0) continue;
            var slot = new SlotDef
            {
                index = slotIdx,
                isPick = type == "pick",
                pickSkills = new List<string>(),
            };
            if (slot.isPick)
            {
                // 收集第 3 列起的非空单元格（最多到第 9 列 = 7 个候选）
                int maxCol = System.Math.Min(row.Length, 9);
                for (int c = 2; c < maxCol; c++)
                {
                    string cell = row[c].Trim();
                    if (cell.Length == 0) continue;
                    // 兼容旧格式：单元格内 | 分隔也拆开
                    foreach (var part in cell.Split('|'))
                    {
                        string p = part.Trim();
                        if (p.Length > 0) slot.pickSkills.Add(p);
                    }
                }
            }
            else
            {
                slot.fixedSkill = content;
            }
            sequence.Add(slot);
        }
        sequence.Sort((a, b) => a.index.CompareTo(b.index));
    }

    private void LoadConditions()
    {
        conditions.Clear();
        var (header, rows) = XlsxConfigLoader.LoadRows("BossConfig.xlsx", "BossConditions");
        if (header == null) { Debug.LogWarning("[BossSkillTable] BossConditions Sheet 不存在或为空"); return; }
        int cId = XlsxConfigLoader.ColumnIndex(header, "条件ID");
        int cVar = XlsxConfigLoader.ColumnIndex(header, "比较变量");
        int cOp = XlsxConfigLoader.ColumnIndex(header, "比较方式");

        foreach (var row in rows)
        {
            string id = XlsxConfigLoader.Cell(row, cId);
            if (id.Length == 0) continue;
            conditions[id] = new ConditionDef
            {
                id = id,
                varName = XlsxConfigLoader.Cell(row, cVar),
                op = XlsxConfigLoader.Cell(row, cOp),
            };
        }
    }

    private void LoadCooldownRules()
    {
        cooldownRules.Clear();
        var (header, rows) = XlsxConfigLoader.LoadRows("BossConfig.xlsx", "BossCooldown");
        if (header == null) { Debug.LogWarning("[BossSkillTable] BossCooldown Sheet 不存在或为空"); return; }
        int cSkill = XlsxConfigLoader.ColumnIndex(header, "招式ID");
        int cCond = XlsxConfigLoader.ColumnIndex(header, "条件ID");
        int cThr = XlsxConfigLoader.ColumnIndex(header, "条件阈值");
        int cAmt = XlsxConfigLoader.ColumnIndex(header, "减冷却数值");

        foreach (var row in rows)
        {
            string skillId = XlsxConfigLoader.Cell(row, cSkill);
            string condId = XlsxConfigLoader.Cell(row, cCond);
            if (skillId.Length == 0 || condId.Length == 0) continue;
            cooldownRules.Add(new CooldownRule
            {
                skillId = skillId,
                conditionId = condId,
                threshold = ParseFloat(XlsxConfigLoader.Cell(row, cThr), 0f),
                reduceAmount = ParseFloat(XlsxConfigLoader.Cell(row, cAmt), 0f),
            });
        }
    }

    /// <summary>
    /// 检查所有减冷却规则：条件由假变真（边沿）时返回需要减冷却的 (招式ID, 减多少) 列表。
    /// </summary>
    public List<KeyValuePair<string, float>> PollCooldownTriggers(
        int playerHitStreak, float bossHpRatio, float playerDist, float playerHpRatio)
    {
        var result = new List<KeyValuePair<string, float>>();
        foreach (var rule in cooldownRules)
        {
            bool met = EvaluateRule(rule, playerHitStreak, bossHpRatio, playerDist, playerHpRatio);
            bool wasMet = ruleWasMet.TryGetValue(rule, out bool w) && w;
            ruleWasMet[rule] = met;
            if (met && !wasMet)
            {
                result.Add(new KeyValuePair<string, float>(rule.skillId, rule.reduceAmount));
            }
        }
        return result;
    }

    private bool EvaluateRule(CooldownRule rule, int playerHitStreak, float bossHpRatio, float playerDist, float playerHpRatio)
    {
        if (!conditions.TryGetValue(rule.conditionId, out var cond)) return false;
        float value = GetVarValue(cond.varName, playerHitStreak, bossHpRatio, playerDist, playerHpRatio);
        switch (cond.op)
        {
            case ">=": return value >= rule.threshold;
            case "<=": return value <= rule.threshold;
            case ">": return value > rule.threshold;
            case "<": return value < rule.threshold;
            case "==": return Mathf.Abs(value - rule.threshold) < 0.0001f;
            default: return false;
        }
    }

    private float GetVarValue(string varName, int playerHitStreak, float bossHpRatio, float playerDist, float playerHpRatio)
    {
        switch (varName)
        {
            case "playerHitStreak": return playerHitStreak;
            case "bossHpRatio": return bossHpRatio;
            case "playerHpRatio": return playerHpRatio;
            case "playerDist": return playerDist;
            default: return 0f;
        }
    }

    private static float ParseFloat(string s, float fallback)
    {
        return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : fallback;
    }

    private static int ParseInt(string s, int fallback)
    {
        return int.TryParse(s, out int i) ? i : fallback;
    }
}
