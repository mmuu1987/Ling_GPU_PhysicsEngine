using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// Player-wide unit stat overrides (persistentDataPath/WarSandboxUnitOverrides.json).
    /// Writes go through a temporary file and an atomic replace; the previous content is kept as
    /// one-level ".bak" for "undo last change". A corrupt file is renamed to ".broken-*" and the
    /// game runs on official values. Missing file = no overrides = exactly the legacy behaviour.
    /// </summary>
    public sealed class WarSandboxUnitStatStore
    {
        public const string FileName = "WarSandboxUnitOverrides.json";
        public const int MaxFileBytes = 512 * 1024;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private WarSandboxGlobalStats cached;

        public string FilePath { get; }
        public string BackupPath => FilePath + ".bak";
        /// <summary>Last non-fatal problem (corrupt file quarantined, unknown fields skipped). Shown once by the UI.</summary>
        public string Warning { get; private set; }

        /// <summary>Test/tool hook: redirects the default store location (null = persistentDataPath). Never set by the game.</summary>
        public static string DefaultPathOverride { get; set; }

        public WarSandboxUnitStatStore() : this(DefaultPathOverride ?? Path.Combine(Application.persistentDataPath, FileName)) { }
        public WarSandboxUnitStatStore(string filePath) { FilePath = filePath; }

        public bool CanUndo => File.Exists(BackupPath);
        public void ClearWarning() => Warning = null;

        /// <summary>Cached global layer. Never throws, never null.</summary>
        public WarSandboxGlobalStats Current
        {
            get
            {
                if (cached == null) cached = Load();
                return cached;
            }
        }

        public void Invalidate() => cached = null;

        private WarSandboxGlobalStats Load()
        {
            if (TryRead(FilePath, out var stats, out string error))
            {
                if (stats.SkippedValues > 0) Warning = "全局兵种数值中有 " + stats.SkippedValues + " 项无法识别，已忽略。";
                return stats;
            }
            string broken = Quarantine();
            Warning = "全局兵种数值文件损坏（" + error + "），已按官方数值运行" + (broken != null ? "；原文件保存为 " + Path.GetFileName(broken) : "") + "。";
            return new WarSandboxGlobalStats();
        }

        /// <summary>Missing file is a successful empty read.</summary>
        public static bool TryRead(string path, out WarSandboxGlobalStats stats, out string error)
        {
            stats = null; error = null;
            try
            {
                if (!File.Exists(path)) { stats = new WarSandboxGlobalStats(); return true; }
                WarSandboxUnitOverrideFile file;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length <= 0 || stream.Length > MaxFileBytes) { error = "文件大小无效"; return false; }
                    var quotas = new XmlDictionaryReaderQuotas { MaxDepth = 16, MaxStringContentLength = MaxFileBytes,
                        MaxArrayLength = MaxFileBytes, MaxBytesPerRead = 4096, MaxNameTableCharCount = 16384 };
                    using (var reader = JsonReaderWriterFactory.CreateJsonReader(stream, Utf8, quotas, null))
                        file = (WarSandboxUnitOverrideFile)new DataContractJsonSerializer(typeof(WarSandboxUnitOverrideFile)).ReadObject(reader);
                }
                return WarSandboxGlobalStats.TryFromContract(file, out stats, out error);
            }
            catch (Exception exception) { stats = null; error = exception.Message; return false; }
        }

        private string Quarantine()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                string target = FilePath + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                for (int i = 1; File.Exists(target); i++) target = FilePath + ".broken-" + i;
                File.Move(FilePath, target); return target;
            }
            catch { return null; }
        }

        /// <summary>Replaces the whole global layer; the previous content becomes the undo backup.</summary>
        public bool TrySave(WarSandboxGlobalStats next, string summary, out string error)
        {
            error = null;
            if (next == null) { error = "全局兵种数值为空。"; return false; }
            var previous = Current;
            var stamped = next.Clone();
            stamped.LastChange = summary;
            stamped.LastChangeUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            if (!TryWriteAtomic(BackupPath, previous, out error)) { error = "无法写入备份：" + error; return false; }
            if (!TryWriteAtomic(FilePath, stamped, out error)) { error = "无法写入全局兵种数值：" + error; return false; }
            cached = stamped; Warning = null; return true;
        }

        /// <summary>Restores the content saved before the last change (one level).</summary>
        public bool TryUndo(out string error)
        {
            error = null;
            if (!CanUndo) { error = "没有可撤销的全局修改。"; return false; }
            if (!TryRead(BackupPath, out var previous, out error)) { error = "备份不可用：" + error; return false; }
            if (!TryWriteAtomic(FilePath, previous, out error)) { error = "撤销失败：" + error; return false; }
            try { File.Delete(BackupPath); } catch { }
            cached = previous; return true;
        }

        public static bool TryWriteAtomic(string path, WarSandboxGlobalStats stats, out string error)
        {
            error = null; string temp = null;
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                byte[] bytes;
                using (var json = new MemoryStream())
                { new DataContractJsonSerializer(typeof(WarSandboxUnitOverrideFile)).WriteObject(json, stats.ToContract()); bytes = json.ToArray(); }
                if (bytes.Length > MaxFileBytes) { error = "文件超过大小限制。"; return false; }
                temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                temp = null; return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
            finally { if (temp != null) try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }
    }
}
