using System.Collections.Generic;
using Unity.Physics;
using UnityEngine;

namespace Pragma.Dots.Utils
{
    /// <summary>
    /// Unity Physics collision filters built from the layer collision matrix of Project Settings > Physics, cached per
    /// layer.
    /// </summary>
    /// <remarks>
    /// Unity Physics works in floats and is not deterministic across machines: use it on the presentation side (picking,
    /// effects), never in a lockstep simulation. Compiled only when the project has <c>com.unity.physics</c>.
    /// </remarks>
    public static class CollisionMatrixToFilter
    {
        private static readonly Dictionary<int, CollisionFilter> Cache = new Dictionary<int, CollisionFilter>();

        /// <summary>The filter of <paramref name="layer"/>: it belongs to the layer and collides with what the matrix allows.</summary>
        public static CollisionFilter GetCollisionFilter(int layer, int groupIndex = 0)
        {
            if (!Cache.TryGetValue(layer, out var filter))
            {
                filter = GenerateFilter(layer);
                Cache.Add(layer, filter);
            }
            filter.GroupIndex = groupIndex;
            return filter;
        }

        public static CollisionFilter GetCollisionFilter(string layer, int groupIndex = 0)
        {
            return GetCollisionFilter(LayerMask.NameToLayer(layer), groupIndex);
        }

        /// <summary>Builds the filter of <paramref name="layer"/> from the current matrix, without the cache.</summary>
        public static CollisionFilter GenerateFilter(int layer, int groupIndex = 0)
        {
            if (layer < 0 || layer >= 32)
            {
                Debug.LogWarning($"[Lockstep] Invalid layer {layer}.");
                return new CollisionFilter();
            }

            var collidesWith = 0u;
            for (var other = 0; other < 32; other++)
            {
                if (!UnityEngine.Physics.GetIgnoreLayerCollision(layer, other))
                {
                    collidesWith |= 1u << other;
                }
            }

            return new CollisionFilter
            {
                BelongsTo = 1u << layer,
                CollidesWith = collidesWith,
                GroupIndex = groupIndex
            };
        }

        // Static state survives entering Play Mode without a domain reload, and the matrix may have changed since.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            Cache.Clear();
        }
    }
}
