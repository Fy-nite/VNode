# VNode Permissions

> **Last updated:** 2026-07-09
>
> This document describes the VNode permission model: `.perms` files, permission entities,
> identity binding, and attribute semantics. For networking and transport details, see
> [`VNode_Networking.md`](./VNode_Networking.md).

---

## Overview

VNode supports **permission entities** that control access to groups, branches (folders),
and leaves (files). These are similar in spirit to Linux RWX permissions but extended
with VNode-specific attributes: **look**, **speak**, and **run**.

Permission entities allow developers, world creators, or hosting providers to:

- Lock off or disallow access to certain paths in the VNode.
- Disallow execution of Lua scripts from the filesystem.
- Remove or limit people's ability to read other folders or groups in the system.

---

## Permission Entities

A folder can be owned by one or more permission entities. Each entity's owner can be:

| Identity Type | Example |
|---------------|---------|
| Authenticated user | A logged-in user account |
| Group | A named collection of users |
| Hardware | A device or machine identity |
| Software | A software controlled service, EG cron jobs, systemd 
---

## .perms File

Permission entities are stored in a **`.perms`** file located in the folder where the
item is stored.

### Format 


```jsonc
{
  "entries": [
    {
      "path": "relative/path",
      "hash": "sha256-of-file",
      "owner": "user|group|hardware-id",
      "attributes": {
        "look": true,
        "speak": false,
        "run": false,
        // custom attributes
      }
    }
  ]
}
```

---

## Attributes

| Attribute | Analog | Description |
|-----------|--------|-------------|
| **look** | Read (`r`) | Ability to view/list a file or its metadata |
| **speak** | Write (`w`) | Ability to modify or delete a file |
| **run** | Execute (`x`) | Ability to execute a file (e.g. Lua scripts) |

Custom attributes can also be applied to files beyond the standard three.

File identification for permission entries uses the **SHA-256 hash** of the file
(via content addressing, `VNode://Hashes/<hash>`).

---

## Identity & Authentication (TBD)

for information about the networking identity and auth, please check out [`VSystem_Auth.md`](./VSystem_Auth.md)

---

## Related Documents

- [`VNode_Networking.md`](./VNode_Networking.md) — URI scheme, ports, relay server
- [`NETWORKING_ARCHITECTURE.md`](./NETWORKING_ARCHITECTURE.md) — V12 transport layer
