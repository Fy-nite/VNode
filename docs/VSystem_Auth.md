# VSystem Authentication

> **Last updated:** 2026-07-09
>
> This document describes the VSystem authentication model for VNode, V12, and
> other V-system services. For transport details, see
> [`NETWORKING_ARCHITECTURE.md`](./NETWORKING_ARCHITECTURE.md). For the permission
> model and identity binding, see [`VNode_Permissions.md`](./VNode_Permissions.md).

## Identity & Authentication

Like all networked applications, VSystem services (VNode, V12) use authentication
to provide personal user accounts, world hosting access, and file storage/sharing.

- **User-level authentication** — Standard account-based login for users.
- **Administrative authentication** — Uses key pairs generated via `ssh-keygen`
  (e.g., RSA, ECDSA, Ed25519). The public key is registered with the node; the
  private key remains on the client. This authenticates a client for administrative
  access to any hosted world on that node.