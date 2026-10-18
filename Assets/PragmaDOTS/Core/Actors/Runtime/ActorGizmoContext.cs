using System.Collections.Generic;
using UnityEngine;

namespace Pragma.Dots.Actors
{
    /// <summary>What <see cref="ActorPart.DrawGizmos"/> draws for: a placed actor in the scene view.</summary>
    public readonly struct ActorGizmoContext
    {
        public ActorGizmoContext(Transform transform, bool isSelected, IReadOnlyList<ActorPart> parts)
        {
            Transform = transform;
            IsSelected = isSelected;
            Parts = parts;
        }

        /// <summary>The GameObject of the placed actor.</summary>
        public Transform Transform { get; }

        /// <summary>True when the object is selected: draw the details.</summary>
        public bool IsSelected { get; }

        /// <summary>All the parts of the actor, overrides applied.</summary>
        public IReadOnlyList<ActorPart> Parts { get; }

        public bool HasPart<TPart>() where TPart : ActorPart => ActorParts.TryGet<TPart>(Parts, out _);
    }
}
