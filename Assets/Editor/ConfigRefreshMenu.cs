using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 编辑器菜单：Window/Refresh Config XLSX——刷新所有 xlsx 配表。
/// 战斗策划改完 Excel 里的 xlsx 后点此菜单，全部配表立即生效（无需进 Play）。
/// 工作方式：
///   1. 找到场景中所有 *ConfigApplier 组件，读取其 configFileName/configSheetName，
///      把 xlsx 值写回同物体上组件的序列化字段（Inspector 可见、保存后持久化）。
///   2. 找到场景中所有 BossAIController，调用 ReloadSkillTable() 重载招式/序列/条件/冷却表。
/// </summary>
public static class ConfigRefreshMenu
{
    [MenuItem("Window/Refresh Config XLSX")]
    public static void RefreshAllConfigs()
    {
        int applied = 0;
        bool reloadedTable = false;

        var all = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        // 1. 刷新所有 *ConfigApplier
        foreach (var applier in all)
        {
            if (applier == null) continue;
            if (!applier.GetType().Name.EndsWith("ConfigApplier")) continue;

            var fnField = applier.GetType().GetField("configFileName",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (fnField == null) continue;
            string fileName = (string)fnField.GetValue(applier);
            if (string.IsNullOrEmpty(fileName)) continue;

            // 合并 BossBase + BossAI 两个 Sheet 的键值对（参数名 → 值）
            var cfg = new Dictionary<string, string>();
            foreach (string sheet in new[] { "BossBase", "BossAI" })
            {
                var (h, rows) = Script.Config.XlsxConfigLoader.LoadRows(fileName, sheet);
                if (h == null) continue;
                foreach (var row in rows)
                {
                    if (row.Length < 2) continue;
                    string k = row[0].Trim();
                    if (k.Length == 0 || k.StartsWith("#")) continue;
                    cfg[k] = row[1].Trim();
                }
            }

            var targetComponents = applier.GetComponents<Component>();
            foreach (var comp in targetComponents)
            {
                if (comp == applier) continue;
                applied += ApplyToSerialized(comp, cfg);
            }
        }

        // 2. 重载所有 BossAIController 的招式表
        foreach (var mb in all)
        {
            if (mb == null) continue;
            if (mb is BossAIController ai)
            {
                ai.ReloadSkillTable();
                reloadedTable = true;
            }
        }

        if (applied > 0 || reloadedTable)
        {
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log($"[ConfigRefresh] 已刷新 {applied} 个参数 + 重载招式表={reloadedTable}（Window/Refresh Config XLSX）");
        }
        else
        {
            Debug.LogWarning("[ConfigRefresh] 没有找到任何 *ConfigApplier 组件或可写的 xlsx 参数");
        }
    }

    /// <summary>
    /// 用 SerializedObject 把 xlsx 值写进组件字段（key=分组_字段名 或 字段名 匹配），返回写入数。
    /// </summary>
    private static int ApplyToSerialized(Object component, Dictionary<string, string> cfg)
    {
        var so = new SerializedObject(component);
        int count = 0;
        var fields = component.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        foreach (var f in fields)
        {
            if (f.IsStatic || f.IsInitOnly) continue;
            var sp = so.FindProperty(f.Name);
            if (sp == null) continue;

            foreach (var kv in cfg)
            {
                if (!kv.Key.EndsWith("_" + f.Name) && kv.Key != f.Name) continue;
                string raw = kv.Value;

                bool ok = false;
                if (f.FieldType == typeof(float))
                {
                    if (float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fv))
                    { sp.floatValue = fv; ok = true; }
                }
                else if (f.FieldType == typeof(int))
                {
                    if (int.TryParse(raw, out int iv)) { sp.intValue = iv; ok = true; }
                }
                else if (f.FieldType == typeof(bool))
                {
                    if (bool.TryParse(raw, out bool bv)) { sp.boolValue = bv; ok = true; }
                    else if (raw == "1") { sp.boolValue = true; ok = true; }
                    else if (raw == "0") { sp.boolValue = false; ok = true; }
                }

                if (ok) { count++; }
                break;
            }
        }
        if (count > 0) so.ApplyModifiedPropertiesWithoutUndo();
        return count;
    }
}
