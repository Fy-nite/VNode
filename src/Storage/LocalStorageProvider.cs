using VNode.Interfaces;

namespace VNode.Storage
{
    /// <summary>
    /// Local disk storage provider for VNode.
    /// Maps VNode paths to a directory on the local filesystem.
    /// 
    /// Configuration:
    ///   RootPath = "/var/vnode" or "C:\vnode-data"
    ///   Groups map to subdirectories: {RootPath}/{Group}/...
    /// </summary>
    public class LocalStorageProvider : IStorageProvider
    {
        public string ProviderName => "local";
        public string RootPath { get; }

        public LocalStorageProvider(string rootPath)
        {
            RootPath = Path.GetFullPath(rootPath);
            if (!Directory.Exists(RootPath))
                Directory.CreateDirectory(RootPath);
        }

        public Stream Open(string path)
        {
            var fullPath = ResolvePath(path);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"VNode leaf not found: {path}", fullPath);
            return File.OpenRead(fullPath);
        }

        public void Save(string path, Stream data)
        {
            var fullPath = ResolvePath(path);
            var dir = Path.GetDirectoryName(fullPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var fileStream = File.Create(fullPath);
            data.CopyTo(fileStream);
        }

        public void Save(string path, byte[] data)
        {
            var fullPath = ResolvePath(path);
            var dir = Path.GetDirectoryName(fullPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(fullPath, data);
        }

        public bool Delete(string path)
        {
            var fullPath = ResolvePath(path);
            if (!File.Exists(fullPath)) return false;
            File.Delete(fullPath);
            return true;
        }

        public bool Exists(string path)
        {
            var fullPath = ResolvePath(path);
            return File.Exists(fullPath);
        }

        public bool DirectoryExists(string path)
        {
            var fullPath = ResolvePath(path);
            return Directory.Exists(fullPath);
        }

        public IEnumerable<INode> List(string directoryPath)
        {
            var fullPath = ResolvePath(directoryPath);
            if (!Directory.Exists(fullPath))
                yield break;

            // List subdirectories (branches)
            foreach (var dir in Directory.GetDirectories(fullPath))
            {
                var info = new DirectoryInfo(dir);
                var relativePath = GetRelativePath(fullPath, dir).Replace('\\', '/');
                yield return new FileSystem.Branch(
                    info.Name,
                    relativePath,
                    new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
                    this
                );
            }

            // List files (leaves)
            foreach (var file in Directory.GetFiles(fullPath))
            {
                var info = new FileInfo(file);
                var relativePath = GetRelativePath(fullPath, file).Replace('\\', '/');
                yield return new FileSystem.Leaf(
                    info.Name,
                    relativePath,
                    new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
                    info.Length,
                    this
                );
            }
        }

        public long GetLastModified(string path)
        {
            var fullPath = ResolvePath(path);
            if (!File.Exists(fullPath)) return 0;
            return new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath)).ToUnixTimeSeconds();
        }

        public long GetSize(string path)
        {
            var fullPath = ResolvePath(path);
            if (!File.Exists(fullPath)) return -1;
            return new FileInfo(fullPath).Length;
        }

        /// <summary>
        /// Compute SHA-256 hash of a file's content.
        /// </summary>
        public static string ComputeHash(string path)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private string ResolvePath(string vnodePath)
        {
            // vnodePath is relative to the group, e.g. "docs/pages/home.xml"
            // or it could be a full VNode:// URI — strip scheme if present
            var cleaned = vnodePath
                .Replace("VNode://", "")
                .Replace("vnode://", "")
                .Replace("v12://", "");

            // If it starts with a group name, strip it (groups map to subdirs)
            // For simplicity, treat the whole path as relative to RootPath
            return Path.Combine(RootPath, cleaned.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar))
                basePath += Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                return fullPath[basePath.Length..];
            return fullPath;
        }
    }
}
