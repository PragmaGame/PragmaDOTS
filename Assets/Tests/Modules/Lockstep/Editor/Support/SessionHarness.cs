using System;
using System.Collections.Generic;
using Pragma.Dots.Lockstep;
using Unity.Collections;

namespace Pragma.Dots.Tests
{
    /// <summary>A server and clients over a loopback network, stepped with simulated time.</summary>
    /// <remarks>
    /// With <c>redundant</c> every side talks through a <see cref="LockstepRedundantChannel"/> and the network loses and
    /// reorders datagrams (<c>loss</c>), as Netcode's unreliable pipeline would.
    /// </remarks>
    internal sealed class SessionHarness : IDisposable
    {
        private readonly List<int> _connectionIds = new List<int>();
        private readonly List<LockstepRedundantChannel> _clientChannels = new List<LockstepRedundantChannel>();
        private readonly HashSet<LockstepClient> _frozen = new HashSet<LockstepClient>();
        private readonly LockstepRedundantChannel _serverChannel;
        private readonly LockstepRedundantChannelSettings _channelSettings;
        private int _nextConnectionId = 1;

        public SessionHarness(LockstepServerSettings settings, double latency = 0, double jitter = 0, int seed = 1, bool redundant = false, double loss = 0,
                              LockstepRedundantChannelSettings? channelSettings = null)
        {
            Network = new LockstepLoopbackNetwork(seed) { Latency = latency, Jitter = jitter };
            if (redundant)
            {
                Network.Loss = loss;
                Network.Reorder = true;
                _channelSettings = channelSettings ?? LockstepRedundantChannelSettings.Default;
                _serverChannel = new LockstepRedundantChannel(Network.ServerTransport, _channelSettings);
                settings.MaxPacketSize = _serverChannel.MaxPacketSize;
                Server = new LockstepServer(settings, _serverChannel);
                Network.AttachServer(new ChannelServerEndpoint(Server, _serverChannel));
            }
            else
            {
                Server = new LockstepServer(settings, Network.ServerTransport);
                Network.AttachServer(Server);
            }
            Server.DesyncDetectedEvent += report => Desyncs.Add(report);
        }

        public LockstepLoopbackNetwork Network { get; }
        public LockstepServer Server { get; }
        public List<LockstepClient> Clients { get; } = new List<LockstepClient>();
        public List<LockstepDesyncReport> Desyncs { get; } = new List<LockstepDesyncReport>();
        public double Time { get; private set; } = 10;
        public int Frame { get; private set; }
        public bool IsRedundant => _serverChannel != null;

        /// <summary>The channel in front of the server, in redundant mode.</summary>
        public LockstepRedundantChannel ServerChannel => _serverChannel;

        /// <summary>
        /// The default client settings with a catch-up time budget the simulated time never reaches: how fast a late
        /// joiner catches up depends on the ticks per update alone, not on the speed of the machine.
        /// </summary>
        public static LockstepClientSettings ClientSettings
        {
            get
            {
                var settings = LockstepClientSettings.Default;
                settings.MaxSimulationMillisecondsPerUpdate = 10000f;
                return settings;
            }
        }

        public LockstepClient AddClient(LockstepSimulationOptions options = null, LockstepClientSettings? settings = null, FixedList64Bytes<byte> joinData = default,
                                        int slot = LockstepProtocol.ANY_SLOT)
        {
            var connectionId = _nextConnectionId++;
            var clientSettings = settings ?? ClientSettings;
            options = options ?? TestUtility.Options(typeof(TestGameplaySystem));
            LockstepClient client;
            LockstepRedundantChannel channel = null;
            if (IsRedundant)
            {
                channel = new LockstepRedundantChannel(Network.CreateClientTransport(connectionId), _channelSettings);
                clientSettings.MaxPacketSize = channel.MaxPacketSize;
                client = new LockstepClient(clientSettings, channel, options);
            }
            else
            {
                client = new LockstepClient(clientSettings, Network.CreateClientTransport(connectionId), options);
            }
            Network.SetTime(Time);
            Network.AttachClient(connectionId, channel != null ? new ChannelClientEndpoint(client, channel) : (ILockstepClientEndpoint)client);
            Clients.Add(client);
            _clientChannels.Add(channel);
            _connectionIds.Add(connectionId);
            client.Join(joinData, slot);
            channel?.Flush(Time);
            return client;
        }

        public void Disconnect(LockstepClient client)
        {
            var index = Clients.IndexOf(client);
            Network.DetachClient(_connectionIds[index]);
            Clients.RemoveAt(index);
            _connectionIds.RemoveAt(index);
            _clientChannels[index]?.Dispose();
            _clientChannels.RemoveAt(index);
            client.Dispose();
        }

        /// <summary>A frozen client neither updates nor sends, like an app the player minimized; packets still reach it.</summary>
        public void Freeze(LockstepClient client) => _frozen.Add(client);

        public void Thaw(LockstepClient client) => _frozen.Remove(client);

        /// <summary>One frame: clients send input, the network delivers, the server closes ticks.</summary>
        public void Step(double deltaTime, Action<LockstepClient, int> setInput = null)
        {
            StepClients(deltaTime, setInput);
            Server.Update(Time);
            _serverChannel?.Flush(Time);
            Network.Deliver(Time);
        }

        /// <summary>Advances only the clients, as if the server had frozen (a host whose app was paused).</summary>
        public void StepClientsOnly(double seconds, double deltaTime = 0.016)
        {
            var end = Time + seconds;
            while (Time < end)
            {
                StepClients(deltaTime, null);
            }
        }

        private void StepClients(double deltaTime, Action<LockstepClient, int> setInput)
        {
            Time += deltaTime;
            Frame++;
            Network.Deliver(Time);
            for (var i = 0; i < Clients.Count; i++)
            {
                if (_frozen.Contains(Clients[i]))
                {
                    continue;
                }
                setInput?.Invoke(Clients[i], Frame);
                Clients[i].Update(Time);
                _clientChannels[i]?.Flush(Time);
            }
            Network.Deliver(Time);
        }

        /// <summary>Advances only the server, as if every client had frozen.</summary>
        public void StepServerOnly(double seconds, double deltaTime = 0.01)
        {
            var end = Time + seconds;
            while (Time < end)
            {
                Time += deltaTime;
                Network.Deliver(Time);
                Server.Update(Time);
                _serverChannel?.Flush(Time);
                Network.Deliver(Time);
            }
        }

        /// <summary>Runs for a duration with frame times varying between 8 and 25 ms.</summary>
        public void Run(double seconds, Action<LockstepClient, int> setInput = null, int seed = 3)
        {
            var random = new System.Random(seed + Frame);
            var end = Time + seconds;
            while (Time < end)
            {
                Step(0.008 + random.NextDouble() * 0.017, setInput);
            }
        }

        public void Dispose()
        {
            foreach (var client in Clients)
            {
                client.Dispose();
            }
            foreach (var channel in _clientChannels)
            {
                channel?.Dispose();
            }
            Clients.Clear();
            _clientChannels.Clear();
            Server.Dispose();
            _serverChannel?.Dispose();
        }
    }
}
