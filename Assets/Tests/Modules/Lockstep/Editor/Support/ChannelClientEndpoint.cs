using Pragma.Dots.Lockstep;

namespace Pragma.Dots.Tests
{
    /// <summary>A client behind a redundant channel, as a loopback network delivers datagrams to it.</summary>
    internal sealed unsafe class ChannelClientEndpoint : ILockstepClientEndpoint
    {
        private readonly ILockstepClientEndpoint _client;
        private readonly LockstepRedundantChannel _channel;

        public ChannelClientEndpoint(ILockstepClientEndpoint client, LockstepRedundantChannel channel)
        {
            _client = client;
            _channel = channel;
        }

        public void OnPacket(byte* data, int length, double now)
        {
            _channel.Receive(0, data, length, now);
            while (_channel.TryRead(out _, out var packet, out var packetLength))
            {
                _client.OnPacket(packet, packetLength, now);
            }
        }
    }
}
