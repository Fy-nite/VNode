using VNode.Interfaces;

namespace VNode.Storage
{
    /// <summary>
    /// In-memory storage provider for VNode.
    /// Stores all data in a Dictionary — useful for testing, ephemeral data, and hot-reload.
    /// Does not persist across restarts.
    /// </summary>
    public class InMemoryStorageProvider : IStorageProvider
    {
        public string ProviderName => "memory";

        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _timestamps = new(StringComparer.OrdinalIgnoreCase);

        public Stream Open(string path)
        {
            var key = NormalizeKey(path);
            if (!_files.TryGetValue(key, out var data))
                throw new FileNotFoundException($"VNode leaf not found: {path}");
            return new MemoryStream(data, writable: false);
        }

        public void Save(string path, Stream data)
        {
            var key = NormalizeKey(path);
            using var ms = new MemoryStream();
            data.CopyTo(ms);
            _files[key] = ms.ToArray();
            _timestamps[key] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public void Save(string path, byte[] data)
        {
            var key = NormalizeKey(path);
            _files[key] = data;
            _timestamps[key] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public bool Delete(string path)
        {
            var key = NormalizeKey(path);
            _timestamps.Remove(key);
            return _files.Remove(key);
        }

        public bool Exists(string path)
        {
            return _files.ContainsKey(NormalizeKey(path));
        }

        public bool DirectoryExists(string path)
        {
            var prefix = NormalizeKey(path);
            if (string.IsNullOrEmpty(prefix)) return _files.Count > 0;
            return _files.Keys.Any(k => k.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<INode> List(string directoryPath)
        {
            var prefix = NormalizeKey(directoryPath);
            if (!string.IsNullOrEmpty(prefix)) prefix += "/";

            var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            foreach (var key in _files.Keys)
            {
                if (!string.IsNullOrEmpty(prefix) && !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var remainder = string.IsNullOrEmpty(prefix)
                    ? key
                    : key[prefix.Length..];

                var slashIdx = remainder.IndexOf('/');
                if (slashIdx >= 0)
                {
                    // It's in a subdirectory — yield the directory once
                    var dirName = remainder[..slashIdx];
                    if (seenDirs.Add(dirName))
                    {
                        var dirPath = prefix + dirName;
                        yield return new FileSystem.Branch(
                            dirName,
                            dirPath,
                            now,
                            this
                        );
                    }
                }
                else
                {
                    // It's a file in this directory
                    var info = _files[key];
                    var lastMod = _timestamps.TryGetValue(key, out var ts) ? ts : now;
                    yield return new FileSystem.Leaf(
                        remainder,
                        key,
                        lastMod,
                        info.Length,
                        this
                    );
                }
            }
        }

        public long GetLastModified(string path)
        {
            var key = NormalizeKey(path);
            return _timestamps.TryGetValue(key, out var ts) ? ts : 0;
        }

        public long GetSize(string path)
        {
            var key = NormalizeKey(path);
            return _files.TryGetValue(key, out var data) ? data.Length : -1;
        }

        /// <summary>List all keys currently stored (for debugging).</summary>
        public IReadOnlyCollection<string> GetAllKeys() => _files.Keys.ToList().AsReadOnly();

        private static string NormalizeKey(string path)
        {
            return path
                .Replace("VNode://", "")
                .Replace("vnode://", "")
                .Replace("v12://", "")
                .Replace('\\', '/')
                .Trim('/');
        }
    }
}
