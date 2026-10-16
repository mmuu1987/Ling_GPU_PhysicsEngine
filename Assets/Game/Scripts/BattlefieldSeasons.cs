using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// Seasons (2026-10-16, plan B): the same 2048 map can be shown as 绿地 / 秋天 / 雪地 / 荒漠. Render only — terrain,
    /// obstacles and combat are unchanged. The menu sets <see cref="Requested"/> before the battlefield scene loads.
    /// </summary>
    public static class BattlefieldSeasons
    {
        public const int Native = -1, Green = 0, Autumn = 1, Snow = 2, Dry = 3, Count = 4;
        public static readonly string[] Names = { "绿地", "秋天", "雪地", "荒漠" };

        /// <summary>Season for the next battlefield load; -1 = the map's own season.</summary>
        public static int Requested = Native;

        /// <summary>Season actually shown by the loaded battlefield (-1 while no switcher is present).</summary>
        public static int Current { get; internal set; } = Native;

        public static string NameOf(int season) => season >= 0 && season < Count ? Names[season] : "原样";

        /// <summary>Maps whose scene carries a <see cref="BattlefieldSeasonSwitcher"/> (installed by the editor tool) → own season.</summary>
        public static readonly Dictionary<string, int> NativeByMapId = new Dictionary<string, int>
        {
            { "rolling-hills-2048", Green }, { "ridge-pass-2048", Autumn }, { "cliff-plateau-2048", Snow }, { "canyon-mesa-2048", Dry },
        };

        public static bool Supports(string mapId, out int native) { native = Native; return mapId != null && NativeByMapId.TryGetValue(mapId, out native); }

        /// <summary>Season the next load of <paramref name="mapId"/> will show (Requested, or the map's own).</summary>
        public static int Effective(string mapId) => Supports(mapId, out int native) ? (Requested >= 0 ? Requested : native) : Native;
    }
}
