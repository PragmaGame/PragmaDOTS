using System;

namespace Pragma.Lockstep
{
    /// <summary>A <see cref="LockstepEntityId"/>: the identity commands and game data use to refer to the actor on every client.</summary>
    [Serializable]
    [ActorPartMenu("Entity Id")]
    public sealed class EntityIdPart : ActorPart
    {
        public override void Build(IActorBuilder builder)
        {
            builder.AddComponent(new LockstepEntityId());
        }
    }
}
