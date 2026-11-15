using Unity.Netcode;

namespace Pragma.Dots.Lockstep.Netcode
{
    /// <summary>
    /// A driver constructor that builds the drivers with another constructor (Netcode's default unless given one) and
    /// then makes their out-of-band RPC pipeline unreliable, for the redundant channel
    /// (<see cref="LockstepNetcode.UseUnreliableOutOfBandPipeline"/>).
    /// </summary>
    /// <remarks>
    /// Assign it before the worlds are created: <c>NetworkStreamReceiveSystem.DriverConstructor = new LockstepDriverConstructor();</c>,
    /// or wrap the game's own constructor (a Relay one) in it. Server and clients both need it.
    /// </remarks>
    public sealed class LockstepDriverConstructor : INetworkStreamDriverConstructor
    {
        private readonly INetworkStreamDriverConstructor _inner;

        public LockstepDriverConstructor(INetworkStreamDriverConstructor inner = null)
        {
            _inner = inner ?? DefaultDriverBuilder.DefaultDriverConstructor;
        }

        public void CreateClientDriver(NetcodeWorld world, ref NetworkDriverStore driver, NetDebug netDebug)
        {
            _inner.CreateClientDriver(world, ref driver, netDebug);
            LockstepNetcode.UseUnreliableOutOfBandPipeline(ref driver);
        }

        public void CreateServerDriver(NetcodeWorld world, ref NetworkDriverStore driver, NetDebug netDebug)
        {
            _inner.CreateServerDriver(world, ref driver, netDebug);
            LockstepNetcode.UseUnreliableOutOfBandPipeline(ref driver);
        }
    }
}
