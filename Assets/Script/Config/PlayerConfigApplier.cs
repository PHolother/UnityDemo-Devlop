using System.Collections.Generic;
using System.Reflection;
using Script.Base.BattleAttribute;
using UnityEngine;

/// <summary>
/// 玩家配表应用器：启动时读取 Config/PlayerConfig.xlsx，把表里的值覆盖到玩家组件字段。
/// 数据驱动入口——战斗策划在 Excel 改表 → 保存 → 刷新配表（编辑器菜单 Window/Refresh Config XLSX）即生效。
/// 覆盖规则：表里有的参数覆盖组件字段；缺失的参数保持 Inspector 值。
/// 挂载位置：Player（与 PlayerMove/PlayerDodge/Health/Mana/Stamina 同物体）。
/// 纯反射写入（不依赖 UnityEditor），运行时/编辑器都可用。
/// </summary>
public class PlayerConfigApplier : MonoBehaviour
{
    [Header("配表")]
    [Tooltip("配置文件（Config/ 目录下，xlsx 单文件多 Sheet）")]
    [SerializeField] private string configFileName = "PlayerConfig.xlsx";
    [Tooltip("刷新时输出日志到 Console")]
    [SerializeField] private bool logReload = true;

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
    /// 重新读取 xlsx 的 PlayerBase + PlayerAttribute Sheet 并应用到组件。
    /// PlayerBase：移动/闪避等基础参数；PlayerAttribute：血量/法力/体力等可见属性。
    /// 表格式：参数名,值,说明（第三列仅供阅读，程序只读前两列；# 开头的行是注释分组）。
    /// </summary>
    public void ApplyConfig()
    {
        cfg = new Dictionary<string, string>();
        LoadKeyValueSheet("PlayerBase");
        LoadKeyValueSheet("PlayerAttribute");

        appliedCount = 0;

        // 字段名直接匹配：PlayerMove / PlayerDodge（基础参数，玩家根）、
        // Health / Mana / Stamina（属性组件在 BattleAttributes 子物体上）、
        // PlayerUIManager（UI 配置，Canvas 下独立物体）
        var playerRoot = transform.root;
        ApplyToObject(playerRoot.GetComponent<PlayerMove>());
        ApplyToObject(playerRoot.GetComponent<PlayerDodge>());
        ApplyToObject(GetComponentInChildren<Health>(true));
        ApplyToObject(GetComponentInChildren<Mana>(true));
        ApplyToObject(GetComponentInChildren<Stamina>(true));
        var uiManager = GameObject.Find("PlayerUIManager");
        if (uiManager != null)
        {
            ApplyToObject(uiManager.GetComponent<PlayerUIManager>());
        }

        if (logReload)
        {
            Debug.Log($"[PlayerConfig] 已应用 {configFileName}（PlayerBase+PlayerAttribute）：{appliedCount} 个参数（xlsx 优先 → Inspector 兜底）");
        }
    }

    /// <summary>
    /// 读取指定 Sheet 的键值对（前两列：参数名,值；跳过表头行与 # 注释），并入 cfg。
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
            if (k.Length == 0 || k.StartsWith("#") || k == "参数名") continue;
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

        // float[] 数组字段：从 cfg 按 字段基名_A1/_A2/_A3/_A4 组装（如 attackStaminaCosts ← attackStaminaCost_A1~A4）
        foreach (var f in fields)
        {
            if (f.IsStatic || f.IsInitOnly) continue;
            if (f.FieldType != typeof(float[])) continue;

            // 基名：去掉末尾 s（attackStaminaCosts → attackStaminaCost）
            string baseName = f.Name.EndsWith("s") ? f.Name.Substring(0, f.Name.Length - 1) : f.Name;
            var arr = new List<float>();
            for (int i = 1; i <= 8; i++)
            {
                string key = $"{baseName}_A{i}";
                if (!cfg.TryGetValue(key, out string v)) break;
                if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fv))
                {
                    arr.Add(fv);
                }
            }
            if (arr.Count > 0)
            {
                f.SetValue(component, arr.ToArray());
                appliedCount++;
            }
        }
    }

    /// <summary>
    /// 按字段类型把配置字符串写入字段值。支持 float/int/bool。
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
                if (int.TryParse(raw, out int iv))
                {
                    f.SetValue(component, iv);
                    return true;
                }
            }
            else if (f.FieldType == typeof(bool))
            {
                if (bool.TryParse(raw, out bool bv))
                {
                    f.SetValue(component, bv);
                    return true;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerConfig] 写入字段 {f.Name} 失败: {e.Message}");
        }
        return false;
    }
}
