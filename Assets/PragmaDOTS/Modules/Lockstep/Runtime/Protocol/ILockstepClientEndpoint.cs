namespace Pragma.Dots.Lockstep
{
    /// <summary>
    /// The client side of a session as a transport sees it: incoming packets. <see cref="LockstepClient"/> implements it,
    /// and so does whatever runs a <see cref="LockstepRedundantChannel"/> in front of a client.
    /// </summary>
    public unsafe interface ILockstepClientEndpoint
    {
        void OnPacket(byte* data, int length, double now);
    }
}
