using System;

namespace Pragma.Dots.Actors
{
    /// <summary>
    /// A piece of an actor that writes its components when the actor is built: its transform, its path-finding agent,
    /// one component of the game's own. Parts are listed in an <see cref="ActorDefinition"/> and overridden by derived
    /// definitions and by placements (<see cref="ActorAuthoring"/>).
    /// </summary>
    /// <remarks>
    /// <para>An actor has at most one part of each type: a derived definition or a placement replaces the part of the same
    /// type. A part that needs several entries (weapons, stats) keeps a list.</para>
    /// <para>The same <see cref="Build"/> runs when a subscene bakes and when actors are built at run time
    /// (<see cref="ActorUtility"/>), so it must give the same result from the same data on every machine: keep
    /// <c>FixedPoint</c> fields rather than floats, and read nothing but the part's own data and the builder.</para>
    /// <para>Parts are serialized by reference (<c>[SerializeReference]</c>): renaming or moving a part class loses its data
    /// unless the class gets <c>[UnityEngine.Scripting.APIUpdating.MovedFrom]</c>.</para>
    /// </remarks>
    [Serializable]
    public abstract class ActorPart
    {
        /// <summary>Adds the part's components to the actor being built.</summary>
        public abstract void Build(IActorBuilder builder);

        /// <summary>Draws the part of a placed actor in the scene view.</summary>
        public virtual void DrawGizmos(in ActorGizmoContext context)
        {
        }
    }
}
