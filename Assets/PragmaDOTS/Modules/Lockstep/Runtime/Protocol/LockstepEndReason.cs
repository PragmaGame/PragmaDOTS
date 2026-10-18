
namespace Pragma.Dots.Lockstep
{
    /// <summary>Why a session ended for a client (<see cref="LockstepClient.EndReason"/>).</summary>
    public enum LockstepEndReason : byte
    {
        /// <summary>The server ended the match (<see cref="LockstepServer.EndGame"/>).</summary>
        Finished = 0,
        /// <summary>The server is shutting down.</summary>
        ServerShutdown = 1,
        /// <summary>
        /// Never sent: the transport lost the server (<see cref="LockstepClient.OnDisconnected"/>). The client still
        /// simulates the frames it has.
        /// </summary>
        ConnectionLost = 2,
    }
}
