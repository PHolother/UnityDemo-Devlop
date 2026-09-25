using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Script.Config
{
    /// <summary>
    /// 通用 CSV 配置读取器（供所有配表系统复用：BOSS、玩家、其它）。
    /// CSV 位置：项目根目录 Config/ 下（与 DevLog.md 同层，归 git 管理）。
    /// 格式约定（兼容 Excel 另存为 CSV）：
    ///   - 每行：分组,参数名,值,说明（逗号分隔，最多 4 列；说明仅供阅读）
    ///   - 以 # 开头的行为注释
    ///   - 空行跳过
    ///   - 值支持 int/float/bool/string，读取时按需转换
    /// 编辑器与运行时都可用：运行时 Application.dataPath 上一级即项目根。
    /// </summary>
    public static class CsvConfigLoader
    {
        /// <summary>
        /// 读取 Config/ 下指定 CSV，返回 参数名 → 值 的字典（按行序，同名后者覆盖前者）。
        /// </summary>
        public static Dictionary<string, string> Load(string fileName)
        {
            var result = new Dictionary<string, string>();
            string path = GetConfigPath(fileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[CsvConfigLoader] 配置表不存在: {path}");
                return result;
            }

            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                // 支持分组:参数名:值 与 分组,参数名,值 两种分隔（Excel 另存为逗号，手写可用冒号）
                string[] parts = line.Split(',');
                if (parts.Length < 3) continue;

                string group = parts[0].Trim();
                string name = parts[1].Trim();
                string value = parts[2].Trim();
                if (name.Length == 0 || value.Length == 0) continue;

                // 用 "分组_参数名" 作 key，避免不同分组同名参数冲突
                result[group + "_" + name] = value;
            }

            Debug.Log($"[CsvConfigLoader] 已加载 {fileName}: {result.Count} 个参数");
            return result;
        }

        /// <summary>
        /// 从配置字典读取 float（找不到返回 fallback）。
        /// </summary>
        public static float GetFloat(Dictionary<string, string> cfg, string group, string name, float fallback)
        {
            if (cfg != null && cfg.TryGetValue(group + "_" + name, out string v))
            {
                if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
                    return f;
            }
            return fallback;
        }

        /// <summary>
        /// 从配置字典读取 int（找不到返回 fallback）。
        /// </summary>
        public static int GetInt(Dictionary<string, string> cfg, string group, string name, int fallback)
        {
            if (cfg != null && cfg.TryGetValue(group + "_" + name, out string v))
            {
                if (int.TryParse(v, out int i)) return i;
            }
            return fallback;
        }

        /// <summary>
        /// 从配置字典读取 bool（找不到返回 fallback）。
        /// </summary>
        public static bool GetBool(Dictionary<string, string> cfg, string group, string name, bool fallback)
        {
            if (cfg != null && cfg.TryGetValue(group + "_" + name, out string v))
            {
                if (bool.TryParse(v, out bool b)) return b;
                if (v == "1") return true;
                if (v == "0") return false;
            }
            return fallback;
        }

        /// <summary>
        /// 读取 Config/ 下指定 CSV 为结构化行：每行一个 string[]（逗号分隔，含表头）。
        /// 用于招式表/序列表/条件表等多列表。注释行(#)与空行跳过。
        /// 返回 (表头行, 数据行列表)；文件不存在时返回 (null, 空)。
        /// </summary>
        public static (string[] header, List<string[]> rows) LoadRows(string fileName)
        {
            string path = GetConfigPath(fileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[CsvConfigLoader] 配置表不存在: {path}");
                return (null, new List<string[]>());
            }

            var rows = new List<string[]>();
            string[] header = null;
            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                // 去掉首尾多余引号（Excel 导出含引号时）
                line = line.Trim().Trim('"');
                string[] parts = line.Split(',');
                for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim().Trim('"');

                if (header == null) { header = parts; continue; }
                rows.Add(parts);
            }
            return (header, rows);
        }

        /// <summary>
        /// 从表头定位列索引（找不到返回 -1）。
        /// </summary>
        public static int ColumnIndex(string[] header, string name)
        {
            if (header == null) return -1;
            for (int i = 0; i < header.Length; i++)
                if (string.Equals(header[i].Trim(), name, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        /// <summary>
        /// 安全取行内某列（越界返回空串）。
        /// </summary>
        public static string Cell(string[] row, int col)
        {
            if (row == null || col < 0 || col >= row.Length) return "";
            return row[col].Trim();
        }

        /// <summary>
        /// 项目根目录：Application.dataPath 的上一级（Assets/..）。
        /// 运行时与编辑器统一走这里，保证路径一致。
        /// </summary>
        public static string GetConfigPath(string fileName)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(root, "Config", fileName);
        }
    }
}
