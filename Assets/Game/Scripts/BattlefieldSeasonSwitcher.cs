using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// Baked into the 2048 scenes by the editor tool 「季节切换」. On Awake it swaps the ground textures, the haze colour and
    /// the prop meshes for the requested season, then static-batches the decoration (the props are saved without the
    /// batching-static flag so their meshes can still be swapped).
    /// </summary>
    [DefaultExecutionOrder(-5000)]
    public sealed class BattlefieldSeasonSwitcher : MonoBehaviour
    {
        [Serializable]
        public sealed class SeasonLook
        {
            public Texture grassA, grassB, dirt, rock, forest;
            public Color haze = new Color(.38f, .57f, .62f, 1f);
            /// <summary>Prop mesh swaps; a null target hides the prop (e.g. flowers in snow).</summary>
            public Mesh[] from = new Mesh[0];
            public Mesh[] to = new Mesh[0];
            /// <summary>Building palette swaps (green / dry / snowy roofs): swapped in place, transform untouched.</summary>
            public Mesh[] buildingFrom = new Mesh[0];
            public Mesh[] buildingTo = new Mesh[0];
        }

        public int nativeSeason;
        public Transform decorationRoot;
        public Renderer[] groundRenderers = new Renderer[0];
        public SeasonLook[] seasons = new SeasonLook[BattlefieldSeasons.Count];

        static readonly int GrassAId = Shader.PropertyToID("_GrassA"), GrassBId = Shader.PropertyToID("_GrassB"),
            DirtId = Shader.PropertyToID("_Dirt"), RockId = Shader.PropertyToID("_Rock"), ForestId = Shader.PropertyToID("_Forest"),
            HazeId = Shader.PropertyToID("_HazeColor");

        /// <summary>Last apply summary (logs / tests).</summary>
        public string LastReport { get; private set; }

        void Awake()
        {
            int season = BattlefieldSeasons.Requested;
            if (season < 0 || season >= BattlefieldSeasons.Count || seasons == null || season >= seasons.Length || seasons[season] == null)
                season = nativeSeason;
            Apply(season);
        }

        void Apply(int season)
        {
            float t0 = Time.realtimeSinceStartup;
            int swapped = 0, hidden = 0;
            if (season != nativeSeason)
            {
                var look = seasons[season];
                foreach (var r in groundRenderers)
                {
                    if (r == null) continue;
                    var mats = r.materials;   // instances: never edit the saved assets
                    foreach (var m in mats)
                    {
                        if (m == null) continue;
                        SetTex(m, GrassAId, look.grassA); SetTex(m, GrassBId, look.grassB); SetTex(m, DirtId, look.dirt);
                        SetTex(m, RockId, look.rock); SetTex(m, ForestId, look.forest);
                        if (m.HasProperty(HazeId)) m.SetColor(HazeId, look.haze);
                    }
                    r.materials = mats;
                }
                if (decorationRoot != null) SwapProps(look, ref swapped, ref hidden);
            }
            if (decorationRoot != null) StaticBatchingUtility.Combine(decorationRoot.gameObject);
            BattlefieldSeasons.Current = season;
            LastReport = "季节 " + BattlefieldSeasons.NameOf(season) + (season == nativeSeason ? "（原样）" : "") +
                         "：换模型 " + swapped + "，隐藏 " + hidden + "，耗时 " + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("F0") + " ms";
            Debug.Log("[Seasons] " + LastReport);
        }

        static void SetTex(Material m, int id, Texture t) { if (t != null && m.HasProperty(id)) m.SetTexture(id, t); }

        void SwapProps(SeasonLook look, ref int swapped, ref int hidden)
        {
            var map = new Dictionary<Mesh, Mesh>();
            int n = Mathf.Min(look.from.Length, look.to.Length);
            for (int i = 0; i < n; i++) if (look.from[i] != null) map[look.from[i]] = look.to[i];
            var inPlace = new Dictionary<Mesh, Mesh>();
            int nb = look.buildingFrom != null && look.buildingTo != null ? Mathf.Min(look.buildingFrom.Length, look.buildingTo.Length) : 0;
            for (int i = 0; i < nb; i++) if (look.buildingFrom[i] != null && look.buildingTo[i] != null) inPlace[look.buildingFrom[i]] = look.buildingTo[i];

            var hazed = new Dictionary<Material, Material>();
            var filters = new List<MeshFilter>();
            var renderers = new List<Renderer>();
            foreach (Transform group in decorationRoot)
            {
                foreach (Transform prop in group)
                {
                    filters.Clear(); prop.GetComponentsInChildren(true, filters);
                    renderers.Clear(); prop.GetComponentsInChildren(true, renderers);
                    if (renderers.Count == 0) continue;

                    // haze follows the season (material copies, shared per original)
                    foreach (var r in renderers)
                    {
                        var shared = r.sharedMaterials; bool changed = false;
                        for (int k = 0; k < shared.Length; k++)
                        {
                            var m = shared[k];
                            if (m == null || !m.HasProperty(HazeId)) continue;
                            if (!hazed.TryGetValue(m, out var copy))
                            {
                                copy = new Material(m) { name = m.name + " " + BattlefieldSeasons.NameOf(BattlefieldSeasons.Requested) };
                                copy.SetColor(HazeId, look.haze);
                                hazed[m] = copy;
                            }
                            shared[k] = copy; changed = true;
                        }
                        if (changed) r.sharedMaterials = shared;
                    }

                    bool building = false;
                    foreach (var f in filters)
                        if (f.sharedMesh != null && inPlace.TryGetValue(f.sharedMesh, out var bt)) { f.sharedMesh = bt; building = true; }
                    if (building) { swapped++; continue; }

                    bool any = false, hide = false;
                    foreach (var f in filters)
                        if (f.sharedMesh != null && map.TryGetValue(f.sharedMesh, out var target)) { any = true; if (target == null) hide = true; }
                    if (!any) continue;
                    if (hide) { prop.gameObject.SetActive(false); hidden++; continue; }

                    Bounds before = BoundsOf(renderers);
                    foreach (var f in filters)
                        if (f.sharedMesh != null && map.TryGetValue(f.sharedMesh, out var target)) f.sharedMesh = target;
                    Bounds after = BoundsOf(renderers);
                    // same height and footprint anchor as the generator (Decorator.Place): scale to height, base on the old base
                    prop.localScale *= before.size.y / Mathf.Max(.01f, after.size.y);
                    after = BoundsOf(renderers);
                    prop.position += new Vector3(before.center.x - after.center.x, before.min.y - after.min.y, before.center.z - after.center.z);
                    var lod = prop.GetComponent<LODGroup>();
                    if (lod != null) lod.RecalculateBounds();
                    swapped++;
                }
            }
        }

        static Bounds BoundsOf(List<Renderer> rs)
        {
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Count; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }
    }
}
