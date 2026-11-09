namespace VNode.Interfaces
{
    /// <summary>
    /// Pluggable storage backend for VNode.
    /// Implementations provide the actual read/write/list operations for a storage root.
    /// VNode ships with <see cref="Storage.LocalStorageProvider"/> (disk) and
    /// can be extended with cloud, database, or in-memory backends.
    /// </summary>
    public interface IStorageProvider
    {
        /// <summary>Human-readable name for this provider (e.g. "local", "s3", "memory").</summary>
        string ProviderName { get; }

        /// <summary>Open a readable stream to the file at the given absolute path.</summary>
        Stream Open(string path);

        /// <summary>Write data from a stream to the given path, creating parent directories as needed.</summary>
        void Save(string path, Stream data);

        /// <summary>Write raw bytes to the given path.</summary>
        void Save(string path, byte[] data);

        /// <summary>Delete the file at the given path.</summary>
        bool Delete(string path);

        /// <summary>Check whether a file exists at the given path.</summary>
        bool Exists(string path);

        /// <summary>Check whether a directory exists at the given path.</summary>
        bool DirectoryExists(string path);

        /// <summary>List immediate children (files and subdirectories) of the given directory path.</summary>
        IEnumerable<Interfaces.INode> List(string directoryPath);

        /// <summary>Get the last-modified timestamp of a file (Unix seconds).</summary>
        long GetLastModified(string path);

        /// <summary>Get the file size in bytes, or -1 if unknown.</summary>
        long GetSize(string path);
    }
}
