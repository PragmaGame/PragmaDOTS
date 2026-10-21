---
name: pragma-lockstep-sessions
description: Set up and run sessions of the lockstep module of Pragma DOTS (package com.pragma.dots) - offline play, hosting, joining and dedicated servers on Netcode for Entities, thin clients, custom transports through ILockstepTransport, LockstepServerSettings / LockstepClientSettings tuning (tick rate, input delay, jitter buffer, late join, player count), lobby, start and end of a match, session UI and replays. Use it whenever the task is about connecting players, bootstrapping worlds, starting or ending a match, latency or smoothness, or a session that does not start, join or stay connected - even if the user only says "multiplayer", "network", "lobby" or "host a game".
---

# Sessions with Pragma DOTS lockstep

A session has one `LockstepServer` and any number of `LockstepClient`s. The server never simulates: it collects the
input of every player, closes one frame per tick on its own clock and streams the frames to everybody. Each client
sends its input, receives the frames and steps its own simulation world with them. Both sides only exchange bytes
through `ILockstepTransport`, so the same session runs over Netcode for Entities, over an in-process loopback
(offline mode, tests) or over a transport of your own.

The lockstep module is opt-in: turn on *Lockstep (PRAGMA_DOTS_LOCKSTEP)* in *Project Settings > Pragma > DOTS* (off
in a new project) and reference `Pragma.Dots.Lockstep`; nothing below compiles without it. The Netcode part
(`LockstepNetcode`, the config, request and status components) is namespace and assembly
`Pragma.Dots.Lockstep.Netcode`, compiled only when `com.unity.netcode` 6.6.0 or later is installed.

Deeper reference:

- `references/settings.md` - every setting with defaults, the latency formula, bandwidth notes.
- The package README (`Assets/PragmaDOTS/README.md` in the package repository, or
  `Library/PackageCache/com.pragma.dots@*/README.md` in a project that installed it).
- `pragma-lockstep-gameplay` for simulation code, `pragma-lockstep-desync` when clients disagree.

## Pick the setup

| Situation | Setup |
|---|---|
| Single player, quick iteration, tutorials | Offline: `LockstepOfflineConfig` in a client or local world |
| A player hosts and others join | Netcode server world + client world in the host process; client worlds elsewhere |
| Dedicated server | Server world only (server build); clients connect |
| Load tests | Thin client worlds (Netcode PlayMode Tools: *Num Thin Clients*, or `ClientServerBootstrap.CreateThinClientWorld`) |
| Steam, a relay, WebSockets | Custom `ILockstepTransport` around `LockstepServer` / `LockstepClient` |

## Offline

```csharp
var config = LockstepOfflineConfig.Create<GameInput>(tickRate: 30); // tickRate, maxPlayers = 1, inputSize
config.seed = 0;                     // 0 picks a random seed
config.checksumInterval = 0;         // nobody to compare with
config.waitForActorRegistry = true;  // when a subscene bakes the actor catalog
world.EntityManager.CreateSingleton(config);
```

`LockstepOfflineSystem` (client and local worlds) runs an in-process server and client over `LockstepLoopbackNetwork`:
the complete protocol, no network. Destroy the singleton to end the session. Without Netcode the default world is a
local world and runs it; with Netcode, `World.DefaultGameObjectInjectionWorld` is a local world once the automatic
bootstrap is disabled. `LockstepWorlds.TryGetClient` and `TryGetServer` on that world return both sides.

## Netcode for Entities

```csharp
using Pragma.Dots.Lockstep;
using Pragma.Dots.Lockstep.Netcode;

// Host: server and client worlds in this process.
Application.runInBackground = true; // Netcode connections need updates while unfocused
var settings = LockstepServerSettings.Default;
settings.TickRate = 30;
settings.MaxPlayers = 4;
settings.InputSize = UnsafeUtility.SizeOf<GameInput>();
settings.MinPlayersToStart = 2;      // 0: start only through RequestStart

var server = ClientServerBootstrap.CreateServerWorld("Server");
var client = ClientServerBootstrap.CreateClientWorld("Client");
LockstepNetcode.HostSession(server, settings);
LockstepNetcode.JoinSession(client, LockstepClientSettings.Default);
LockstepNetcode.Listen(server, NetworkEndpoint.AnyIpv4.WithPort(7979));
LockstepNetcode.Connect(client, NetworkEndpoint.LoopbackIpv4.WithPort(7979));

// Join from another machine.
var remote = ClientServerBootstrap.CreateClientWorld("Client");
LockstepNetcode.JoinSession(remote, LockstepClientSettings.Default, LockstepBytes.ToFixedList64(new CharacterChoice { id = 2 }),
    waitForActorRegistry: true); // when a subscene bakes the actor catalog
LockstepNetcode.Connect(remote, NetworkEndpoint.Parse(address, 7979));
```

- `HostSession` / `JoinSession` create or overwrite the `LockstepServerConfig` / `LockstepClientConfig` singletons
  that `LockstepNetcodeServerSystem` / `LockstepNetcodeClientSystem` react to. The systems read them when a session
  starts, so new settings take effect only after the config is removed and created again. Creating the components
  yourself is equivalent (from a subscene, with a baker of your own).
- The client joins as soon as its world is connected (`NetworkId` exists) and leaves when the config or the
  connection goes away.
- **Netcode.** The package does not install Netcode for Entities: add `com.unity.netcode` (6.6.0 or later) to the
  project, and with the Lockstep module on, `Pragma.Dots.Lockstep.Netcode` (`LockstepNetcode`, the Netcode systems)
  compiles. Offline mode, replays and custom transports work without it.
- **Bootstrap.** Netcode's automatic bootstrap creates client/server worlds at start (in the editor per the PlayMode
  Tools). Either configure those worlds, or add `OverrideAutomaticNetcodeBootstrap` (*Disable*) to the scene or write a
  `ClientServerBootstrap` subclass, and create worlds on demand as above.
- **Dedicated server.** Only the server world. Set `MinPlayersToStart` or call `LockstepNetcode.RequestStart` from
  lobby logic. The server compares the clients' `LockstepSimulation.DefaultSimulationHash` with its own, so build it
  from the same code and with the same modules (or set `ValidateSimulationHash = false`). The module defines are per
  build target: a Linux Server target without `PRAGMA_DOTS_NAVIGATION` refuses Windows clients built with it.
  *Project Settings > Pragma > DOTS* writes them to every target.
- **Thin clients.** `LockstepNetcodeClientSystem` runs in thin client worlds too and forces `Simulate = false`. They
  still need a `LockstepClientConfig`, never simulate and never report checksums. `LockstepInputSystemGroup` does not
  run there, so they send an empty input unless a system of theirs (`WorldSystemFilterFlags.ThinClientSimulation`)
  gets the client with `LockstepWorlds.TryGetClient` and calls `SetInput` / `AddCommand`.
- Keep `TickRate` at or below Netcode's `ClientServerTickRate.SimulationTickRate`: the server closes lockstep ticks
  inside its own update.

## Match flow

```
Lobby --(MinPlayersToStart reached | RequestStart | server.StartGame)--> Running: countdown of StartDelaySeconds,
      then tick 0, 1, ... --(RequestEnd | server.EndGame)--> Ended
```

| Moment | Server side | Client side |
|---|---|---|
| Joined, waiting | `server.State == Lobby`, `PlayerCount` | `client.State == Lobby`, `LocalSlot` |
| Start | `RequestStart(serverWorld)` or `server.StartGame(now)`: `State` is `Running` at once, `GetTickPosition(now)` is negative during the countdown | The start message arrives during the countdown: `Running`, `StartedEvent`, the simulation world is created |
| Late join | `AllowLateJoin` (default true) | Receives every frame from tick 0, re-simulates within the catch-up budget, enters at `JoinTick` |
| Leave | Announced in a frame; the slot frees up | `client.Leave()`, or the connection/world goes away |
| End | `RequestEnd(serverWorld, reason)` / `server.EndGame(reason)`; removing `LockstepServerConfig` ends with `ServerShutdown` | `EndedEvent` with `client.EndReason`; the remaining frames are still simulated. A dropped connection ends with `ConnectionLost` (`client.OnDisconnected()`; the Netcode client system calls it and keeps the session registered until the config goes). Ending in the lobby tells the waiting players the same way; later joins get `GameEnded` |
| Rejected | | `RejectedEvent(reason)`: `InvalidRequest`, `ProtocolMismatch`, `SimulationMismatch`, `SessionFull`, `GameInProgress`, `GameEnded` |

With Netcode (`Pragma.Dots.Lockstep.Netcode`), the component equivalents in the server world are an entity with
`LockstepStartGameRequest`, an entity with `LockstepEndGameRequest { reason }`, and the `LockstepServerStatus`
singleton (`state`, `closedTicks`, `playerCount`) for UI.

Late-join loading screen: show `client.SimulatedTicks / (float)client.ServerTicks` (the ticks the server has closed,
from its start and pong messages) while `client.BufferedTicks` is large; re-simulation speed is bounded by
`MaxTicksPerUpdate` and `MaxSimulationMillisecondsPerUpdate`.

## Session data

- **Join data** (62 bytes): per player, sent when joining (`JoinSession(world, settings, joinData)`,
  `client.Join(joinData)`); the simulation reads `LockstepPlayer.GetJoinData<T>()`.
- **Start data** (126 bytes): per match, `LockstepServerSettings.StartData = LockstepBytes.ToFixedList128(rules)`;
  the simulation reads `LockstepSessionInfo.GetStartData<T>()`.
- **Seed**: `LockstepServerSettings.Seed` (0 picks one at start; `server.Config.Seed` is the final one); the
  simulation reads `LockstepSessionInfo.seed`.
- Limits (`LockstepProtocol`): `MAX_PLAYERS` 64, `MAX_INPUT_SIZE` 128, `MAX_JOIN_DATA_SIZE` 62,
  `MAX_START_DATA_SIZE` 126, `MAX_COMMANDS_PER_TICK` 32 per player (the rest move to later ticks).

## Reading session state

```csharp
if (LockstepWorlds.TryGetClient(clientWorld, out var client))
{
    // client.State, LocalSlot, JoinTick, RejectReason, EndReason, Config, SimulatedTicks, ConfirmedTicks, ServerTicks, InterpolationAlpha,
    // RoundTripTime (NaN until measured), JitterTicks, IsDesynced, DesyncTick,
    // Simulation (null before the start and on non-simulating clients; also LockstepWorlds.TryGetSimulation)
}
if (LockstepWorlds.TryGetServer(serverWorld, out var server)) // a server world, or the offline world
{
    // server.State, PlayerCount, ClosedTicks, StartTime, GetTickPosition(now), LateInputCount, VerifiedChecksumCount,
    // IsSlotInUse(slot), TryGetPlayerSlot(connectionId, out slot)
}
// Every session of the process: LockstepWorlds.GetClients(list), GetServers(list).
```

Subscribe to `client.StartedEvent`, `DesyncedEvent`, `EndedEvent`, `RejectedEvent` and `server.DesyncDetectedEvent`,
`ProtocolViolationEvent` for UI and logging. The *Window > Pragma > Lockstep Sessions* debug window shows all of this
for every session in the editor.

## Tuning latency and smoothness

Perceived input delay is roughly one round trip plus `1 + InputMarginTicks + PlayoutDelayTicks + 2 × jitter` ticks
(the 1 is the tick itself: the server closes tick T when its clock reaches T + 1; the playout part is capped by
`MaxPlayoutDelayTicks`). At 30 Hz and 60 ms round trip that is roughly 200 ms.

- Choppy movement on a jittery connection: raise `PlayoutDelayTicks` (smoothness for latency).
- Frequent late inputs (`server.LateInputCount` grows): raise `InputMarginTicks`; the client already adds margin
  automatically after late inputs.
- Input feels slow on a good network: lower `InputMarginTicks` and `PlayoutDelayTicks` (not below about 0.5), or
  raise `TickRate` (more bandwidth and simulation cost).
- One laggy player should not slow everybody: keep `MaxInputWaitTicks = 0` (their previous input repeats). For the
  classic "everybody waits for the slowest" behaviour, set it to a few ticks.
- Catch-up after a hitch steals frame time: lower `MaxSimulationMillisecondsPerUpdate`.

## Custom transports

```csharp
public sealed unsafe class MyTransport : ILockstepTransport
{
    public void Send(int connectionId, byte* data, int length)
    {
        // Copy the bytes and send them reliably and in order to connectionId (clients may ignore the id).
    }
}

// Server: one id per peer.
var server = new LockstepServer(settings, serverTransport); // validates the settings
LockstepWorlds.RegisterServer(world, server);                // for the debug window
server.OnConnected(peerId);
server.OnPacket(peerId, data, length, now);
server.OnDisconnected(peerId);
server.Update(now);                      // every frame

// Client.
var client = new LockstepClient(LockstepClientSettings.Default, transport, LockstepClientWorldUtility.CreateSimulationOptions(world, false));
LockstepWorlds.RegisterClient(world, client); // so the presentation (LockstepViewSystem, GameObject views) and the debug window find it
client.Join();
client.OnPacket(data, length, now);
// Every frame: hand over what the input systems wrote (a query over LockstepLocalInput and LockstepCommand),
// or call client.SetInput / AddCommand yourself, then update.
LockstepClientWorldUtility.PushLocalInput(world.EntityManager, localInputQuery, client);
client.Update(now);
```

Requirements: reliable, ordered, exactly-once delivery per connection; packets up to `MaxPacketSize` (the package
fragments larger messages); `now` from one monotonic clock in seconds per side. Dispose the server and clients and
unregister them (`LockstepWorlds.UnregisterClient` / `UnregisterServer`) when the session ends. For local tests and
bots, `LockstepLoopbackNetwork` (`ServerTransport`, `CreateClientTransport(id)`, `AttachServer`, `AttachClient`,
`DetachClient`, `Deliver(now)`, `Latency`, `Jitter`) is a ready in-process transport.

## Replays

```csharp
File.WriteAllBytes(path, client.ExportReplay()); // RecordReplay is on by default; server.ExportReplay() too

using (var replay = LockstepReplay.Read(bytes))
using (var player = new LockstepReplayPlayer(replay, options))
{
    var firstMismatch = player.SimulateToEnd(); // -1: every recorded checksum matched
}
```

`replay.Players` (slot, join and leave ticks, join data) and `replay.DurationSeconds` describe a replay without
simulating it.

To watch a replay with the game's presentation, play it to ordinary clients:

```csharp
var session = new LockstepReplaySession(LockstepReplay.Read(bytes));   // owns the replay
session.Watch(clientWorld, slot, clientSettings, LockstepClientWorldUtility.CreateSimulationOptions(clientWorld, true));
session.Speed = 2f; session.IsPaused = false;                           // host and clients together
session.Update(Time.unscaledTimeAsDouble);                              // every frame; Dispose unregisters
```

`LockstepReplayHost` takes the server's place: each connection joins as the slot it watches (`Watch(connectionId, slot)`
before the join; none means `InvalidRequest`), frames come on the playback clock, the session ends after the last frame,
client input is ignored, and checksums that differ from the recording raise `MismatchEvent` and a desync on the client.
Clients follow the pace through `LockstepClient.PlaybackSpeed`. Several worlds can watch different slots at once.

For checks without presentation, `LockstepReplayPlayer` simulates directly: `player.Update(deltaTime)` each frame with
`player.Simulation.World` and `player.InterpolationAlpha` (`Speed` scales playback), or `SimulateToEnd()`. Pass the same
`LockstepSimulationOptions` the game uses when the simulation uses actors (`WorldName`, `AutoDiscoverSystems`,
`AdditionalSystems`, `Initialize`, `CanCreate`; `LockstepClientWorldUtility.CreateSimulationOptions` builds the
game's). Replays need the same simulation build.

## Troubleshooting

| Symptom | Check |
|---|---|
| `TryGetClient` returns false | No `LockstepClientConfig` / `JoinSession` in that world, or the world is not connected yet (the session starts once Netcode assigned a `NetworkId`): wrong address or port, server not listening |
| Client stays `Joining` | Server world has no `LockstepServerConfig`: the server drops the packets |
| `SimulationMismatch` | Client and server built from different code (system list differs) |
| `GameInProgress` | Match already running and `AllowLateJoin` is off |
| `SessionFull` | `MaxPlayers` reached (64 at most) |
| Never starts | `MinPlayersToStart` not reached; call `RequestStart` |
| "input set on the client is N bytes but the session input size is M" | `InputSize` != `UnsafeUtility.SizeOf<TInput>()` of the struct written to `LockstepLocalInput` |
| Starts but nothing reacts to input | No system in `LockstepInputSystemGroup` writes `LockstepLocalInput`, or gameplay systems are outside `LockstepSimulationSystemGroup` |
| Disconnects when unfocused | `Application.runInBackground = false` |
| Entities Graphics copies missing | No actor registry in the presentation world, or `waitForActorRegistry` off while the subscene loads late |
| Actors missing from the simulation | The catalog subscene had not loaded when the simulation was created (`waitForActorRegistry`), or the run-time content (`ActorUtility.CreateRegistry`, `CreatePlaced`) was built after the session started |
| GameObject views missing in worlds created on demand | The view catalog was baked into a subscene that loaded before the world existed: register it with `EntityViewConfigProvider` (Views module, `pragma-dots-views`) |
| Desync errors | Use the `pragma-lockstep-desync` skill |
