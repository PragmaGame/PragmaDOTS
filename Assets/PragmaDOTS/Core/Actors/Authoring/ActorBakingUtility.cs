using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pragma.Dots.Actors.Authoring
{
    internal static class ActorBakingUtility
    {
        /// <summary>Bakes again when the definition or any of its parents changes.</summary>
        public static void DependsOnLineage(IBaker baker, ActorDefinition definition)
        {
            var lineage = new List<ActorDefinition>();
            definition.GetLineage(lineage);
            foreach (var ancestor in lineage)
            {
                baker.DependsOn(ancestor);
            }
        }

        // The identity of the object in its scene file: the same wherever the scene is baked, unlike an instance id, and
        // unique in the project. Baking only runs in the editor.
        public static ulong GetSceneOrder(GameObject gameObject)
        {
#if UNITY_EDITOR
            return TypeHash.FNV1A64(GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString());
#else
            return 0;
#endif
        }
    }
}
