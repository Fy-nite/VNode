# VNode Storage networking

> **Last updated:** 2026-07-09
>
> This document provides information about how VNode does storage networking.

unlike proper networking solutions that provide only one type of storage solution, VNode supports a practicly unlimited set of storage solutions via our custom
IStorage solution

```cs
public interface IStorageProvider
{
    Stream Open(...);

    void Save(...);

    bool Exists(...);

    IEnumerable<Entry> List(...);
}
```