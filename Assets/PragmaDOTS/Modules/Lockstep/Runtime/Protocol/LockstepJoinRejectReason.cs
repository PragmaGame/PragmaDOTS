
namespace Pragma.Dots.Lockstep
{
    public enum LockstepJoinRejectReason : byte
    {
        None = 0,
        InvalidRequest = 1,
        ProtocolMismatch = 2,
        SimulationMismatch = 3,
        SessionFull = 4,
        GameInProgress = 5,
        GameEnded = 6,
        /// <summary>The join asked for a slot another player holds.</summary>
        SlotTaken = 7,
    }
}
