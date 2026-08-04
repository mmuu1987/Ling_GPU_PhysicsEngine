using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassEngine.Game
{
    /// <summary>Runtime-only visual blocks for GPU static obstacles.</summary>
    [DisallowMultipleComponent]
    public sealed class WarSandboxStaticObstaclePresenter : MonoBehaviour
    {
        private readonly List<GameObject> visuals = new List<GameObject>();
        private int lastHash = int.MinValue;

        public void Sync(StaticObstacleRect[] obstacles)
        {
            int hash = ComputeHash(obstacles);
            if (hash == lastHash)
                return;
            lastHash = hash;
            ClearVisuals();

            if (obstacles == null)
                return;

            int count = Mathf.Min(obstacles.Length, StaticObstacleMath.MaxObstacleCount);
            for (int i = 0; i < count; i++)
            {
                StaticObstacleRect obstacle = obstacles[i];
                if (!obstacle.IsValid)
                    continue;

                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = "Static Obstacle " + (i + 1);
                visual.hideFlags = HideFlags.DontSave;
                visual.transform.SetParent(transform, false);
                visual.transform.position = new Vector3(obstacle.center.x, 2f, obstacle.center.y);
                visual.transform.localScale = new Vector3(obstacle.size.x, 4f, obstacle.size.y);

                Collider collider = visual.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);

                Renderer renderer = visual.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    MaterialPropertyBlock properties = new MaterialPropertyBlock();
                    properties.SetColor("_BaseColor", new Color(0.18f, 0.22f, 0.25f, 1f));
                    properties.SetColor("_Color", new Color(0.18f, 0.22f, 0.25f, 1f));
                    renderer.SetPropertyBlock(properties);
                }

                visuals.Add(visual);
            }
        }

        private void OnDestroy()
        {
            ClearVisuals();
        }

        private void ClearVisuals()
        {
            for (int i = 0; i < visuals.Count; i++)
            {
                if (visuals[i] != null)
                    Destroy(visuals[i]);
            }
            visuals.Clear();
        }

        private static int ComputeHash(StaticObstacleRect[] obstacles)
        {
            if (obstacles == null)
                return 0;

            unchecked
            {
                int count = Mathf.Min(obstacles.Length, StaticObstacleMath.MaxObstacleCount);
                int hash = count;
                for (int i = 0; i < count; i++)
                {
                    hash = hash * 31 + obstacles[i].center.GetHashCode();
                    hash = hash * 31 + obstacles[i].size.GetHashCode();
                }
                return hash;
            }
        }
    }
}
