using System;
using Unity.Mathematics;
using Unity.Transforms;

namespace Pragma.Dots.Actors
{
    /// <summary>
    /// Unity's transform: a <c>LocalTransform</c> where a placed actor stands (identity on the prefab of a type, which
    /// spawning code places) and its <c>LocalToWorld</c>. A scale that differs between the axes goes into a
    /// <c>PostTransformMatrix</c>, as baking does. For games without the Lockstep module, whose Transform part takes its
    /// place in new definitions.
    /// </summary>
    [Serializable]
    [ActorPartMenu("Local Transform")]
    [DefaultActorPart]
    public sealed class LocalTransformPart : ActorPart
    {
        public override void Build(IActorBuilder builder)
        {
            var placement = builder.Placement;
            var placementScale = builder.PlacementScale;
            var position = (float3)placement.position;
            var rotation = (quaternion)placement.rotation;
            var scale = (float3)placementScale;
            var isUniform = placementScale.x == placementScale.y && placementScale.y == placementScale.z;

            builder.AddComponent(LocalTransform.FromPositionRotationScale(position, rotation, isUniform ? scale.x : 1f));
            if (!isUniform)
            {
                builder.AddComponent(new PostTransformMatrix { Value = float4x4.Scale(scale) });
            }
            builder.AddComponent(new LocalToWorld { Value = float4x4.TRS(position, rotation, scale) });
        }
    }
}
