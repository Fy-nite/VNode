# V12 Networking Architecture

> **Last updated:** 2026-07-09
>
> This document covers the complete networking stack: transport layer, message bus, message types,
> DTO schemas, the SyncManager value-level sync system, component/element dirty tracking,
> threading model, and data flow.

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Transport Layer — TCP](#2-transport-layer--tcp)
3. [Message Bus — NetworkCables](#3-message-bus--networkcables)
4. [Wire Protocol](#4-wire-protocol)
5. [Message Types](#5-message-types)
6. [DTO Reference](#6-dto-reference)
7. [SyncManager — Value-Level Synchronization](#7-syncmanager--value-level-synchronization)
8. [DirtyTracker — Component / Element-Level Sync](#8-dirtytracker--component--element-level-sync)
9. [Connection & Sync Flow](#9-connection--sync-flow)
10. [Threading Model](#10-threading-model)
11. [LiteNetLib UDP Transport (Legacy)](#11-litenetlib-udp-transport-legacy)
12. [Security Considerations](#12-security-considerations)

---

## 1. Architecture Overview

V12 uses a **layered networking stack** built on three tiers:

```
┌─────────────────────────────────────────────────────────────────┐
│                    APPLICATION LAYER                             │
│                                                                  │
│  DirtyTracker     ───  component/element dirty sync              │
│  SyncManager      ───  SyncValue<T> value-level sync             │
│  GameRoot         ───  world snapshot (WorldSync)                │
└──────────────────────┬──────────────────────────────────────────┘
                       │  MessageDTO queues
                       ▼
┌─────────────────────────────────────────────────────────────────┐
│                  MESSAGE BUS (NetworkCables)                     │
│                                                                  │
│  Thread-safe ConcurrentQueue<MessageDTO>                        │
│  Events: OnMessageReceived, OnMessageSending                    │
│  Update() → ProcessIncoming() + ProcessOutgoing()               │
└──────────────────────┬──────────────────────────────────────────┘
                       │  Serialized via BSON + length-prefixed TCP
                       ▼
┌─────────────────────────────────────────────────────────────────┐
│                  TRANSPORT LAYER                                 │
│                                                                  │
│  NetworkHost  (TCP server)   ←→   NetworkClient  (TCP client)   │
│  LiteNetManager (UDP, legacy)                                    │
└─────────────────────────────────────────────────────────────────┘
```

### Two Parallel Sync Paths

| Path | Scope | Mechanism | MessageType | Serialization |
|------|-------|-----------|-------------|---------------|
| **DirtyTracker** | Full components & elements | `IComponent.OnDirty` / `IWorldElement.OnDirty` → `ComponentBatchDTO` / `ElementBatchDTO` | `WorldUpdate` | BSON (via `AncientCompressor`) |
| **SyncManager** | Individual `SyncValue<T>` values | `SyncValue<T>.Set()` → `IsDirty` → `SyncBatchBuilder.BuildBatch()` | `SyncBatch` | Custom binary (version + payload) |

---

## 2. Transport Layer — TCP

The primary transport is **TCP** using `TcpListener` / `TcpClient`. It lives in `V12/Core/NetworkCable/`.

### NetworkHost — Server (`NetworkHost.cs`)

- Listens on a configurable port (default `7777`).
- Accepts multiple concurrent TCP clients.
- Assigns each client a **unique server-assigned player ID** (starting at `1000`).
- Broadcasts outgoing messages to all connected clients.
- **Anti-echo**: skips the sender by matching `SenderToken` (a `TcpClient` reference set during receive).
- **Heartbeat monitoring**: checks every 5 s that each client sent data in the last 15 s; drops stale clients.
- Events: `OnClientConnected(long playerId)`, `OnClientDisconnected(long playerId)`, `OnStartFailed(Exception)`.
- Debug logging via static `DebugMode` flag.

### NetworkClient — Client (`NetworkClient.cs`)

- Connects to a host:port.
- Runs a receive loop that reads length-prefixed BSON frames.
- Events: `OnConnected`, `OnDisconnected`, `OnConnectionFailed(Exception)`.
- Subscribes to `Cables.OnMessageSending` to transmit outgoing messages.

### Data Flow: Sending

```
Game logic → Cables.SendData(MessageDTO) → Cables.SendQueue
    ↓ (networking thread, Cables.ProcessOutgoing)
Cables.OnMessageSending event
    ├─ NetworkHost.BroadcastMessage  (server) → TCP send to all clients
    └─ NetworkClient.SendMessage     (client) → TCP send to server
```

### Data Flow: Receiving

```
TCP receive → read length prefix (4 bytes) + payload (BSON MessageDTO)
    ↓
Deserialize → tag with SenderToken → Cables.EnqueueIncoming()
    ↓ (networking thread, Cables.ProcessIncoming)
Cables.OnMessageReceived event
    ├─ SyncNetworkBridge → SyncManager.ApplyIncoming()
    └─ (other application listeners)
```

---

## 3. Message Bus — NetworkCables

**File:** `V12/Core/NetworkCable/NetworkCables.cs`

`NetworkCables` is an in-process message bus that decouples the transport layer from the application.

### Core Structure

| Member | Type | Purpose |
|--------|------|---------|
| `SendQueue` | `ConcurrentQueue<MessageDTO>` | Outgoing messages enqueued by game code |
| `ReceiveQueue` | `ConcurrentQueue<MessageDTO>` | Incoming messages enqueued by transport |
| `OnMessageReceived` | `event Action<MessageDTO>` | Fired when processing incoming queue |
| `OnMessageSending` | `event Action<MessageDTO>` | Fired when processing outgoing queue |
| `Default` | `static NetworkCables` | Global singleton instance |

### Key Methods

- **`SendData(MessageDTO)`** — enqueues a message onto `SendQueue`.
- **`EnqueueIncoming(MessageDTO)`** — enqueues a received message onto `ReceiveQueue`.
- **`Update()`** — processes both queues: `ProcessIncoming()` then `ProcessOutgoing()`.
- **`ProcessOutgoing()`** — drains `SendQueue`, raising `OnMessageSending` for each.
- **`ProcessIncoming()`** — drains `ReceiveQueue`, raising `OnMessageReceived` for each.

### Static Helpers

- `NetworkCables.Send(MessageDTO)` — sends via the `Default` instance.

---

## 4. Wire Protocol

### Frame Format (TCP)

Every TCP message uses a **length-prefixed** format:

```
┌──────────────────────────────────────────────┐
│ Length Prefix (4 bytes, Int32, little-endian) │
├──────────────────────────────────────────────┤
│ Payload (variable length)                     │
│  = BSON-serialized MessageDTO                │
└──────────────────────────────────────────────┘
```

- Max payload size: **10 MB** (sanity check in both `NetworkHost` and `NetworkClient`).

### MessageDTO Schema

**File:** `V12/Core/Networking/MessageDTO.cs`

| Field | Type | Description |
|-------|------|-------------|
| `Message` | `byte[]` | BSON-encoded payload (content depends on `MessageType`) |
| `Sender` | `Uri` | Origin identifier (e.g. `networkcables://server`, `networkcables://gameroot`) |
| `MessageType` | `MessageType` | Enum identifying the type of message |
| `SenderToken` | `object?` | **Not serialized** (`[BsonIgnore]`). Transport-layer reference used by `NetworkHost` for anti-echo. |

### BSON Configuration

**File:** `V12/Core/Networking/BsonConfig.cs`

Called once at startup via `BsonConfig.Initialize()`:

- Allows all `V12.*` types through `ObjectSerializer`.
- Registers `Uri` → string serialization via `UriAsBsonStringSerializer`.
- Registers serializers for `uint`, `uint[]`, `int[]`, `double[]`, `long?`.

---

## 5. Message Types

**File:** `V12/Core/Networking/MessageType.cs`

| Enum Value | Purpose | Payload Format |
|------------|---------|----------------|
| `Text` | Generic text message | BSON-serialized string data |
| `Binary` | Raw binary data | Raw bytes |
| `Command` | Server / client commands | Arbitrary BSON |
| `Event` | Generic game events | BSON-serialized event data |
| `WorldSync` | Full world snapshot sent to newly joined clients | `WorldSyncDTO` → BSON via `AncientCompressor.Compress(world)` |
| `WorldUpdate` | Incremental dirty component/element batches | `ComponentBatchDTO` or `ElementBatchDTO` → BSON |
| `WorldArchive` | V12World archive (.zip) asset bundle sent to clients on join | Custom binary: `[nameLen(4)] [name(bytes)] [archive(bytes)]` |
| `PlayerJoin` | Player joined the game | (Raised as event; payload TBD) |
| `PlayerLeave` | Player disconnected | (Raised as event; payload TBD) |
| `PlayerBan` | Player banned | (Reserved) |
| `PlayerKick` | Player kicked | (Reserved) |
| `PlayerAction` | Player action event | (Reserved) |
| `PlayerSync` | Player position/rotation/state | `PlayerSyncDTO` → BSON |
| `SyncBatch` | SyncManager value-level batch (separate from DirtyTracker) | Custom binary: `SyncBatchBuilder.BuildBatch()` |
| `Heartbeat` | Connection keep-alive | Empty or minimal BSON |
| `Error` | Error notification | BSON-serialized error data |

---

## 6. DTO Reference

### WorldSyncDTO

**File:** `V12/Core/Networking/WorldSyncDTO.cs`

Used for full world snapshots (`MessageType.WorldSync`).

```csharp
public class WorldSyncDTO
{
    string? WorldName;
    List<ElementSyncDTO> Elements;
}

public class ElementSyncDTO
{
    long Id;
    string? Name;
    string? Description;
    List<ComponentSyncDTO> Components;   // assembly-qualified TypeName + BSON data
    List<ElementSyncDTO> Children;       // recursive
}

public class ComponentSyncDTO
{
    string TypeName;   // e.g. "V12.Components.TransformComponent, V12"
    byte[] Data;       // BSON-serialized component instance
}
```

### PlayerSyncDTO

**File:** `V12/Core/Networking/PlayerSyncDTO.cs`

```csharp
public class PlayerSyncDTO
{
    long PlayerId;
    string PlayerName;
    Vector3 Position;
    Quaternion Rotation;
    long ElementId;
    List<ComponentSyncDTO> Components;
    DateTime Timestamp;   // default: DateTime.UtcNow
}

public class PlayerLeaveDTO
{
    long PlayerId;
}
```

### DirtyTracker Batch DTOs (embedded in `DirtyTracker.cs`)

```csharp
public class ComponentBatchDTO
{
    List<ComponentSnapshot> Components;
    DateTime Timestamp;
}

public class ComponentSnapshot
{
    long Id;
    string Name;
    byte[]? Payload;   // BSON-serialized ComponentSyncDTO
}

public class ElementBatchDTO
{
    List<ElementSnapshot> Elements;
    DateTime Timestamp;
}

public class ElementSnapshot
{
    long Id;
    string Name;
    string Description;
    List<long> ComponentIds;
}
```

---

## 7. SyncManager — Value-Level Synchronization

The SyncManager provides **fine-grained synchronization of individual values** using `SyncValue<T>` wrappers, completely separate from the DirtyTracker's component/element-level sync.

### Architecture

```
SyncValue<T>.Set() → isDirty=true
    ↓
DirtyTracker.SendDirtyUpdates() → SyncManager.FlushDirty()
    ↓
SyncRegistry.DirtyValues() → SyncBatchBuilder.BuildBatch()
    ↓ (binary batch)
SyncManager.SendBytes (delegate)
    ↓
SyncNetworkBridge → wraps in MessageDTO(MessageType.SyncBatch)
    ↓
NetworkCables → transport → remote peer
    ↓
SyncNetworkBridge.OnMessageReceived(MessageType.SyncBatch)
    ↓
SyncManager.ApplyIncoming() → SyncBatchBuilder.EnumerateBatch()
    ↓
SyncRegistry.TryGet(key) → SyncValue<T>.ApplyRemotePayload()
```

### SyncValue\<T\>

**File:** `V12/Core/Networking/SyncValue.cs`

A generic, thread-safe wrapper around a value of type `T`.

| Member | Description |
|--------|-------------|
| `Key` | Globally unique string identifier (e.g. `"MeshComponent/123/MeshPoints"`) |
| `Path` | Alias for `Key` |
| `Value` | Gets the current value (thread-safe) |
| `Version` | Monotonically increasing `ulong` version counter |
| `IsDirty` | Whether the value has changed since last network flush |
| `Set(T)` | Updates value, increments version, marks dirty, fires events |
| `MarkDirty()` | Explicitly mark as dirty without changing value |
| `ClearDirty()` | Clear dirty flag |
| `ToByteArray()` | Binary serialize: `[version(8)] [payload]` |
| `ApplyRemotePayload()` | Deserialize and apply if remote version > local version |
| `TryApplyRemote(T, ulong)` | Version-gated remote write |

**Events:**
- `OnValueChanged(T old, T new)` — local value changed
- `OnDirty(SyncValue<T>)` — became dirty
- `OnRemoteApplied(T value, ulong version)` — remote update was applied

### Serialization Format (`ToByteArray` / `ApplyRemotePayload`)

```
┌──────────────────────────────────────────────┐
│ Version (8 bytes, UInt64)                     │
├──────────────────────────────────────────────┤
│ Payload (variable)                            │
│  - int: BinaryWriter.Write(int)               │
│  - long: BinaryWriter.Write(long)             │
│  - float: BinaryWriter.Write(float)           │
│  - double: BinaryWriter.Write(double)         │
│  - bool: BinaryWriter.Write(bool)             │
│  - string: length-prefixed UTF-8              │
│  - others: JSON fallback (length-prefixed)    │
└──────────────────────────────────────────────┘
```

### SyncBatchBuilder

**File:** `V12/Core/Networking/SyncBatchBuilder.cs`

Builds and parses binary batches of multiple `SyncValue` updates.

**Batch wire format:**

```
┌──────────────────────────────────────────────┐
│ Count (4 bytes, Int32)                        │
├──────────────────────────────────────────────┤
│ Item 0:                                       │
│   KeyLength (4 bytes)                         │
│   KeyBytes (UTF-8)                            │
│   PayloadLength (4 bytes)                     │
│   Payload (SyncValue.ToByteArray output)      │
├──────────────────────────────────────────────┤
│ Item 1: ...                                   │
├──────────────────────────────────────────────┤
│ ...                                           │
└──────────────────────────────────────────────┘
```

### SyncRegistry

**File:** `V12/Core/Networking/SyncRegistry.cs`

A global `ConcurrentDictionary<string, ISyncValue>` that maps keys to instances.

- `Register(ISyncValue)` — called automatically by `SyncValue<T>` constructor.
- `TryGet(key, out value)` — lookup by key.
- `AllValues()` — enumerate all registered sync values.
- `DirtyValues()` — filter to only dirty values.

### [Sync] Attribute & AutoCreator

**Files:** `SyncAttribute.cs`, `SyncAutoCreator.cs`

The `[Sync]` attribute can be placed on properties of type `SyncValue<T>`:

```csharp
[Sync]
public SyncValue<double[]> MeshPointsValue { get; set; }
```

`SyncAutoCreator.EnsureSyncValues(target)` uses reflection to find `[Sync]`-marked properties
that are null and instantiates them, automatically registering each in `SyncRegistry`.

Default key path format: `"{TypeName}/{PropertyName}"` (or custom via `[Sync("custom/path")]`).

### SyncNetworkBridge

**File:** `V12/Core/Networking/SyncNetworkBridge.cs`

Wires `SyncManager` to a `NetworkCables` instance:

- Sets `SyncManager.SendBytes` to enqueue a `MessageDTO(MessageType.SyncBatch)`.
- Subscribes to `Cables.OnMessageReceived` and routes `SyncBatch` messages to `SyncManager.ApplyIncoming()`.

### SyncSerializers

**File:** `V12/Core/Networking/SyncSerializers.cs`

Extensible serializer registry used by `SyncValue<T>`:

| Type | TypeId | Handler |
|------|--------|---------|
| `int` | 1 | `BinaryWriter.Write(int)` / `BinaryReader.ReadInt32()` |
| `long` | 2 | `BinaryWriter.Write(long)` / `BinaryReader.ReadInt64()` |
| `float` | 3 | `BinaryWriter.Write(float)` / `BinaryReader.ReadSingle()` |
| `double` | 4 | `BinaryWriter.Write(double)` / `BinaryReader.ReadDouble()` |
| `bool` | 5 | `BinaryWriter.Write(bool)` / `BinaryReader.ReadBoolean()` |
| `string` | 10 | Length-prefixed UTF-8 |
| Other | — | JSON fallback via `JsonSerializer` |

You can register custom serializers via `SyncSerializers.Register<T>(ISyncSerializer<T>)`.

---

## 8. DirtyTracker — Component / Element-Level Sync

**File:** `V12/Core/Networking/DirtyTracker.cs`

The `DirtyTracker` is an `IGameService` that tracks `IComponent` and `IWorldElement` changes
via their `OnDirty` events, batches them, and sends them as `WorldUpdate` messages.

### Lifecycle

```
GameRoot creates DirtyTracker during SetupNetworking()
    ↓
TrackWorld(world) subscribes to world.ElementAdded/Removed + all existing elements
    ↓
TrackElement(element) subscribes to OnDirty + all child elements recursively + all components
    ↓
Component/Element property changes → MarkDirty() → OnDirty event
    ↓
DirtyTracker collects in _dirtyComponents / _dirtyElements (ConcurrentDictionary)
    ↓
DirtyTracker.Update(deltaTime) — throttled at ThrottleInterval
    ↓
SendDirtyUpdates()
    ├─ SendComponentBatch() → ComponentBatchDTO → BSON → MessageType.WorldUpdate
    ├─ SendElementBatch()  → ElementBatchDTO  → BSON → MessageType.WorldUpdate
    └─ SyncManager.FlushDirty()  (value-level sync)
```

### Configuration

| Property | Default | Description |
|----------|---------|-------------|
| `MaxBatchSize` | `50` | Max dirty items per batch message |
| `ThrottleInterval` | `0f` | Min seconds between sends (`0` = send every update) |
| `HasConnectedPeer` | `null` | Predicate; when returns false, dirty sets are drained without sending (no traffic when idle) |

### Batch DTOs

**ComponentBatchDTO** — sent when one or more components are dirty:

```csharp
public class ComponentBatchDTO
{
    List<ComponentSnapshot> Components;   // Id + Name + BSON payload
    DateTime Timestamp;
}
```

Each `ComponentSnapshot` payload is a BSON-serialized `ComponentSyncDTO` containing the
assembly-qualified type name and BSON data of the serialized component.

**ElementBatchDTO** — sent when one or more world elements change identity (name, description, component list):

```csharp
public class ElementBatchDTO
{
    List<ElementSnapshot> Elements;   // Id + Name + Description + ComponentIds
    DateTime Timestamp;
}
```

### How to Use

```csharp
var tracker = new DirtyTracker(NetworkCables.Default)
{
    ThrottleInterval = 0.1f,   // 10 Hz
    MaxBatchSize = 50
};

// Track an entire world
tracker.TrackWorld(myWorld);

// Or individual items
tracker.TrackComponent(myComponent);
tracker.TrackElement(myElement);

// In update loop
dirtyTracker.Update(deltaTime);

// Cleanup
tracker.UntrackWorld(myWorld);
```

### MarkDirty Extension

**File:** `V12/Core/NetworkCable/NetworkCablesExtensions.cs`

Any object can be sent as a `MessageType.Event` message using the extension method:

```csharp
myObject.MarkDirty(NetworkCables.Default, senderUri, MessageType.Event);
```

Internally calls `AncientCompressor.Compress(obj)` and queues a `MessageDTO`.

---

## 9. Connection & Sync Flow

### Server Start

1. `GameRoot.SetupNetworking(port, isServer: true)` creates a `NetworkHost` (TCP server).
2. `GameRoot.StartNetworkingAsync()` calls `NetworkHost.StartAsync()` → starts listening.
3. A background `HeartbeatMonitorAsync` task runs (5s check, 15s timeout).

### Client Connect & World Sync

```
1. Client calls SetupNetworking(port, connectHost, isServer: false)
2. StartNetworkingAsync → NetworkClient.ConnectAsync()
3. Client establishes TCP connection
4. NetworkHost accepts → assigns PlayerId (1000+)
5. NetworkHost fires OnClientConnected(playerId)
6. GameRoot.OnClientConnected handler:
   a. Reads world archive (.zip) → sends as WorldArchive
   b. Calls PublishWorldMeshSyncValues(world) → populates Mesh SyncValues
   c. Serializes world via AncientCompressor.Compress(world) → WorldSyncDTO → BSON
   d. Sends as MessageDTO(MessageType.WorldSync)
7. Client receives WorldArchive + WorldSync messages via Cables.OnMessageReceived
8. Client deserializes world via AncientCompressor.Decompress<World>(data)
   → reconstructs Element + Component tree
9. DirtyTracker begins sending incremental WorldUpdate batches
10. SyncNetworkBridge pipes SyncBatch messages bidirectionally
```

### Heartbeat Flow

```
Server heartbeat monitor (every 5s):
  For each client:
    If last message received > 15s ago → close connection → fire OnClientDisconnected

Client: every message sent counts as a heartbeat (no dedicated heartbeat message needed)
```

---

## 10. Threading Model

```
┌──────────────────────────────────────────────────────────────┐
│ GAME/UPDATE THREAD (~1ms / 1000 Hz)                          │
│                                                              │
│ GameRoot.Update(dt)                                          │
│   └─ SelectedWorld.Update(dt)                                │
│   └─ Registry.Update(dt) → DirtyTracker.Update(dt)           │
│        └─ SendDirtyUpdates() → source-side sync              │
└──────────────────────┬───────────────────────────────────────┘
                       │  Enqueue/Dequeue MessageDTO
                       ▼
┌──────────────────────────────────────────────────────────────┐
│ NETWORKING THREAD (~10ms / 100 Hz)                           │
│                                                              │
│ Cables.Update()                                              │
│   ├─ ProcessIncoming() → OnMessageReceived (target-side)     │
│   └─ ProcessOutgoing() → OnMessageSending (source-side)      │
└──────────────┬───────────────────────────────────┬───────────┘
               │                                   │
               ▼                                   ▼
┌─────────────────────────┐    ┌──────────────────────────────┐
│ TRANSPORT THREADS       │    │ TRANSPORT THREADS             │
│ (Server)                │    │ (Client)                      │
│ AcceptClientsAsync      │    │ ReceiveLoopAsync              │
│ HandleClientAsync × N   │    │ (single read loop)            │
│ HeartbeatMonitorAsync   │    │                              │
└─────────────────────────┘    └──────────────────────────────┘
```

### Thread Safety

- `NetworkCables` uses `ConcurrentQueue<MessageDTO>` for cross-thread message passing.
- `SyncValue<T>` uses a `lock` for thread-safe read/write access to value, version, and dirty flag.
- `DirtyTracker` uses `ConcurrentDictionary` for dirty component/element collections.
- `NetworkHost._clients` is a `ConcurrentDictionary<TcpClient, bool>`.

### GameRoot Networking Thread

**File:** `V12/Core/GameRoot.cs` — `StartNetworkingThread()`

```csharp
_networkingThread = new Thread(() =>
{
    while (!token.IsCancellationRequested)
    {
        Cables.Update();      // process both queues
        Thread.Sleep(10);     // ~100Hz
    }
}) { IsBackground = true, Name = "V12NetworkingThread" };
```

---

## 11. LiteNetLib UDP Transport (Legacy)

**File:** `V12/Core/Networking/LiteNetManager.cs`

An older UDP-based transport using the [LiteNetLib](https://github.com/RevenantX/LiteNetLib) library.
Wraps `NetManager` implementing `INetEventListener` and `INetworkManager`.

| Feature | Detail |
|---------|--------|
| Serialization | JSON (not BSON) |
| Delivery | `DeliveryMethod.Unreliable` |
| Peer Mgmt | `ConcurrentDictionary<int, NetPeer>` |
| Client Map | `ConcurrentDictionary<string, int>` (string clientId → NetPeer.Id) |
| Receive | `BlockingCollection<RemoteMessage>` — blocking/polling receive |
| Polling | Background task runs `net.PollEvents()` every 5ms |

> **Note:** This transport is considered legacy. The primary transport is the TCP-based
> `NetworkHost` / `NetworkClient` pair.

---

## 12. Security Considerations

⚠️ **Current limitations** (not yet implemented):

- No authentication — any client can connect.
- No encryption — plaintext TCP (no TLS).
- No rate limiting — clients could flood the server.
- 10 MB message size limit serves as a basic sanity check only.

**Recommended for production:**

- Add TLS/SSL via `SslStream`.
- Implement authentication tokens (e.g. JWT) during handshake.
- Add per-client rate limiting in `NetworkHost`.
- Validate message schemas before deserialization.
- Implement reconnection logic with sequence numbers.

---

## File Index

All networking-related source files:

| File | Purpose |
|------|---------|
| `V12/Core/Networking/MessageType.cs` | `MessageType` enum (16 message types) |
| `V12/Core/Networking/MessageDTO.cs` | Universal message envelope |
| `V12/Core/Networking/SyncManager.cs` | Static sync orchestrator |
| `V12/Core/Networking/ISyncValue.cs` | SyncValue interface |
| `V12/Core/Networking/SyncValue.cs` | Generic `SyncValue<T>` implementation |
| `V12/Core/Networking/SyncRegistry.cs` | Global `ISyncValue` registry |
| `V12/Core/Networking/SyncAttribute.cs` | `[Sync]` attribute |
| `V12/Core/Networking/SyncAutoCreator.cs` | Auto-creates `SyncValue<T>` from `[Sync]` properties |
| `V12/Core/Networking/SyncBatchBuilder.cs` | Binary batch format builder/parser |
| `V12/Core/Networking/SyncSerializers.cs` | Primitive serializer registry |
| `V12/Core/Networking/ISyncSerializer.cs` | Serializer interfaces |
| `V12/Core/Networking/SyncNetworkBridge.cs` | Bridges SyncManager ↔ NetworkCables |
| `V12/Core/Networking/DirtyTracker.cs` | Component/element dirty tracking & batching |
| `V12/Core/Networking/DirtyTrackingExtensions.cs` | Extension methods for `OnDirty` |
| `V12/Core/Networking/AncientCompressor.cs` | BSON serialization engine |
| `V12/Core/Networking/BsonConfig.cs` | MongoDB BSON serializer registration |
| `V12/Core/Networking/WorldSyncDTO.cs` | World snapshot DTOs |
| `V12/Core/Networking/PlayerSyncDTO.cs` | Player state DTOs |
| `V12/Core/Networking/WorldXmlHotReloader.cs` | XML hot-reload (non-networking, co-located) |
| `V12/Core/Networking/LiteNetManager.cs` | Legacy UDP transport |
| `V12/Core/NetworkCable/NetworkCables.cs` | Message bus / pub-sub transport abstraction |
| `V12/Core/NetworkCable/NetworkHost.cs` | TCP server transport |
| `V12/Core/NetworkCable/NetworkClient.cs` | TCP client transport |
| `V12/Core/NetworkCable/NetworkCablesExtensions.cs` | `MarkDirty()` extension helper |
| `V12/Core/Interfaces/INetworkManager.cs` | Legacy network interface |

---

## Related Documents

- [`NETWORKING_GUIDE.md`](./NETWORKING_GUIDE.md) — Setup guide, examples, command-line flags
- [`DIRTY_TRACKING_GUIDE.md`](./DIRTY_TRACKING_GUIDE.md) — Component-level dirty tracking guide
- [`V12_Codebase_Guide.md`](./V12_Codebase_Guide.md) — Overall project architecture
