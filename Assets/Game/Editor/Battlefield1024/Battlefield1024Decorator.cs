using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Places third-party props as cheap, visual-only scenery (pattern from Art21Builder):
    /// one authored LOD kept, colliders/scripts stripped, URP/Lit instanced material copies with
    /// textures capped at 512, static batching, LODGroup screen-size culling, and a global
    /// triangle budget. Prefabs above the per-prop budget are rejected (logged) instead of failing.
    /// </summary>
    public sealed class Decorator
    {
        public readonly MapSpec Spec;
        public readonly TerrainSurface Surface;
        readonly Backdrop backdrop;
        readonly Transform root;
        readonly System.Random rng;
        readonly Dictionary<string, Transform> groups = new Dictionary<string, Transform>();
        readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();
        readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        readonly HashSet<string> rejected = new HashSet<string>();
        readonly Dictionary<string, int> used = new Dictionary<string, int>();
        readonly List<string> notes = new List<string>();
        long triangles; int props, shadowCasters, budgetSkips;
        long actualTriangles; int remapped;
        /// <summary>Optional prefab swap (procedural-props comparison map). Applied only AFTER the original prefab passed every
        /// check, so the RNG stream, rejections and the triangle budget stay identical to the unswapped map (same layout).</summary>
        public Func<string, bool, string> Remap;
        /// <summary>Early mode: Pick ignores file existence / rejections and Place swaps BEFORE loading, so the map no longer
        /// needs the third-party files (layout then differs from the unswapped map).</summary>
        public bool RemapEarly;

        public Decorator(MapSpec spec, TerrainSurface surface, Backdrop backdrop, Transform root)
        {
            Spec = spec; Surface = surface; this.backdrop = backdrop; this.root = root; rng = new System.Random(spec.Seed);
        }

        public float Rand(float a, float b) => a + (b - a) * (float)rng.NextDouble();
        public bool Chance(float p) => rng.NextDouble() < p;
        public float Height(float x, float z) => backdrop.Sample(Surface, x, z);
        public bool Inside(float x, float z) => Mathf.Abs(x) < Spec.Size.Half && Mathf.Abs(z) < Spec.Size.Half;

        /// <summary>True when the gameplay cell under (x,z) is not walkable (outside the map counts as blocked).</summary>
        public bool Blocked(float x, float z) => !Inside(x, z) || !Surface.TrySample(new Vector2(x, z), out var q) || !q.Walkable;

        /// <summary>Blocked here and within <paramref name="r"/> metres in 8 directions (trunk well inside the wood).</summary>
        public bool DeepBlocked(float x, float z, float r)
        {
            if (!Blocked(x, z)) return false;
            for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4f; if (!Blocked(x + Mathf.Cos(a) * r, z + Mathf.Sin(a) * r)) return false; }
            return true;
        }

        public Transform Group(string name)
        {
            if (groups.TryGetValue(name, out var t)) return t;
            t = new GameObject(name).transform; t.SetParent(root, false); groups[name] = t; return t;
        }

        /// <summary>Random usable prefab from a pool (null when every candidate was rejected).</summary>
        public string Pick(string[] pool, bool far)
        {
            if (Remap != null && RemapEarly) return pool.Length == 0 ? null : pool[rng.Next(pool.Length)];
            var ok = pool.Where(p => !rejected.Contains(Key(p, far)) && (File.Exists(p))).ToArray();
            return ok.Length == 0 ? null : ok[rng.Next(ok.Length)];
        }

        /// <summary>Jittered-grid scatter over a rectangle. Candidates are shuffled before the cap so
        /// a max count thins the whole area evenly instead of filling it row by row.</summary>
        public int Scatter(string group, float x0, float z0, float x1, float z1, float spacing, int max,
            Func<float, float, bool> where, Func<float, float, Choice> choose)
        {
            var candidates = new List<Vector2>();
            for (float z = z0; z <= z1; z += spacing)
                for (float x = x0; x <= x1; x += spacing)
                {
                    float px = x + Rand(-.45f, .45f) * spacing, pz = z + Rand(-.45f, .45f) * spacing;
                    if (where(px, pz)) candidates.Add(new Vector2(px, pz));
                }
            for (int i = candidates.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = candidates[i]; candidates[i] = candidates[j]; candidates[j] = t; }
            int placed = 0; var g = Group(group);
            foreach (var p in candidates)
            {
                if (placed >= max) break;
                var c = choose(p.x, p.y);
                if (c.Path == null) continue;
                if (Place(c.Path, p.x, p.y, c.Height, g, c.Far, c.Shadows, c.Sink) != null) placed++;
            }
            if (candidates.Count > max) notes.Add(group + "：候选 " + candidates.Count + "，按上限放置 " + placed);
            return placed;
        }

        public struct Choice
        {
            public string Path; public float Height; public bool Far, Shadows; public float Sink;
            public Choice(string path, float height, bool far, bool shadows, float sink = .25f) { Path = path; Height = height; Far = far; Shadows = shadows; Sink = sink; }
        }

        static string Key(string path, bool far) => path + (far ? "#far" : "#near");

        public GameObject Place(string path, float x, float z, float height, Transform parent, bool far, bool shadows, float sink = .25f)
        {
            if (path != null && RemapEarly && Remap != null) { string early = Remap(path, far); if (early != null) { path = early; remapped++; } }
            if (path == null || rejected.Contains(Key(path, far))) return null;
            if (triangles > Battlefield1024Generator.DecorationTriangleBudget) { budgetSkips++; return null; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Reject(path, far, "缺少预制体"); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (PrefabUtility.IsPartOfPrefabInstance(go)) PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = prefab.name;
            go.transform.rotation = Quaternion.Euler(0, Rand(0, 360), 0);
            bool mountain = path.Contains("Mountain");
            KeepOneLod(go, far);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) Object.DestroyImmediate(mb);
            foreach (var skinned in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) RemoveRenderer(go, skinned);
            var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) { Object.DestroyImmediate(go); Reject(path, far, "没有 MeshRenderer"); return null; }
            long tri = go.GetComponentsInChildren<MeshFilter>(true).Sum(f => Scene18AssetAudit.Triangles(f.sharedMesh));
            long limit = mountain ? 4000 : far ? 900 : 1600;
            if (tri > limit) { Object.DestroyImmediate(go); Reject(path, far, "三角形 " + tri + " > " + limit); return null; }
            long placedTri = tri;
            string swap = RemapEarly ? null : Remap?.Invoke(path, far);
            var swapPrefab = swap != null ? AssetDatabase.LoadAssetAtPath<GameObject>(swap) : null;
            if (swapPrefab != null)
            {
                var rotation = go.transform.rotation;
                Object.DestroyImmediate(go);
                go = (GameObject)PrefabUtility.InstantiatePrefab(swapPrefab, parent);
                if (PrefabUtility.IsPartOfPrefabInstance(go)) PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.name = swapPrefab.name; go.transform.rotation = rotation;
                KeepOneLod(go, far);
                renderers = go.GetComponentsInChildren<MeshRenderer>(true);
                placedTri = go.GetComponentsInChildren<MeshFilter>(true).Sum(f => Scene18AssetAudit.Triangles(f.sharedMesh));
                path = swap; remapped++;
            }
            foreach (var r in renderers) { r.enabled = true; r.gameObject.SetActive(true); }
            Bounds b = BoundsOf(renderers);
            go.transform.localScale *= height / Mathf.Max(.01f, b.size.y);
            b = BoundsOf(renderers);
            float y = Height(x, z) - sink - (mountain ? height * .2f : 0f);
            go.transform.position += new Vector3(x, y, z) - new Vector3(b.center.x, b.min.y, b.center.z);
            bool castShadow = shadows && height > 5f;
            foreach (var r in renderers)
            {
                r.sharedMaterials = r.sharedMaterials.Select(Convert).ToArray();
                r.shadowCastingMode = castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveShadows = true; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
            }
            var cull = go.AddComponent<LODGroup>();
            cull.SetLODs(new[] { new LOD(height < 2.5f ? .012f : height < 8f ? .006f : .0035f, renderers) });
            cull.RecalculateBounds();
            triangles += tri; actualTriangles += placedTri; props++; if (castShadow) shadowCasters++;
            used.TryGetValue(path, out int count); used[path] = count + 1;
            return go;
        }

        /// <summary>2026-10-15 procedural buildings: native scale, fixed yaw, prefab origin placed at (x, floorY, z); near LOD.
        /// Same material conversion, shadow rule, culling and triangle budget as <see cref="Place"/>; no RNG use.</summary>
        public GameObject PlaceAt(string path, float x, float z, float yawDegrees, float floorY, Transform parent, bool shadows)
        {
            if (path == null || rejected.Contains(Key(path, false))) return null;
            if (triangles > Battlefield1024Generator.DecorationTriangleBudget) { budgetSkips++; return null; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Reject(path, false, "缺少预制体"); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (PrefabUtility.IsPartOfPrefabInstance(go)) PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = prefab.name;
            // Detailed buildings (2026-10-15): LOD_HD close up, LOD_Near (old mesh) beyond ~100 m; LOD_Far dropped.
            Renderer[] hd = null;
            var hdT = go.transform.Find("LOD_HD");
            if (hdT != null && hdT.GetComponent<MeshRenderer>() != null)
            {
                foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lg);
                var farT = go.transform.Find("LOD_Far"); if (farT != null) Object.DestroyImmediate(farT.gameObject);
                hd = new Renderer[] { hdT.GetComponent<MeshRenderer>() };
            }
            else KeepOneLod(go, false);
            var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            var mid = renderers.Where(r => hd == null || !hd.Contains(r)).ToArray();
            if (mid.Length == 0) { Object.DestroyImmediate(go); Reject(path, false, "没有 MeshRenderer"); return null; }
            long tri = mid.Select(r => r.GetComponent<MeshFilter>()).Where(f => f != null).Sum(f => Scene18AssetAudit.Triangles(f.sharedMesh));
            if (tri > 1600) { Object.DestroyImmediate(go); Reject(path, false, "三角形 " + tri + " > 1600"); return null; }
            go.transform.SetPositionAndRotation(new Vector3(x, floorY, z), Quaternion.Euler(0f, yawDegrees, 0f));
            foreach (var r in renderers) { r.enabled = true; r.gameObject.SetActive(true); }
            var bb = BoundsOf(mid);
            float height = bb.max.y - floorY;
            bool castShadow = shadows && height > 5f;
            foreach (var r in renderers)
            {
                r.sharedMaterials = r.sharedMaterials.Select(Convert).ToArray();
                r.shadowCastingMode = castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveShadows = true; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
            }
            var cull = go.AddComponent<LODGroup>();
            float cullSize = height < 2.5f ? .012f : height < 8f ? .006f : .0035f;
            if (hd != null)
            {
                float size = Mathf.Max(bb.size.x, Mathf.Max(bb.size.y, bb.size.z));
                float hdSize = Mathf.Max(cullSize * 3f, size / (2f * HdSwitchDistance * Mathf.Tan(30f * Mathf.Deg2Rad)));
                cull.SetLODs(new[] { new LOD(hdSize, hd), new LOD(cullSize, mid) });
                hdTriangles += hd.Select(r => r.GetComponent<MeshFilter>()).Where(f => f != null).Sum(f => Scene18AssetAudit.Triangles(f.sharedMesh));
            }
            else cull.SetLODs(new[] { new LOD(cullSize, mid) });
            cull.RecalculateBounds();
            triangles += tri; actualTriangles += tri; props++; if (castShadow) shadowCasters++;
            used.TryGetValue(path, out int count); used[path] = count + 1;
            return go;
        }

        /// <summary>Distance (m, 60° vertical FOV) where detailed buildings switch to their mid mesh.</summary>
        public const float HdSwitchDistance = 110f;
        /// <summary>Triangles of the detailed building meshes (only drawn close up; not part of the decoration budget).</summary>
        public long hdTriangles;

        /// <summary>Keep one authored LOD (1 near, 2 far), never several LOD levels at once.</summary>
        static void KeepOneLod(GameObject go, bool far)
        {
            foreach (var lodGroup in go.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = lodGroup.GetLODs();
                int level = Math.Min(far ? 2 : 1, lods.Length - 1);
                if (level >= 0)
                {
                    var keep = new HashSet<Renderer>(lods[level].renderers.Where(r => r != null));
                    foreach (var rr in lods.SelectMany(l => l.renderers).Where(r => r != null).Distinct().ToArray())
                        if (!keep.Contains(rr)) RemoveRenderer(go, rr);
                }
                Object.DestroyImmediate(lodGroup);
            }
        }

        /// <summary>Drop a renderer; never destroys the placed root itself.</summary>
        static void RemoveRenderer(GameObject root, Renderer r)
        {
            if (r == null) return;
            if (r.gameObject != root && r.transform.childCount == 0) { Object.DestroyImmediate(r.gameObject); return; }
            var filter = r.GetComponent<MeshFilter>();
            Object.DestroyImmediate(r);
            if (filter != null) Object.DestroyImmediate(filter);
        }

        void Reject(string path, bool far, string why)
        {
            if (rejected.Add(Key(path, far))) notes.Add(Path.GetFileNameWithoutExtension(path) + (far ? "(远)" : "") + "：" + why);
        }

        static Bounds BoundsOf(Renderer[] rs) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }

        static Texture FirstAlbedo(Material m)
        {
            var names = m.GetTexturePropertyNames();
            string[] hints = { "albedo", "base", "main", "diffuse", "color", "tex" };
            foreach (var hint in hints)
                foreach (var n in names)
                    if (n.ToLowerInvariant().Contains(hint) && !n.ToLowerInvariant().Contains("normal") && !n.ToLowerInvariant().Contains("mask"))
                    { var t = m.GetTexture(n); if (t != null) return t; }
            return null;
        }

        Material Convert(Material original)
        {
            if (original == null) return null;
            if (materials.TryGetValue(original, out var result)) return result;
            result = new Material(Battlefield1024PropHaze.FindShader()) { name = "P " + original.name, enableInstancing = true };
            Battlefield1024PropHaze.SetHaze(result, Spec.Look.Haze, Spec.Look.HazeStart, Spec.Look.HazeEnd); // same haze as the ground
            Texture t = original.HasProperty("_BaseMap") ? original.GetTexture("_BaseMap") : null;
            if (t == null && original.HasProperty("_MainTex")) t = original.GetTexture("_MainTex");
            if (t == null) t = FirstAlbedo(original);   // custom shaders (e.g. Polytope PT_Rock_Shader) have no _MainTex
            result.SetTexture("_BaseMap", Capped(t));
            Color c = original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor") : original.HasProperty("_Color") ? original.GetColor("_Color") : Color.white;
            result.SetColor("_BaseColor", c);
            if (original.HasProperty("_VertexColor") && result.HasProperty("_VertexColor")) result.SetFloat("_VertexColor", original.GetFloat("_VertexColor"));
            if (result.HasProperty("_Smoothness")) result.SetFloat("_Smoothness", .06f);
            if (result.HasProperty("_Metallic")) result.SetFloat("_Metallic", 0);
            string lower = original.name.ToLowerInvariant();
            bool leaves = lower.Contains("leave") || lower.Contains("leaf") || lower.Contains("foliage") || lower.Contains("grass") || original.shader.name.Contains("Foliage");
            bool clip = leaves || (original.HasProperty("_AlphaClip") && original.GetFloat("_AlphaClip") > .5f) || original.IsKeywordEnabled("_ALPHATEST_ON");
            if (clip && t != null)
            {
                result.SetFloat("_AlphaClip", 1); result.SetFloat("_Cutoff", .45f); result.EnableKeyword("_ALPHATEST_ON");
                result.SetOverrideTag("RenderType", "TransparentCutout"); result.renderQueue = 2450; result.SetFloat("_Cull", 0);
            }
            AssetDatabase.CreateAsset(result, Spec.Dir + "/Materials/P" + materials.Count + ".mat");
            materials[original] = result;
            return result;
        }

        Texture2D Capped(Texture t)
        {
            if (t == null) return null;
            string path = AssetDatabase.GetAssetPath(t);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) return t as Texture2D;
            if (textures.TryGetValue(path, out var known)) return known;
            Battlefield1024Generator.EnsureFolder(Spec.DerivedDir + "/Props");
            string dest = Spec.DerivedDir + "/Props/T" + textures.Count + Path.GetExtension(path);
            if (!AssetDatabase.CopyAsset(path, dest)) return t as Texture2D;
            var importer = (TextureImporter)AssetImporter.GetAtPath(dest);
            importer.maxTextureSize = 512; importer.mipmapEnabled = true; importer.isReadable = false; importer.anisoLevel = 2;
            importer.textureCompression = TextureImporterCompression.Compressed; importer.crunchedCompression = false;
            var platform = importer.GetPlatformTextureSettings("Standalone");
            platform.overridden = true; platform.maxTextureSize = 512; platform.format = TextureImporterFormat.Automatic;
            platform.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(platform); importer.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(dest);
            textures[path] = tex;
            return tex;
        }

        public void Finish(Battlefield1024Generator.Report report)
        {
            foreach (var g in groups.Values.ToArray()) if (g.childCount == 0) Object.DestroyImmediate(g.gameObject);
            Battlefield1024Generator.Require(root.GetComponentsInChildren<Collider>(true).Length == 0, "景物里残留碰撞体");
            report.props = props; report.decorationTriangles = triangles; report.shadowCasters = shadowCasters;
            report.renderers = root.GetComponentsInChildren<Renderer>(true).Length;
            report.materials = materials.Count; report.textures = textures.Count;
            report.prefabs = used.OrderByDescending(kv => kv.Value).Select(kv => Path.GetFileNameWithoutExtension(kv.Key) + " ×" + kv.Value).ToList();
            report.notes.AddRange(notes);
            if (budgetSkips > 0) report.notes.Add("三角形预算用尽，跳过 " + budgetSkips + " 个景物");
            if (Remap != null)
            {
                report.decorationTriangles = actualTriangles;
                report.notes.Add(RemapEarly ? "程序化道具独立布局：替换 " + remapped + " 次，景物 " + props + " 个（不依赖第三方道具文件）"
                                            : "程序化替换 " + remapped + " / " + props + " 个景物（布局与原图一致；三角形预算按原道具计）");
            }
        }
    }

    public sealed class GroundLook
    {
        public string GrassA, GrassB, Dirt, Rock, Forest;
        public Vector4 Tiling = new Vector4(.08f, .06f, .12f, .05f);
        public float ForestTiling = .07f;
        public Color GrassATint = Color.white, GrassBTint = Color.white, DirtTint = Color.white, RockTint = Color.white, ForestTint = Color.white;
        public Vector4 PaletteDark = new Vector4(.14f, .205f, .07f, 1), PaletteLight = new Vector4(.30f, .36f, .15f, 1);
        public float Stylize = .5f;
        public Vector4 SlopeRock = new Vector4(.16f, .38f, 0, 0);
        public Vector4 OutsideMix = new Vector4(0, 0, .45f, .6f);
        public Vector4 Haze = new Vector4(.38f, .57f, .62f, 1);
        public float HazeStart = 800f, HazeEnd = 2600f;
        public float RidgeBase = 55f, FarMountains = 240f;
    }

    /// <summary>One map = deterministic height / obstacle / splat fields + scenery rules.</summary>
    public abstract class MapSpec
    {
        public abstract string Id { get; }
        public abstract string Folder { get; }
        public abstract string DisplayName { get; }
        public abstract string Description { get; }
        public abstract string Briefing { get; }
        public abstract int Seed { get; }
        public abstract GroundLook Look { get; }
        public string TerrainId => Id + "-surface";
        public virtual int TerrainVersion => 1;
        /// <summary>2026-10-14: map size profile (default 1024 = the old constants; 2048 maps override).</summary>
        public virtual BattlefieldMapSize Size => BattlefieldMapSize.S1024;
        public string Dir => Size.Root + "/" + Folder;
        public string DerivedDir => Size.Derived + "/" + Folder;
        public string ScenePath => Dir + "/" + Folder + ".unity";
        public virtual Vector2 ControlPoint => Vector2.zero;
        public virtual float ControlPointRadius => 36f;
        public virtual Vector3 PreviewCamera => new Vector3(-560f, 430f, -760f);
        public virtual Vector3 PreviewTarget => new Vector3(0f, 0f, 60f);
        /// <summary>Points that must be reachable from the west deployment (passes, flanks).</summary>
        public virtual Vector2[] MustConnect => new Vector2[0];

        public abstract float Height(float x, float z);
        /// <summary>Authored obstacle at a cell centre (forest, rock). The edge band is added by the generator.</summary>
        public abstract bool Blocked(float x, float z);
        /// <summary>Control weights R road, G rock, B forest floor, A meadow.</summary>
        public abstract Color32 Splat(float x, float z, float height, float slope);
        public abstract void Decorate(Decorator d);

        // ---- shared field helpers
        protected float Noise(float x, float z, float frequency, float offset = 0f) =>
            Mathf.PerlinNoise(x * frequency + 1000f + Seed * .37f + offset, z * frequency + 2000f + Seed * .53f + offset * 1.7f);
        protected float Fbm(float x, float z, float frequency, int octaves)
        {
            float sum = 0, amp = 1, norm = 0;
            for (int i = 0; i < octaves; i++) { sum += Noise(x, z, frequency, i * 31.7f) * amp; norm += amp; amp *= .5f; frequency *= 2.03f; }
            return sum / norm;
        }
        protected static float Gauss(float x, float z, float cx, float cz, float sx, float sz) =>
            Mathf.Exp(-((x - cx) * (x - cx) / (2 * sx * sx) + (z - cz) * (z - cz) / (2 * sz * sz)));
        protected static float Gauss1(float d, float s) => Mathf.Exp(-d * d / (2 * s * s));
        protected static float Smooth(float a, float b, float v) { float t = Mathf.Clamp01((v - a) / (b - a)); return t * t * (3 - 2 * t); }
        /// <summary>1 inside the open battle core, fading to 0 by 60 m outside it.</summary>
        protected static float Core(float x, float z) => 1f - Smooth(Battlefield1024Generator.OpenCore - 40f, Battlefield1024Generator.OpenCore + 60f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)));
        protected static float EdgeDistance(float x, float z) => Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
        /// <summary>Distance to the curve z = f(x), first-order corrected for the curve slope.</summary>
        protected static float DistanceToCurveZ(float x, float z, Func<float, float> f)
        {
            float y = f(x), slope = (f(x + 1f) - f(x - 1f)) * .5f;
            return Mathf.Abs(z - y) / Mathf.Sqrt(1 + slope * slope);
        }
        protected static float DistanceToCurveX(float x, float z, Func<float, float> f) => DistanceToCurveZ(z, x, f);
        protected static byte B(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
        protected static bool InArmyFootprint(float x, float z, float margin)
        {
            var c = Battlefield1024Generator.ArmyCenter; var s = Battlefield1024Generator.ArmySize * .5f;
            return Mathf.Abs(Mathf.Abs(x) - c.x) < s.x + margin && Mathf.Abs(z) < s.y + margin;
        }

        // ---- prefab pools
        protected const string PT = "Assets/ThirdParty/Polytope Studio/Lowpoly_Environments/Prefabs/";
        protected const string PP = "Assets/ThirdParty/Pure Poly/Free Low Poly Nature Pack/Prefabs/";
        protected const string SC = "Assets/ThirdParty/Supercyan Free Forest Sample/Prefabs/Mobile/";
        protected const string KN = "Assets/ThirdParty/KenneyNaturePlanB/";
        protected const string TF = "Assets/ThirdParty/TriForge Assets/Fantasy Worlds - DEMO Content/Textures/";
        protected static readonly string[] Broadleaf =
        {
            PP + "PP_Tree_02.prefab", PP + "PP_Tree_10.prefab", PT + "Trees/PT_Fruit_Tree_01_green.prefab",
            SC + "Tree/Leaf/Normal/Mobile_forestpack_tree_1_leaf_1.prefab", KN + "tree_oak.fbx", KN + "tree_default.fbx",
        };
        protected static readonly string[] Birch = { PP + "PP_Birch_Tree_05.prefab", PP + "PP_Birch_Tree_06.prefab" };
        protected static readonly string[] Conifer =
        {
            PT + "Trees/PT_Pine_Tree_03_green.prefab", SC + "Tree/Fir/Mobile_forestpack_tree_fir_tall.prefab",
            KN + "tree_pineRoundA.fbx", KN + "tree_pineRoundB.fbx",
        };
        protected static readonly string[] Rocks =
        {
            PT + "Rocks/PT_Generic_Rock_01.prefab", PT + "Rocks/PT_Menhir_Rock_02.prefab", PP + "PP_Rock_Moss_Grown_09.prefab",
            PP + "PP_Rock_Moss_Grown_11.prefab", SC + "Stone/Mobile_forestpack_stone_large_1.prefab", KN + "rock_largeA.fbx", KN + "rock_largeB.fbx", KN + "rock_tallA.fbx",
        };
        protected static readonly string[] RockPiles =
        {
            PP + "PP_Rock_Pile_Forest_Moss_05.prefab", PP + "PP_Rock_Pile_Forest_Moss_10.prefab", PT + "Rocks/PT_River_Rock_Pile_02.prefab",
            SC + "Stone/Mobile_forestpack_stone_medium_1.prefab", KN + "rock_smallA.fbx",
        };
        protected static readonly string[] Shrubs = { PT + "Shrubs/PT_Generic_Shrub_01_green.prefab", KN + "grass_leafs.fbx" };
        protected static readonly string[] Meadow =
        {
            PT + "Flowers/PT_Poppy_02.prefab", PP + "PP_Grass_11.prefab", PP + "PP_Grass_15.prefab",
            SC + "Foliage/Grass/Mobile_forestpack_foliage_grassPatch_small_1.prefab", SC + "Foliage/Grass/Mobile_forestpack_foliage_grassPatch_small_2.prefab",
            KN + "flower_yellowA.fbx", KN + "grass.fbx",
        };
        protected static readonly string[] Deadwood =
        {
            SC + "Tree/Treestump/Mobile_forestpack_tree_stump_1.prefab", PT + "Trees/PT_Pine_Tree_03_stump.prefab",
            PT + "Trees/PT_Pine_Tree_03_logs.prefab", PT + "Trees/PT_Fruit_Tree_01_logs.prefab",
        };
        protected static readonly string[] Mountains = { PP + "PP_Forest_Mountain_Moss_01.prefab", PP + "PP_Forest_Mountain_Moss_02.prefab" };

        // ---- shared scenery helpers
        /// <summary>Trees on blocked cells: edge trees (close to walkable ground) near LOD + some shadows, interior far LOD.</summary>
        protected void Woods(Decorator d, string group, float spacing, Func<float, float, bool> zone, Func<float, float, string[]> pool, float hMin, float hMax, int max)
        {
            d.Scatter(group, -510f, -510f, 510f, 510f, spacing, max,
                (x, z) => zone(x, z) && d.Blocked(x, z),
                (x, z) =>
                {
                    bool interior = d.DeepBlocked(x, z, 14f);
                    return new Decorator.Choice(d.Pick(pool(x, z), interior), d.Rand(hMin, hMax), interior, !interior && d.Chance(.55f), .35f);
                });
        }

        /// <summary>Distant trees on the backdrop apron (far LOD, no shadows), thinning with distance.</summary>
        protected void DistantTrees(Decorator d, string[] pool, float density, int max)
        {
            d.Scatter("远景树林", -1500f, -1500f, 1500f, 1500f, 26f, max,
                (x, z) =>
                {
                    float e = EdgeDistance(x, z);
                    if (e < 516f || e > 1500f) return false;
                    float keep = density * (1f - Smooth(700f, 1500f, e)) * Smooth(.35f, .6f, Noise(x, z, .006f, 9f));
                    return d.Chance(keep);
                },
                (x, z) => new Decorator.Choice(d.Pick(pool, true), d.Rand(15f, 26f), true, false, .4f));
        }

        protected void DistantMountains(Decorator d, int count)
        {
            var g = d.Group("远山");
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count + d.Rand(-.18f, .18f), r = d.Rand(1700f, 2500f);
                d.Place(d.Pick(Mountains, false), Mathf.Cos(a) * r, Mathf.Sin(a) * r, d.Rand(160f, 300f), g, false, false, 0f);
            }
        }
    }
}
