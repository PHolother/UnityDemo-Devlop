using System.Collections.Generic;
using System.Reflection;
using Script.Base.Hitbox;
using UnityEngine;

/// <summary>
/// BOSS 配表应用器：启动时读取 Config/BossAI.csv，把表里的值覆盖到各组件 Inspector 参数。
/// 数据驱动入口——战斗策划在 Excel 改表 → 保存 → 刷新配表（编辑器菜单 Tools/Refresh Config CSV）即生效。
/// 覆盖规则：CSV 里有的参数覆盖组件字段；CSV 缺失的参数保持 Inspector 值。
/// 挂载位置：GreatSword Boss（与 BossAIController 同物体）。
/// 纯反射写入（不依赖 UnityEditor），运行时/编辑器都可用。
/// </summary>
public class BossConfigApplier : MonoBehaviour
{
    [Header("配表")]
    [Tooltip("配置文件（Config/ 目录下，xlsx 单文件多 Sheet）")]
    [SerializeField] private string configFileName = "BossConfig.xlsx";
    [Tooltip("刷新时输出日志到 Console")]
    [SerializeField] private bool logReload = true;

    private BossAIController ai;
    private CharacterBlocker blocker;
    private Dictionary<string, string> cfg;
    private int appliedCount;

    private void Awake()
    {
        ApplyConfig();
    }

    private void OnEnable()
    {
        Script.Config.ConfigRefreshHub.Register(this);
    }

    private void OnDisable()
    {
        Script.Config.ConfigRefreshHub.Unregister(this);
    }

    /// <summary>
    /// 重新读取 xlsx 的 BossBase + BossAI Sheet 并应用到组件（编辑器菜单 Window/Refresh Config XLSX 调用）。
    /// BossBase：移动/阻挡/受击；BossAI：出招决策（决策字段直接按字段名匹配）。
    /// </summary>
    public void ApplyConfig()
    {
        ai = GetComponent<BossAIController>();
        blocker = GetComponent<CharacterBlocker>();
        cfg = new Dictionary<string, string>();

        // 合并 BossBase + BossAI 两个 Sheet 的键值对（表头/注释自动跳过，key = 参数名）
        LoadKeyValueSheet("BossBase");
        LoadKeyValueSheet("BossAI");

        appliedCount = 0;

        // 字段名直接匹配（无前缀）：决策字段进 ai，阻挡进 blocker，受击/移动进各自组件
        ApplyToObject(ai);
        ApplyToObject(blocker);

        if (logReload)
        {
            Debug.Log($"[BossConfig] 已应用 {configFileName}（BossBase+BossAI）：{appliedCount} 个参数（xlsx 优先 → Inspector 兜底）");
        }
    }

    /// <summary>
    /// 读取指定 Sheet 的键值对（两列：参数名,值；跳过表头行与 # 注释），并入 cfg。
    /// </summary>
    private void LoadKeyValueSheet(string sheetName)
    {
        var (header, rows) = Script.Config.XlsxConfigLoader.LoadRows(configFileName, sheetName);
        if (header == null) return;
        foreach (var row in rows)
        {
            if (row.Length < 2) continue;
            string k = row[0].Trim();
            string v = row[1].Trim();
            if (k.Length == 0 || k.StartsWith("#")) continue;
            cfg[k] = v;
        }
    }

    /// <summary>
    /// 把配置字典里的参数写入目标组件的私有/公有字段（字段名直接匹配）。
    /// 纯反射 SetValue，不依赖 UnityEditor。
    /// </summary>
    private void ApplyToObject(Object component)
    {
        if (component == null) return;
        var type = component.GetType();
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        foreach (var f in fields)
        {
            if (f.IsStatic || f.IsInitOnly) continue;

            if (!cfg.TryGetValue(f.Name, out string raw)) continue;
            if (raw == null) continue;

            if (TrySetField(component, f, raw))
            {
                appliedCount++;
            }
        }
    }

    /// <summary>
    /// 按字段类型把 CSV 字符串写入字段值。支持 float/int/bool。
    /// </summary>
    private bool TrySetField(Object component, FieldInfo f, string raw)
    {
        try
        {
            if (f.FieldType == typeof(float))
            {
                if (float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fv))
                {
                    f.SetValue(component, fv);
                    return true;
                }
            }
            else if (f.FieldType == typeof(int))
            {
                if (int.TryParse(raw, out int iv)) { f.SetValue(component, iv); return true; }
            }
            else if (f.FieldType == typeof(bool))
            {
                if (bool.TryParse(raw, out bool bv)) { f.SetValue(component, bv); return true; }
                if (raw == "1") { f.SetValue(component, true); return true; }
                if (raw == "0") { f.SetValue(component, false); return true; }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[BossConfig] 写入字段 {f.Name} 失败: {e.Message}");
        }
        return false;
    }
}
