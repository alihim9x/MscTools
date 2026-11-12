using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using ElementCoordinateExtractor.Models;

namespace ElementCoordinateExtractor.Services
{
    public static class SyncLogService
    {
        public static string GetLogDirectory(Document doc)
        {
            string safeDocName = GetSafeFileName(doc.Title);

            // 1. Nếu file có đường dẫn local và ghi được
            if (!string.IsNullOrEmpty(doc.PathName))
            {
                try
                {
                    string? dir = Path.GetDirectoryName(doc.PathName);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        string targetDir = Path.Combine(dir, "SyncLogs");
                        if (!Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }
                        return targetDir;
                    }
                }
                catch
                {
                    // Fallback xuống AppData nếu thư mục dự án bị chặn quyền ghi
                }
            }

            // 2. Fallback cho Cloud Model (BIM 360 / ACC) hoặc thư mục ReadOnly
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string cloudLogDir = Path.Combine(appData, "MscTools", "SyncLogs", safeDocName);
            if (!Directory.Exists(cloudLogDir))
            {
                Directory.CreateDirectory(cloudLogDir);
            }
            return cloudLogDir;
        }

        public static string WriteSyncLog(Document doc, string syncUser, DateTime syncTime, List<SyncChangeItem> items)
        {
            try
            {
                string dir = GetLogDirectory(doc);
                string safeDocName = GetSafeFileName(doc.Title);
                string filePath = Path.Combine(dir, $"Sync_Audit_{safeDocName}.csv");

                bool fileExists = File.Exists(filePath);
                var sb = new StringBuilder();

                if (!fileExists)
                {
                    // Ghi tiêu đề cột nếu là file mới (UTF-8 BOM)
                    sb.AppendLine("Timestamp,SyncUser,ElementId,ChangeType,Category,ElementName,TypeName,LastChangedBy,Creator,Level,IsColleague");
                }

                foreach (var item in items)
                {
                    string row = $"{syncTime:yyyy-MM-dd HH:mm:ss}," +
                                 $"\"{EscapeCsv(syncUser)}\"," +
                                 $"{item.ElementId}," +
                                 $"\"{item.ChangeType}\"," +
                                 $"\"{EscapeCsv(item.CategoryName)}\"," +
                                 $"\"{EscapeCsv(item.ElementName)}\"," +
                                 $"\"{EscapeCsv(item.TypeName)}\"," +
                                 $"\"{EscapeCsv(item.LastChangedBy)}\"," +
                                 $"\"{EscapeCsv(item.Creator)}\"," +
                                 $"\"{EscapeCsv(item.LevelName)}\"," +
                                 $"{(item.IsChangedByColleague ? "Yes" : "No")}";
                    sb.AppendLine(row);
                }

                // Ghi append file chuẩn UTF-8
                if (!fileExists)
                {
                    File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
                }
                else
                {
                    File.AppendAllText(filePath, sb.ToString(), Encoding.UTF8);
                }

                return filePath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SyncLogService] Lỗi khi ghi log: {ex.Message}");
                return string.Empty;
            }
        }

        public static void OpenLogFolder(Document doc)
        {
            try
            {
                string dir = GetLogDirectory(doc);
                if (Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
                }
            }
            catch { }
        }

        private static string GetSafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "RevitModel";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        private static string EscapeCsv(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("\"", "\"\"");
        }
    }
}
