using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModernGrassTool
{
    [Serializable]
    public class ModernGrassChunk
    {
        public Vector2Int coord;
        public Bounds bounds;
        public List<GrassPoint> points = new List<GrassPoint>();

        [NonSerialized]
        public float[] cutHeights;

        [NonSerialized]
        public bool isDirty = false;

        [NonSerialized]
        public bool cutDirty = false;

        public int PointCount => points != null ? points.Count : 0;

        public ModernGrassChunk()
        {
        }

        public ModernGrassChunk(Vector2Int coord, float chunkSize)
        {
            this.coord = coord;
            float halfSize = chunkSize * 0.5f;
            Vector3 center = new Vector3(coord.x * chunkSize + halfSize, 0f, coord.y * chunkSize + halfSize);
            this.bounds = new Bounds(center, new Vector3(chunkSize, 200f, chunkSize));
            this.points = new List<GrassPoint>();
        }

        public void RecalculateBounds(float chunkSize)
        {
            if (points == null || points.Count == 0)
            {
                float halfSize = chunkSize * 0.5f;
                Vector3 center = new Vector3(coord.x * chunkSize + halfSize, 0f, coord.y * chunkSize + halfSize);
                bounds = new Bounds(center, new Vector3(chunkSize, 200f, chunkSize));
                return;
            }

            Vector3 min = points[0].position;
            Vector3 max = points[0].position;

            for (int i = 1; i < points.Count; i++)
            {
                Vector3 p = points[i].position;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            float padding = 2f;
            bounds = new Bounds();
            bounds.SetMinMax(min - Vector3.one * padding, max + Vector3.one * padding);
        }

        public void EnsureCutHeights()
        {
            int count = PointCount;
            if (cutHeights == null || cutHeights.Length != count)
            {
                cutHeights = new float[count];
                for (int i = 0; i < count; i++) cutHeights[i] = -1f;
                cutDirty = true;
            }
        }
    }
}
