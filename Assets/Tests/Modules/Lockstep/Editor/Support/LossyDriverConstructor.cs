#if PRAGMA_DOTS_UNITY_NETCODE
using Pragma.Dots.Lockstep.Netcode;
using Unity.Netcode;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;

namespace Pragma.Dots.Tests
{
    /// <summary>
    /// Clients on UDP sockets whose pipelines go through Transport's network simulator, which drops a share of their packets
    /// both ways; the server as usual. Out-of-band pipelines are unreliable on both sides, for the redundant channel.
    /// </summary>
    internal sealed class LossyDriverConstructor : INetworkStreamDriverConstructor
    {
        private readonly int _dropPercentage;

        public LossyDriverConstructor(int dropPercentage)
        {
            _dropPercentage = dropPercentage;
        }

        public void CreateClientDriver(NetcodeWorld world, ref NetworkDriverStore driver, NetDebug netDebug)
        {
            var settings = DefaultDriverBuilder.GetNetworkClientSettings();
            settings.WithSimulatorStageParameters(maxPacketCount: 1000, mode: ApplyMode.AllPackets, packetDropPercentage: _dropPercentage, randomSeed: 17);
            var instance = new NetworkDriverStore.NetworkDriverInstance { driver = NetworkDriver.Create(new UDPNetworkInterface(), settings) };
            DefaultDriverBuilder.CreateClientSimulatorPipelines(ref instance);
            driver.RegisterDriver(TransportType.Socket, instance);
            LockstepNetcode.UseUnreliableOutOfBandPipeline(ref driver);
        }

        public void CreateServerDriver(NetcodeWorld world, ref NetworkDriverStore driver, NetDebug netDebug)
        {
            DefaultDriverBuilder.RegisterServerDriver(world, ref driver, netDebug);
            LockstepNetcode.UseUnreliableOutOfBandPipeline(ref driver);
        }
    }
}
#endif
