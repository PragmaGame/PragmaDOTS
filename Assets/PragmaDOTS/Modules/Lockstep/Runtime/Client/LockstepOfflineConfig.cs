using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Pragma.Dots.Lockstep
{
    /// <summary>
    /// Create this singleton in a client or local world to run a session without network: single player, tests,
    /// or a quick iteration loop. Destroy it to end the session.
    /// </summary>
    public struct LockstepOfflineConfig : IComponentData
    {
        public int tickRate;
        public int maxPlayers;
        public int inputSize;
        /// <summary>Zero picks a random seed.</summary>
        public uint seed;
        public int checksumInterval;
        public FixedList128Bytes<byte> startData;
        public FixedList64Bytes<byte> joinData;
        /// <summary>Wait until an actor registry exists in this world before creating the simulation.</summary>
        public bool waitForActorRegistry;

        public static LockstepOfflineConfig Create<TInput>(int tickRate = 30) where TInput : unmanaged
        {
            return new LockstepOfflineConfig
            {
                tickRate = tickRate,
                maxPlayers = 1,
                inputSize = UnsafeUtility.SizeOf<TInput>(),
            };
        }
    }
}
