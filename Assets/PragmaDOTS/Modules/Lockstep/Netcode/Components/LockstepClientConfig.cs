using Pragma.Dots.Utils;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Dots.Lockstep.Netcode
{
    /// <summary>
    /// Create this singleton in a client world to take part in the lockstep session of the server it connects to.
    /// The client joins as soon as the connection is established.
    /// </summary>
    public struct LockstepClientConfig : IComponentData
    {
        public LockstepClientSettings settings;
        /// <summary>Game-defined data that reaches the simulation in <see cref="LockstepPlayer.joinData"/>.</summary>
        public FixedList64Bytes<byte> joinData;
        /// <summary>The slot to join, as a lobby that seats its players sets it; unset takes the first free one.</summary>
        public OptionalValue<int> slot;
        /// <summary>Wait until an actor registry exists in the client world before creating the simulation.</summary>
        public bool waitForActorRegistry;
        /// <summary>
        /// Talk through a <see cref="LockstepRedundantChannel"/> carried by <see cref="LockstepDatagramRpc"/>s instead of
        /// reliable RPCs: on a lossy network a lost packet is replaced by the next one instead of holding every later frame
        /// back until it is resent. The server answers each client the way it talks. Needs the out-of-band pipeline made
        /// unreliable on the server and the clients (<see cref="LockstepNetcode.UseUnreliableOutOfBandPipeline"/>); without it
        /// the channel runs over a reliable pipeline and only costs traffic.
        /// </summary>
        public bool redundantChannel;
    }
}
