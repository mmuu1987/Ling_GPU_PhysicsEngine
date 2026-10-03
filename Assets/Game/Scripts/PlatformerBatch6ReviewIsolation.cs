#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>Opt-in review-tool isolation only. Normal launches keep the existing player-wide store behaviour.</summary>
    public static class PlatformerBatch6ReviewIsolation
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Configure()
        {
            const string prefix = "--war-sandbox-review-global-file=";
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (!argument.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string candidate = argument.Substring(prefix.Length);
                if (!Path.IsPathRooted(candidate)) throw new InvalidOperationException("Review global stat file must be absolute.");
                candidate = Path.GetFullPath(candidate);
                string playerRoot = Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (candidate.Equals(playerRoot, StringComparison.OrdinalIgnoreCase) || candidate.StartsWith(playerRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Review global stat file must not overlap player data.");
                WarSandboxUnitStatStore.DefaultPathOverride = candidate;
                Debug.Log("PLATFORMER6_REVIEW_GLOBAL_ISOLATED");
                return;
            }
        }
    }
}
#endif
