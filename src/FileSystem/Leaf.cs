using VNode.Interfaces;

namespace VNode.FileSystem
{
    /// <summary>
    /// A leaf represents a file in the VNode tree.
    /// Leaves hold a reference to their storage provider for lazy content access.
    /// </summary>
    public class Leaf : INode
    {
        private readonly IStorageProvider _storage;

        public string Name { get; }
        public string Path { get; }
        public long LastModified { get; }
        public bool IsDirectory => false;

        /// <summary>Size in bytes, populated on listing.</summary>
        public long Size { get; }

        /// <summary>SHA-256 content hash, computed on read or listing.</summary>
        public string? ContentHash { get; internal set; }

        public Leaf(string name, string path, long lastModified, long size, IStorageProvider storage)
        {
            Name = name;
            Path = path;
            LastModified = lastModified;
            Size = size;
            _storage = storage;
        }

        /// <summary>Open a readable stream to this leaf's content.</summary>
        public Stream OpenRead() => _storage.Open(Path);

        /// <summary>Read the full content as a byte array.</summary>
        public byte[] ReadAll()
        {
            using var stream = _storage.Open(Path);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        /// <summary>Read the full content as text.</summary>
        public string ReadAllText()
        {
            using var stream = _storage.Open(Path);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public override string ToString() => $"Leaf({Path}, {Size} bytes)";
    }
}
