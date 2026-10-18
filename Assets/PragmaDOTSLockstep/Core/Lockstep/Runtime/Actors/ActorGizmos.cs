using UnityEngine;

namespace Pragma.Lockstep
{
    /// <summary>Drawing helpers for <see cref="ActorPart.DrawGizmos"/>.</summary>
    public static class ActorGizmos
    {
        private const int CIRCLE_SEGMENTS = 48;

        /// <summary>A circle on the XZ plane around <paramref name="center"/>.</summary>
        public static void DrawCircle(Vector3 center, float radius, Color color)
        {
            Gizmos.color = color;
            var previous = center + new Vector3(radius, 0f, 0f);
            for (var i = 1; i <= CIRCLE_SEGMENTS; i++)
            {
                var angle = i * Mathf.PI * 2f / CIRCLE_SEGMENTS;
                var next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }

        /// <summary>A rectangle on the XZ plane from <paramref name="corner"/>, <paramref name="size"/> along X and Z.</summary>
        public static void DrawRectangle(Vector3 corner, Vector2 size, Color color)
        {
            Gizmos.color = color;
            var extent = new Vector3(size.x, 0f, size.y);
            Gizmos.DrawWireCube(corner + extent / 2f, extent);
        }
    }
}
