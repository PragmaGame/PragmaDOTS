using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Authoring
{
    /// <summary>
    /// Bakes an <see cref="ActorAuthoring"/> into a scene entity (<see cref="LockstepSceneEntity"/>) that every simulation
    /// world starts with: a <see cref="LockstepActor"/> whose type is found by key when it is copied, built from its parts
    /// at the pose of the GameObject.
    /// </summary>
    internal sealed class ActorAuthoringBaker : Baker<ActorAuthoring>
    {
        public override void Bake(ActorAuthoring authoring)
        {
            var definition = authoring.Definition;
            if (definition == null)
            {
                Debug.LogError($"[Lockstep] The actor '{authoring.name}' has no definition; nothing is baked.", authoring);
                return;
            }
            ActorBakingUtility.DependsOnLineage(this, definition);
            if (!definition.TryGetKey(out var key))
            {
                Debug.LogError($"[Lockstep] The actor '{authoring.name}': its definition '{definition.name}' needs a key of 1 to 29 bytes of UTF-8.", authoring);
                return;
            }

            // Read through the baker, so moving the object bakes it again.
            var transform = GetComponent<Transform>();
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new LockstepSceneEntity { order = ActorBakingUtility.GetSceneOrder(authoring.gameObject) });
            AddComponent(entity, new LockstepActor { key = key, type = -1 });
            var parts = new List<ActorPart>();
            authoring.GetParts(parts);
            var builder = new BakerActorBuilder(this, entity, key, true, ActorAuthoring.GetPlacement(transform), ActorAuthoring.GetPlacementScale(transform), parts);
            LockstepActorUtility.Build(builder, parts, authoring);
        }
    }
}
