using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Dots.Actors.Authoring
{
    /// <summary>
    /// Bakes an <see cref="ActorAuthoring"/> into a <see cref="PlacedActor"/>, ordered by the identity of the GameObject: an
    /// <see cref="Actor"/> built from its parts at the pose of the GameObject, whose type is found by key later
    /// (<see cref="ActorUtility.ResolveTypes(EntityManager)"/>, or the Lockstep module when it copies the actor into a
    /// simulation).
    /// </summary>
    internal sealed class ActorAuthoringBaker : Baker<ActorAuthoring>
    {
        public override void Bake(ActorAuthoring authoring)
        {
            var definition = authoring.Definition;
            if (definition == null)
            {
                Debug.LogError($"[Actors] The actor '{authoring.name}' has no definition; nothing is baked.", authoring);
                return;
            }
            ActorBakingUtility.DependsOnLineage(this, definition);
            if (!definition.TryGetKey(out var key))
            {
                Debug.LogError($"[Actors] The actor '{authoring.name}': its definition '{definition.name}' needs a key of 1 to 29 bytes of UTF-8.", authoring);
                return;
            }

            // Read through the baker, so moving the object bakes it again.
            var transform = GetComponent<Transform>();
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new PlacedActor { order = ActorBakingUtility.GetSceneOrder(authoring.gameObject) });
            AddComponent(entity, new Actor { key = key, type = -1 });
            var parts = new List<ActorPart>();
            authoring.GetParts(parts);
            var builder = new BakerActorBuilder(this, entity, key, true, ActorAuthoring.GetPlacement(transform), ActorAuthoring.GetPlacementScale(transform), parts);
            ActorUtility.Build(builder, parts, authoring);
        }
    }
}
