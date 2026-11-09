# VNode

Node-based asset storage server for the V12 ecosystem.

## What is VNode?

VNode is a standalone file storage service — like Amazon S3, but purpose-built for
the V12 game engine. It stores files (leaves) in directories (branches) organized
by namespace (groups), and serves them over TCP (port 2560) and HTTP.

## Quick Start

```bash
cd libs/VNode/src
dotnet run -- --data=./vnode-data --port=2560
```

## Features

- **Tree filesystem** — Groups → Branches → Leaves
- **Pluggable storage** — Local disk, in-memory, or custom backends
- **TCP protocol** — Fast binary transport for game engines
- **REST API** — HTTP/JSON for web clients and debugging
- **VNode:// URIs** — `VNode://Group/Path/File.ext`
- **Permissions** — Linux-inspired look/speak/run model

## Documentation

- [Architecture & API Reference](docs/VNode_Architecture.md) — Full reference
- [Networking](docs/VNode_Networking.md) — Ports, relay server
- [Permissions](docs/VNode_Permissions.md) — Permission model
- [Storage Provider](docs/VSystem_IStorage.md) — Pluggable backends
- [Authentication](docs/VSystem_Auth.md) — Auth model

## Client Usage

```csharp
var client = new VNodeClient("127.0.0.1", 2560);
await client.ConnectAsync();

byte[]? data = await client.ReadAsync("wiki/docs/pages/home.xml");
await client.WriteTextAsync("wiki/test.xml", "<WorldML/>");
VNodeEntry[] files = await client.ListAsync("wiki/docs");
```

## License

Part of the V12 engine ecosystem by Finite.
