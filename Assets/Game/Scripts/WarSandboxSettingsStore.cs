using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace MassEngine.Game
{
    [Serializable]
    public struct WarSandboxAudioSettings
    {
        public int version;
        public float volume;
        public bool muted;
        public static WarSandboxAudioSettings Default => new WarSandboxAudioSettings { version = 1, volume = .65f };
        public bool IsValid => version == 1 && !float.IsNaN(volume) && !float.IsInfinity(volume) && volume >= 0 && volume <= 1;
    }

    public sealed class WarSandboxSettingsStore
    {
        public string FilePath { get; }
        public WarSandboxSettingsStore(string path) { FilePath = Path.GetFullPath(path); }

        public bool TryLoad(out WarSandboxAudioSettings settings, out string error)
        {
            settings = WarSandboxAudioSettings.Default; error = null;
            try
            {
                if (!File.Exists(FilePath)) return true;
                var loaded = JsonUtility.FromJson<WarSandboxAudioSettings>(File.ReadAllText(FilePath, Encoding.UTF8));
                if (!loaded.IsValid) { error = "设置内容无效，暂时使用默认音量。"; return false; }
                settings = loaded; return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            { error = "无法读取设置，暂时使用默认音量。"; return false; }
        }

        public bool TrySave(WarSandboxAudioSettings settings, out string error)
        {
            error = null;
            if (!settings.IsValid) { error = "音量必须在 0% 到 100% 之间。"; return false; }
            string temporary = FilePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(temporary, JsonUtility.ToJson(settings, true), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            { error = "无法保存设置，请检查磁盘空间和目录权限。"; return false; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
}
