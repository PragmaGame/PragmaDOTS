using Pragma.Dots.Lockstep;

namespace Pragma.Dots.Tests
{
    /// <summary>A server behind a redundant channel, as a loopback network delivers datagrams to it.</summary>
    internal sealed unsafe class ChannelServerEndpoint : ILockstepServerEndpoint
    {
        private readonly ILockstepServerEndpoint _server;
        private readonly LockstepRedundantChannel _channel;

        public ChannelServerEndpoint(ILockstepServerEndpoint server, LockstepRedundantChannel channel)
        {
            _server = server;
            _channel = channel;
        }

        public void OnConnected(int connectionId)
        {
            _server.OnConnected(connectionId);
        }

        public void OnDisconnected(int connectionId)
        {
            _channel.RemoveConnection(connectionId);
            _server.OnDisconnected(connectionId);
        }

        public void OnPacket(int connectionId, byte* data, int length, double now)
        {
            _channel.Receive(connectionId, data, length, now);
            while (_channel.TryRead(out var from, out var packet, out var packetLength))
            {
                _server.OnPacket(from, packet, packetLength, now);
            }
        }
    }
}
