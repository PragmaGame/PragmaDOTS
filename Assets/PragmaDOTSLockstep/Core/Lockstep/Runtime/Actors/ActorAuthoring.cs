using System;
using System.Collections.Generic;
using Pragma.Lockstep.Mathematics;
using UnityEngine;

namespace Pragma.Lockstep
{
    /// <summary>
    /// An actor placed on a map: a definition at the pose of this GameObject, with parts of its own that override the
    /// definition's (the owner of a main building, the radius of a capture point). In a subscene it bakes into a scene
    /// entity every simulation starts with; on a prefab it is a preset to drop into maps.
    /// </summary>
    /// <remarks>
    /// The position, rotation and uniform scale (X) of the GameObject become the placement, converted to fixed point once
    /// at bake time. The baked entity is copied whole into the simulation: keep renderers off this GameObject and show the
    /// actor with a view.
    /// </remarks>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [SelectionBase]
    [AddComponentMenu("Pragma/Lockstep/Actor")]
    public sealed class ActorAuthoring : MonoBehaviour
    {
        private static readonly List<ActorPart> GizmoParts = new List<ActorPart>();

        [SerializeField, Tooltip("The actor type placed here; its parts are where this object starts from.")]
        private ActorDefinition _definition;

        [SerializeReference] private ActorPart[] _overrides = Array.Empty<ActorPart>();

        public ActorDefinition Definition
        {
            get => _definition;
            set => _definition = value;
        }

        /// <summary>The parts of this placement alone; each one replaces the definition's part of its type.</summary>
        public IReadOnlyList<ActorPart> Overrides => _overrides;

        public void SetOverrides(params ActorPart[] overrides)
        {
            _overrides = overrides ?? Array.Empty<ActorPart>();
        }

        /// <summary>The parts the placed actor is built from: the definition's, with the overrides applied.</summary>
        public void GetParts(List<ActorPart> result)
        {
            if (_definition != null)
            {
                _definition.GetParts(result);
            }
            ActorParts.Override(result, _overrides);
        }

        /// <summary>The pose of the GameObject in fixed point, with its uniform scale (X).</summary>
        public LockstepTransform GetPlacement() => GetPlacement(transform);

        /// <summary>The scale of the GameObject along its local axes, in fixed point.</summary>
        public FixedVector3 GetPlacementScale() => GetPlacementScale(transform);

        /// <summary>The pose of <paramref name="source"/> in fixed point, with its uniform scale (X).</summary>
        public static LockstepTransform GetPlacement(Transform source)
        {
            return new LockstepTransform
            {
                position = (FixedVector3)source.position,
                rotation = FixedMath.NormalizeSafe((FixedQuaternion)source.rotation),
                scale = (FixedPoint)source.lossyScale.x,
            };
        }

        public static FixedVector3 GetPlacementScale(Transform source) => (FixedVector3)source.lossyScale;

        private void OnDrawGizmos()
        {
            DrawGizmos(false);
        }

        private void OnDrawGizmosSelected()
        {
            DrawGizmos(true);
        }

        private void DrawGizmos(bool selected)
        {
            GizmoParts.Clear();
            GetParts(GizmoParts);
            var context = new ActorGizmoContext(transform, selected, GizmoParts);
            foreach (var part in GizmoParts)
            {
                part.DrawGizmos(context);
            }
            GizmoParts.Clear();
        }

#if UNITY_EDITOR
        /// <summary>Raised in the editor when a placed actor is enabled or edited, for tools that show actors (previews).</summary>
        public static event Action<ActorAuthoring> EditorChanged;

        /// <summary>Raised in the editor when a placed actor is disabled or destroyed.</summary>
        public static event Action<ActorAuthoring> EditorDisabled;

        private void OnEnable()
        {
            EditorChanged?.Invoke(this);
        }

        private void OnValidate()
        {
            EditorChanged?.Invoke(this);
        }

        private void OnDisable()
        {
            EditorDisabled?.Invoke(this);
        }
#endif
    }
}
