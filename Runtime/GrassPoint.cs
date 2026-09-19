using System;
using UnityEngine;

namespace ModernGrassTool
{
    [Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct GrassPoint
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector2 size;    // x = width, y = height
        public Color color;     // individual point tint / variation

        public const int STRIDE = 12 + 12 + 8 + 16; // 48 bytes

        public GrassPoint(Vector3 position, Vector3 normal, Vector2 size, Color color)
        {
            this.position = position;
            this.normal = normal;
            this.size = size;
            this.color = color;
        }
    }
}
