# VNode v2.0 — Architecture & API Reference

> **Version:** 2.0.0 | **Last updated:** 2026-07-23
>
> VNode is a node-based asset storage server for the V12 ecosystem — analogous to
> Amazon S3 but purpose-built for game worlds, wikis, and the V12 engine pipeline.

---

## Overview

VNode provides:

- **Tree filesystem** — Groups → Branches (directories) → Leaves (files)
- **Pluggable storage** — Local disk, in-memory, or custom backends via `IStorageProvider`
- **TCP protocol** — Fast binary transport on port 2560 for game engines and services
- **REST API** — HTTP/JSON endpoints for web clients and debugging
- **VNode:// URI scheme** — Universal addressing: `VNode://Group/Folder/File.ext`
- **Permission model** — Linux-inspired look/speak/run attributes (`.perms` files)
- **Relay server** — Offload multiplayer traffic (UDP port 2565)

VNode runs as a standalone service alongside the V12 game engine, each on its own port.

---

## Quick Start

```bash
# Run VNode with local disk storage
cd libs/VNode/src
dotnet run -- --data=./vnode-data --port=2560

# Test the API
curl http://localhost:2560/api/status
curl http://localhost:2560/api/list/
```

---

## Architecture

```
┌─────────────────────────────────────────────────┐
│                   VNode Server                   │
│                                                  │
│  ┌──────────┐  ┌──────────────┐  ┌────────────┐│
│  │ TCP Port │  │  REST API    │  │  VNode     ││
│  │   2560   │  │  /api/*      │  │  Protocol  ││
│  └────┬─────┘  └──────┬───────┘  └─────┬──────┘│
│       │               │                │        │
│       └───────────────┼────────────────┘        │
│                       │                         │
│              ┌────────▼────────┐                │
│              │  IStorageProvider│                │
│              │  (pluggable)    │                │
│              └────────┬────────┘                │
│                       │                         │
└───────────────────────┼─────────────────────────┘
                        │
        ┌───────────────┼───────────────┐
        │               │               │
   ┌────▼────┐    ┌─────▼─────┐   ┌────▼────┐
   │  Local  │    │  In-Memory│   │ Custom  │
   │  Disk   │    │  (testing)│   │  (S3..) │
   └─────────┘    └───────────┘   └─────────┘
```

---

## Filesystem Model

VNode uses tree-based terminology:

| Term | Analogy | C# Type | Description |
|------|---------|---------|-------------|
| **Leaf** | File | `VNode.FileSystem.Leaf` | A file with content, size, and hash |
| **Branch** | Directory | `VNode.FileSystem.Branch` | A folder containing children |
| **Group** | Namespace | Top-level VNode:// segment | A collection of branches |

### URI Format

```
VNode://<Group>/<Path/...>/<FileName>.ext
```

Examples:
```
VNode://wiki/docs/pages/home.xml
VNode://worlds/eco/save.dat
VNode://Hashes/a1b2c3d4e5f6...
```

---

## Storage Providers

### IStorageProvider Interface

```csharp
public interface IStorageProvider
{
    string ProviderName { get; }
    Stream Open(string path);
    void Save(string path, Stream data);
    void Save(string path, byte[] data);
    bool Delete(string path);
    bool Exists(string path);
    bool DirectoryExists(string path);
    IEnumerable<INode> List(string directoryPath);
    long GetLastModified(string path);
    long GetSize(string path);
}
```

### Built-in Providers

| Provider | Class | Use Case |
|----------|-------|----------|
| **Local Disk** | `LocalStorageProvider` | Production — maps VNode paths to a directory on disk |
| **In-Memory** | `InMemoryStorageProvider` | Testing — ephemeral, no persistence |

### Custom Providers

Implement `IStorageProvider` to add S3, database, or network storage backends.
The VNode server and client work identically regardless of the backend.

---

## TCP Protocol

VNode uses a simple length-prefixed binary protocol over TCP (port 2560):

```
Wire format:
┌──────────────┬──────────────────┬─────────────────┐
│ 4 bytes LE   │ N bytes JSON     │ M bytes binary  │
│ metadata len │ request/response │ optional payload│
└──────────────┴──────────────────┴─────────────────┘
```

### Request Operations

| Op | Description | Path | Payload |
|----|-------------|------|---------|
| `read` | Read file content | file path | — |
| `write` | Write file content | file path | file bytes |
| `delete` | Delete a file | file path | — |
| `list` | List directory | dir path | — |
| `exists` | Check existence | path | — |
| `stat` | Get file metadata | file path | — |
| `ping` | Health check | — | — |

### Response Format

```json
{
  "success": true,
  "requestId": "a1b2c3d4",
  "error": null,
  "dataLength": 1024,
  "entries": [
    { "name": "home.xml", "path": "docs/home.xml", "isDirectory": false, "size": 1024, "lastModified": 1721750400 }
  ]
}
```

---

## REST API

For web clients and debugging, VNode exposes HTTP endpoints:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/status` | GET | Server status, port, storage backend, client count |
| `/api/list/{path}` | GET | List files/dirs at path |
| `/api/read/{path}` | GET | Read file content (binary) |
| `/api/exists/{path}` | GET | Check file/dir existence |
| `/` | GET | API index with endpoint list |

---

## Configuration

### Command-Line Arguments

```bash
dotnet run -- --data=/var/vnode --port=2560
```

| Argument | Default | Description |
|----------|---------|-------------|
| `--data={path}` | `./vnode-data` | Storage root directory |
| `--port={n}` | `2560` | TCP + HTTP listen port |

### appsettings.json

```json
{
  "VNode": {
    "DataPath": "./vnode-data",
    "Port": 2560
  }
}
```

---

## Client Usage (VNodeClient)

```csharp
using VNode.Networking;

var client = new VNodeClient("127.0.0.1", 2560);
await client.ConnectAsync();

// Read a file
byte[]? data = await client.ReadAsync("wiki/docs/pages/home.xml");
string? text = await client.ReadTextAsync("wiki/docs/pages/home.xml");

// Write a file
await client.WriteTextAsync("wiki/docs/pages/test.xml", "<WorldML>...</WorldML>");

// List a directory
VNodeEntry[] entries = await client.ListAsync("wiki/docs/pages");

// Check existence
bool exists = await client.ExistsAsync("wiki/docs/pages/home.xml");

// Get metadata
VNodeEntry? stat = await client.StatAsync("wiki/docs/pages/home.xml");

// Health check
bool alive = await client.PingAsync();

client.Dispose();
```

---

## Integration with V12.Web

The `VNodeFileClient` in V12.Web provides automatic fallback:

1. **Try TCP first** — connects to VNode server at configured host:port
2. **Fall back to local disk** — if VNode is unavailable, reads from filesystem

```csharp
// In V12.Web DI setup
var vnodeClient = new VNodeFileClient(
    basePath: options.DataPath,
    vnodeHost: "127.0.0.1",
    vnodePort: 2560
);

// Try to connect (optional — works without VNode running)
await vnodeClient.TryConnectAsync();

// Read — uses TCP if connected, local disk otherwise
byte[]? content = await vnodeClient.ReadFileAsync("VNode://wiki/docs/pages/home.xml");
```

---

## Permission Model

Permissions are stored in `.perms` JSON files within directories:

```json
{
  "entries": [
    {
      "path": "relative/path",
      "hash": "sha256-of-file",
      "owner": "user|group|hardware-id",
      "attributes": {
        "look": true,
        "speak": false,
        "run": false
      }
    }
  ],
  "basePath": "wiki/docs"
}
```

| Attribute | Linux Analog | Description |
|-----------|-------------|-------------|
| **look** | Read (r) | View/list a file or its metadata |
| **speak** | Write (w) | Modify or delete a file |
| **run** | Execute (x) | Execute a file (e.g., Lua scripts) |

---

## Source Files

| File | Purpose |
|------|---------|
| `src/Interfaces/INode.cs` | Base interface for all filesystem entries |
| `src/Interfaces/IStorageProvider.cs` | Pluggable storage backend contract |
| `src/Interfaces/perms.cs` | Permission entry and manifest models |
| `src/FileSystem/Leaf.cs` | File node with content access |
| `src/FileSystem/Branch.cs` | Directory node with child navigation |
| `src/Storage/LocalStorageProvider.cs` | Disk-backed storage implementation |
| `src/Storage/InMemoryStorageProvider.cs` | In-memory storage for testing |
| `src/Networking/VNodeProtocol.cs` | Wire protocol request/response models |
| `src/Networking/VNodeServer.cs` | TCP server (port 2560) |
| `src/Networking/VNodeClient.cs` | TCP client for connecting to VNode |
| `src/Networking/URIHandler.cs` | VNode:// URI parser |
| `src/Globals.cs` | Constants and defaults |
| `src/Program.cs` | Entry point with DI, TCP server, REST API |

---

## Related Documents

- [`VNode_Networking.md`](./VNode_Networking.md) — Ports, relay server, advanced usage
- [`VNode_Permissions.md`](./VNode_Permissions.md) — Permission model details
- [`VSystem_IStorage.md`](./VSystem_IStorage.md) — Storage provider design
- [`VSystem_Auth.md`](./VSystem_Auth.md) — Authentication model
- [`NETWORKING_ARCHITECTURE.md`](./NETWORKING_ARCHITECTURE.md) — V12 transport layer
