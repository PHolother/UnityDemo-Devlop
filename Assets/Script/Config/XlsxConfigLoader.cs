using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml;
using UnityEngine;

namespace Script.Config
{
    /// <summary>
    /// 通用 xlsx 配置读取器（自研 zip+XML 解析，无第三方依赖）。
    /// 供所有配表系统复用（BOSS/玩家/其它）：
    ///   一个 xlsx 文件 = 多个 Sheet，按 Sheet 名读取二维表。
    /// 位置：项目根目录 Config/BossConfig.xlsx（与 DevLog.md 同层，归 git 管理）。
    /// 编辑器与运行时统一走 Application.dataPath 上一级定位项目根。
    /// 工作流：策划在 Excel 改 xlsx → 保存 → Unity 顶部 Window > Refresh Config XLSX 刷新。
    /// </summary>
    public static class XlsxConfigLoader
    {
        /// <summary>
        /// 读取 xlsx 指定 Sheet，返回二维表（行 x 列，string）。
        /// 找不到 Sheet 返回 null。
        /// </summary>
        public static List<string[]> ReadSheet(string fileName, string sheetName)
        {
            string path = GetConfigPath(fileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[XlsxConfigLoader] 文件不存在: {path}");
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (IOException ex)
            {
                // 文件被 Excel 等程序占用时的友好提示（不抛异常中断游戏）
                Debug.LogWarning($"[XlsxConfigLoader] 读取失败：{fileName} 可能正被 Excel 打开占用（请先关闭 Excel 再刷新）。\n{ex.Message}");
                return null;
            }

            using (var ms = new MemoryStream(bytes))
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                // 1. 读取 workbook.xml → sheet 名 → sheetN.xml 映射
                var workbookEntry = zip.GetEntry("xl/workbook.xml");
                if (workbookEntry == null)
                {
                    Debug.LogWarning($"[XlsxConfigLoader] {fileName} 缺少 workbook.xml（非标准 xlsx）");
                    return null;
                }

                string sheetFile = null;
                using (var reader = XmlReader.Create(workbookEntry.Open()))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheet")
                        {
                            string name = reader.GetAttribute("name");
                            string rid = reader.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                            if (name == sheetName)
                            {
                                sheetFile = ResolveSheetFile(zip, rid, workbookEntry);
                                break;
                            }
                        }
                    }
                }

                if (sheetFile == null)
                {
                    Debug.LogWarning($"[XlsxConfigLoader] {fileName} 中找不到 Sheet: {sheetName}");
                    return null;
                }

                // 2. 读取共享字符串表
                var shared = ReadSharedStrings(zip);

                // 3. 解析 sheet 数据
                return ParseSheet(zip.GetEntry(sheetFile), shared);
            }
        }

        /// <summary>
        /// 通过 relationship id 解析实际 sheet 文件名（sheet1.xml / sheet2.xml ...）。
        /// </summary>
        private static string ResolveSheetFile(ZipArchive zip, string rid, ZipArchiveEntry workbookEntry)
        {
            // 读取 xl/_rels/workbook.xml.rels
            var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (relsEntry == null) return null;

            using (var reader = XmlReader.Create(relsEntry.Open()))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship")
                    {
                        string id = reader.GetAttribute("Id");
                        string target = reader.GetAttribute("Target");
                        if (id == rid && !string.IsNullOrEmpty(target))
                        {
                            // Target 格式不统一：
                            //   openpyxl/Excel: "/xl/worksheets/sheet1.xml"（带 xl/ 前缀）
                            //   WPS:           "worksheets/sheet1.xml"（相对路径，需补 xl/）
                            string t = target.Replace('\\', '/').TrimStart('/');
                            if (!t.StartsWith("xl/")) t = "xl/" + t;
                            return t;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 读取共享字符串表（sharedStrings.xml），返回索引 → 字符串。
        /// 直接遍历 <si> 收集其 <t> 文本（兼容富文本多 <t> 拼接）。
        /// </summary>
        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var result = new List<string>();
            var entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return result;

            using (var reader = XmlReader.Create(entry.Open()))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si")
                    {
                        var sb = new System.Text.StringBuilder();
                        // 遍历该 <si> 子树内的所有 <t>（用 ReadSubtree 隔离，不破坏外层位置）
                        using (var sub = reader.ReadSubtree())
                        {
                            while (sub.Read())
                            {
                                if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "t")
                                    sb.Append(sub.ReadElementContentAsString());
                            }
                        }
                        result.Add(sb.ToString());
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 解析 sheet 的 XML：每个 <row> 一行，每个 <c r="A1" t="..."> 一格。
        /// t=inlineStr → <is><t>内联文本；t=s → 共享字符串索引（<v>）；
        /// 其它 → <v> 数字。用 r 属性定位列，空单元格填 ""。
        /// 用 ReadSubtree 隔离每个单元格，避免 XML reader 顺序错乱。
        /// </summary>
        private static List<string[]> ParseSheet(ZipArchiveEntry entry, List<string> shared)
        {
            var rows = new List<string[]>();
            if (entry == null) return rows;

            using (var reader = XmlReader.Create(entry.Open()))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row") continue;

                    // 当前行：从 row 的 r 属性取行号（1 起），按行号补齐空行
                    int rowIdx = rows.Count;
                    if (int.TryParse(reader.GetAttribute("r"), out int rn)) rowIdx = rn - 1;
                    while (rows.Count <= rowIdx) rows.Add(new string[0]);

                    var rowData = new System.Collections.Generic.Dictionary<int, string>();
                    using (var sub = reader.ReadSubtree())
                    {
                        while (sub.Read())
                        {
                            if (sub.NodeType != XmlNodeType.Element || sub.LocalName != "c") continue;
                            string refAttr = sub.GetAttribute("r");
                            string typeAttr = sub.GetAttribute("t");
                            int col = ColumnFromRef(refAttr);
                            rowData[col] = ReadCellValue(sub, typeAttr, shared);
                        }
                    }

                    // 组装行：补齐最大列
                    int maxCol = -1;
                    foreach (var k in rowData.Keys) if (k > maxCol) maxCol = k;
                    var arr = new string[maxCol + 1];
                    for (int c = 0; c <= maxCol; c++) arr[c] = rowData.TryGetValue(c, out string v) ? v : "";
                    rows[rowIdx] = arr;
                }
            }
            return rows;
        }

        /// <summary>
        /// 读取单个 <c> 单元格的值。传入的 reader 已定位在 <c> 元素，
        /// 返回后 reader 位于 </c> 之后（ReadSubtree 内安全）。
        /// </summary>
        private static string ReadCellValue(XmlReader reader, string type, List<string> shared)
        {
            // 在 <c> 子树内遍历
            using (var sub = reader.ReadSubtree())
            {
                string inline = null;
                string value = null;
                while (sub.Read())
                {
                    if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "t")
                    {
                        inline = sub.ReadElementContentAsString();
                    }
                    else if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "v")
                    {
                        value = sub.ReadElementContentAsString();
                    }
                }

                if (type == "inlineStr") return inline ?? "";
                if (type == "s")
                {
                    if (value != null && int.TryParse(value, out int idx) && idx >= 0 && idx < shared.Count)
                        return shared[idx];
                    return "";
                }
                if (type == "b") return value == "1" ? "1" : "0";
                return value ?? "";
            }
        }

        private static string ReadInlineString(XmlReader reader)
        {
            if (reader.IsEmptyElement) return "";
            var sb = new System.Text.StringBuilder();
            int depth = reader.Depth;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
                    sb.Append(reader.ReadElementContentAsString());
                else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "is")
                    break;
            }
            return sb.ToString();
        }

        private static string ResolveCellValue(string type, string value, List<string> shared)
        {
            if (value == null) return "";
            switch (type)
            {
                case "s":
                    if (int.TryParse(value, out int idx) && idx >= 0 && idx < shared.Count)
                        return shared[idx];
                    return "";
                case "inlineStr":
                    return value;
                case "b":
                    return value == "1" ? "1" : "0";
                default:
                    // 数字直接返回（保留原始字符串）
                    return value;
            }
        }

        /// <summary>
        /// 把单元格引用转列索引："A1"→0, "B2"→1, "AA1"→26。
        /// </summary>
        private static int ColumnFromRef(string cellRef)
        {
            if (string.IsNullOrEmpty(cellRef)) return 0;
            int col = 0;
            foreach (char ch in cellRef)
            {
                if (ch >= 'A' && ch <= 'Z') col = col * 26 + (ch - 'A' + 1);
                else if (ch >= 'a' && ch <= 'z') col = col * 26 + (ch - 'a' + 1);
                else break; // 遇到数字停止
            }
            return col - 1;
        }

        /// <summary>
        /// 项目根目录：Application.dataPath 的上一级（Assets/..）。
        /// </summary>
        public static string GetConfigPath(string fileName)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(root, "Config", fileName);
        }

        // ---- 便捷 API（与 CsvConfigLoader 对齐，供调用方少改代码）----

        /// <summary>
        /// 读取 Sheet 为 参数名→值 字典。
        /// 支持两种格式：
        ///   - 两列：参数名,值
        ///   - 三列：分组,参数名,值（key = 分组_参数名）
        /// 跳过 # 注释行。
        /// </summary>
        public static Dictionary<string, string> LoadKeyValue(string fileName, string sheetName)
        {
            var result = new Dictionary<string, string>();
            var rows = ReadSheet(fileName, sheetName);
            if (rows == null) return result;
            foreach (var row in rows)
            {
                if (row.Length < 2) continue;
                if (row[0].Trim().StartsWith("#")) continue;
                if (row.Length >= 3)
                {
                    // 三列：分组,参数名,值
                    string group = row[0].Trim();
                    string name = row[1].Trim();
                    string value = row[2].Trim();
                    if (group.Length == 0 || name.Length == 0) continue;
                    result[group + "_" + name] = value;
                }
                else
                {
                    string k = row[0].Trim();
                    if (k.Length == 0) continue;
                    result[k] = row[1].Trim();
                }
            }
            return result;
        }

        /// <summary>
        /// 读取 Sheet 为结构化行（含表头），跳过 # 注释行。
        /// </summary>
        public static (string[] header, List<string[]> rows) LoadRows(string fileName, string sheetName)
        {
            var all = ReadSheet(fileName, sheetName);
            if (all == null || all.Count == 0) return (null, new List<string[]>());

            string[] header = null;
            var rows = new List<string[]>();
            foreach (var row in all)
            {
                // 跳过空行
                bool empty = true;
                foreach (var c in row) if (!string.IsNullOrWhiteSpace(c)) { empty = false; break; }
                if (empty) continue;
                if (row.Length > 0 && row[0].Trim().StartsWith("#")) continue;

                if (header == null) { header = row; continue; }
                rows.Add(row);
            }
            return (header, rows);
        }

        public static int ColumnIndex(string[] header, string name)
        {
            if (header == null) return -1;
            for (int i = 0; i < header.Length; i++)
                if (string.Equals(header[i].Trim(), name, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        public static string Cell(string[] row, int col)
        {
            if (row == null || col < 0 || col >= row.Length) return "";
            return row[col].Trim();
        }
    }
}
