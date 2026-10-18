using System;
using Unity.Collections;
using UnityEngine;

namespace Pragma.Dots.Lockstep
{
    /// <summary>Configuration of a <see cref="LockstepServer"/>.</summary>
    [Serializable]
    public struct LockstepServerSettings
    {
        /// <summary>Simulation ticks per second.</summary>
        [field: SerializeField] public int TickRate { get; set; }
        [field: SerializeField] public int MaxPlayers { get; set; }
        /// <summary>Size of the game's input struct: <c>UnsafeUtility.SizeOf&lt;MyInput&gt;()</c>.</summary>
        [field: SerializeField] public int InputSize { get; set; }
        /// <summary>Random seed of the session; zero picks one when the game starts.</summary>
        [field: SerializeField] public uint Seed { get; set; }
        /// <summary>Start automatically once this many players joined; zero means only <see cref="LockstepServer.StartGame"/> starts.</summary>
        [field: SerializeField] public int MinPlayersToStart { get; set; }
        /// <summary>Countdown between the start message and tick 0, giving clients time to create their worlds.</summary>
        [field: SerializeField] public float StartDelaySeconds { get; set; }
        /// <summary>Accept players after the start; they catch up by re-simulating the match from tick 0.</summary>
        [field: SerializeField] public bool AllowLateJoin { get; set; }
        /// <summary>Ticks between checksums; zero disables desync detection.</summary>
        [field: SerializeField] public int ChecksumInterval { get; set; }
        /// <summary>
        /// How many ticks the server may hold a frame back waiting for a late input before it repeats the player's
        /// previous input. Zero never waits: one slow player never slows anybody else down.
        /// </summary>
        [field: SerializeField] public int MaxInputWaitTicks { get; set; }
        /// <summary>Inputs further ahead than this many ticks are ignored (a client clock gone wrong).</summary>
        [field: SerializeField] public int MaxInputLeadTicks { get; set; }
        /// <summary>Ticks between input timing feedback messages to each client.</summary>
        [field: SerializeField] public int FeedbackIntervalTicks { get; set; }
        /// <summary>Frame bytes sent to one connection per update; limits late-join catch-up bursts. Zero takes 32 KB.</summary>
        [field: SerializeField] public int MaxSendBytesPerUpdate { get; set; }
        /// <summary>Largest packet handed to the transport.</summary>
        [field: SerializeField] public int MaxPacketSize { get; set; }
        /// <summary>
        /// The largest message the server reassembles from a client; a larger one is dropped as a protocol violation. Zero
        /// takes 4 MB.
        /// </summary>
        [field: SerializeField] public int MaxMessageSize { get; set; }
        /// <summary>The largest data of one command; a larger one is a protocol violation. Zero takes 1 MB.</summary>
        [field: SerializeField] public int MaxCommandDataSize { get; set; }
        /// <summary>
        /// Command bytes of one player that go into one frame; the rest waits for the next ticks, and a single larger
        /// command goes alone. Bounds how fast the frame history grows. Zero takes 64 KB.
        /// </summary>
        [field: SerializeField] public int MaxCommandBytesPerTick { get; set; }
        /// <summary>
        /// Command bytes of one player the server holds, sent ahead or waiting for a frame; commands beyond it are dropped
        /// as a protocol violation. Zero takes 4 MB.
        /// </summary>
        [field: SerializeField] public int MaxBufferedCommandBytes { get; set; }
        /// <summary>Refuse clients whose <see cref="LockstepSimulation.DefaultSimulationHash"/> differs from this one.</summary>
        [field: SerializeField] public bool ValidateSimulationHash { get; set; }
        /// <summary>Game-defined start parameters, readable in the simulation through <see cref="LockstepSessionInfo"/>.</summary>
        [field: SerializeField] public FixedList128Bytes<byte> StartData { get; set; }

        public static LockstepServerSettings Default => new LockstepServerSettings
        {
            TickRate = 30,
            MaxPlayers = 8,
            InputSize = 0,
            Seed = 0,
            MinPlayersToStart = 1,
            StartDelaySeconds = 0.5f,
            AllowLateJoin = true,
            ChecksumInterval = 60,
            MaxInputWaitTicks = 0,
            MaxInputLeadTicks = 0,
            FeedbackIntervalTicks = 4,
            MaxSendBytesPerUpdate = 32 * 1024,
            MaxPacketSize = LockstepProtocol.DEFAULT_MAX_PACKET_SIZE,
            MaxMessageSize = 4 * 1024 * 1024,
            MaxCommandDataSize = 1024 * 1024,
            MaxCommandBytesPerTick = 64 * 1024,
            MaxBufferedCommandBytes = 4 * 1024 * 1024,
            ValidateSimulationHash = true,
        };

        /// <summary>Effective input lead limit: four seconds when not configured.</summary>
        public int EffectiveMaxInputLeadTicks => MaxInputLeadTicks > 0 ? MaxInputLeadTicks : TickRate * 4;

        public int EffectiveMaxSendBytesPerUpdate => MaxSendBytesPerUpdate > 0 ? MaxSendBytesPerUpdate : 32 * 1024;

        public int EffectiveMaxMessageSize => MaxMessageSize > 0 ? MaxMessageSize : 4 * 1024 * 1024;

        public int EffectiveMaxCommandDataSize => MaxCommandDataSize > 0 ? MaxCommandDataSize : 1024 * 1024;

        public int EffectiveMaxCommandBytesPerTick => MaxCommandBytesPerTick > 0 ? MaxCommandBytesPerTick : 64 * 1024;

        public int EffectiveMaxBufferedCommandBytes => MaxBufferedCommandBytes > 0 ? MaxBufferedCommandBytes : 4 * 1024 * 1024;

        public void Validate()
        {
            if (TickRate < 1 || TickRate > 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(TickRate), TickRate, "Tick rate must be in [1, 1000].");
            }
            if (MaxPlayers < 1 || MaxPlayers > LockstepProtocol.MAX_PLAYERS)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxPlayers), MaxPlayers, $"Max players must be in [1, {LockstepProtocol.MAX_PLAYERS}].");
            }
            if (InputSize < 0 || InputSize > LockstepProtocol.MAX_INPUT_SIZE)
            {
                throw new ArgumentOutOfRangeException(nameof(InputSize), InputSize, $"Input size must be in [0, {LockstepProtocol.MAX_INPUT_SIZE}] bytes.");
            }
            if (ChecksumInterval < 0 || ChecksumInterval > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(ChecksumInterval), ChecksumInterval, "Checksum interval must be in [0, 65535].");
            }
            if (MaxInputWaitTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxInputWaitTicks), MaxInputWaitTicks, "The wait cannot be negative.");
            }
            if (MaxPacketSize < 64)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxPacketSize), MaxPacketSize, "Packets must hold at least 64 bytes.");
            }
            if (EffectiveMaxInputLeadTicks > 4096)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxInputLeadTicks), MaxInputLeadTicks, "The input lead is limited to 4096 ticks.");
            }
            if (MaxSendBytesPerUpdate < 0 || MaxMessageSize < 0 || MaxCommandDataSize < 0 || MaxCommandBytesPerTick < 0 || MaxBufferedCommandBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxMessageSize), "Sizes and budgets cannot be negative.");
            }
            // A command and its wire form (type hash, payload, data length) have to fit a message and the buffer.
            var command = EffectiveMaxCommandDataSize + LockstepCommand.MAX_PAYLOAD_SIZE + 16;
            if (EffectiveMaxMessageSize < command || EffectiveMaxBufferedCommandBytes < command)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxCommandDataSize), MaxCommandDataSize,
                    "A command with the largest data has to fit the message size and the buffered command bytes.");
            }
        }
    }
}
