# VNode Networking

> **Last updated:** 2026-07-09
>
> This document describes how VNode hosts its servers, which ports they use, and related
> networking concepts. It does **not** cover the VNode C# API or the underlying V12
> transport protocol — for transport details see
> [`NETWORKING_ARCHITECTURE.md`](./NETWORKING_ARCHITECTURE.md).

---

## Overview

VNode is a node-based server for asset storage, Authentication and more — similar to Amazon S3 or other object
storage solutions. It runs on top of the **V12 networking engine**, which provides
the TCP transport, message bus, and synchronization primitives described in
[`NETWORKING_ARCHITECTURE.md`](./NETWORKING_ARCHITECTURE.md).

For file access in a multi-network setup like V12, VNode uses a custom `VNode://`
URI scheme. This allows routers to direct users and information to the correct
location without ambiguity.

---

## Ports

| Transport | Port | Purpose |
|-----------|------|---------|
| TCP       | 2560 | General asset serving |
| UDP       | 2565 | Relay server (client connections) |

> **Note:** These ports differ from the V12 engine port (default `7777`). VNode and the
> V12 game engine are separate services that may run simultaneously on the same host,
> each on their own ports. The underlying V12 TCP transport stack
> (`NetworkHost` / `NetworkClient`) is reused by VNode — see
> [`NETWORKING_ARCHITECTURE.md`](./NETWORKING_ARCHITECTURE.md) §2.

---

## File System

Files on the server are referenced by paths starting at the base of the container
and descending downward.

### Terminology

| Term | Definition |
|------|------------|
| **Leaf** | A file (analogy: leaf of a tree) |
| **Branch** | A directory or folder that contains leaves |
| **Group** | A collection of branches on the main node |

Groups are supported via custom referencing: a folder can be owned by one or more
permission entities, whose owner can be an authenticated user, a group, or a piece
of hardware. See [`VNode_Permissions.md`](./VNode_Permissions.md) for the full
permission model.

### URI Format

When referencing files under a group, use the following format:

```
VNode://<Group>/<Folder?...>/File.xml
```

`Folder` represents a chain of subdirectories (up to 255 segments) to reach the
desired file.

---

## Permissions

Permissions are documented separately in
[`VNode_Permissions.md`](./VNode_Permissions.md). The permission model covers
`.perms` files, **look / speak / run** attributes, identity binding, and
authentication.

---

## Relay Server

VNode can be configured as a relay server to offload network traffic for multiplayer
games (e.g., EcoVR). Instead of clients connecting directly to the host, they
connect to the relay, which forwards the data — reducing the burden on the host
and ensuring a smoother experience when the host has an unstable or bandwidth-limited
connection.

The relay is built on the **V12 transport layer**: it uses `NetworkHost` to accept
client connections and `NetworkCables` to route messages between clients and the
origin server. For the complete transport protocol (wire format, message types,
threading model), see [`NETWORKING_ARCHITECTURE.md`](./NETWORKING_ARCHITECTURE.md).

---

## Advanced Usage

- **Networked file server** — VNode can be mounted as a filesystem via a custom
  Linux driver.
- **Content addressing** — Files are SHA-256 hashed and addressable under
  `VNode://Hashes/<hash>`, enabling deduplication and integrity verification.

---

## Related Documents

- [`NETWORKING_ARCHITECTURE.md`](./NETWORKING_ARCHITECTURE.md) — V12 transport layer
  (TCP, message bus, wire protocol, SyncManager, DirtyTracker, threading)
- [`VNode_Permissions.md`](./VNode_Permissions.md) — Permission model, `.perms`
  files, attributes, identity
