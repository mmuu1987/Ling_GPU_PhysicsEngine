using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>A transient gesture, not a save-format identity. Any draft revision invalidates its exact snapshot.</summary>
    public sealed class WarSandboxDeploymentTranslation
    {
        public const float ThresholdPixels = 6;
        public bool Active { get; private set; }
        public bool Dragging { get; private set; }
        public WarSandboxDeploymentDraft Draft { get; private set; }
        public int Revision { get; private set; }
        public int Index { get; private set; }
        public WarSandboxDeploymentEntry Original { get; private set; }
        public Vector3 PreviewCenter { get; private set; }
        public Rect Map { get; private set; }
        public Vector2 World { get; private set; }
        private Vector2 startPoint, startScreen;
        public bool Begin(WarSandboxDeploymentDraft draft, int index, Vector2 world, Rect map, Vector2 point, Vector2 screen)
        {
            Cancel();
            if (draft == null || index < 0 || index >= draft.Count || !Finite(world) || world.x <= 0 || world.y <= 0 ||
                !Finite(map.position) || !Finite(map.size) || map.width <= 0 || map.height <= 0 || !Finite(point) || !Finite(screen) || !map.Contains(point)) return false;
            if (!Finite(new Vector2(draft[index].center.x,draft[index].center.z))) return false;
            Draft = draft; Revision = draft.Revision; Index = index; Original = draft[index];
            Map = map; World = world; startPoint = point; startScreen = screen; PreviewCenter = Original.center; Active = true; return true;
        }
        public bool IsCurrent(WarSandboxDeploymentDraft draft, int index, Vector2 world, Rect map) => Active &&
            ReferenceEquals(Draft, draft) && draft.Revision == Revision && index == Index && index >= 0 && index < draft.Count &&
            draft[index].Equals(Original) && world == World && map == Map;
        public bool Move(WarSandboxDeploymentDraft draft, int index, Vector2 world, Rect map, Vector2 point, Vector2 screen)
        {
            if (!IsCurrent(draft,index,world,map) || !Finite(point) || !Finite(screen) || !map.Contains(point)) { Cancel(); return false; }
            if (!Dragging && (screen-startScreen).sqrMagnitude < ThresholdPixels*ThresholdPixels) return true;
            Dragging = true;
            Vector3 a = WarSandboxMinimapProjection.MapToWorld(startPoint, World, Map);
            Vector3 b = WarSandboxMinimapProjection.MapToWorld(point, World, Map);
            PreviewCenter = Original.center + new Vector3(b.x-a.x, 0, b.z-a.z); return true;
        }
        public void Cancel() { Active = Dragging = false; Draft = null; }
        private static bool Finite(Vector2 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.y);
    }
}
