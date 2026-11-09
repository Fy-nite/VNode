namespace VNode.Networking
{
    /// <summary>
    /// Parses and constructs VNode:// URIs.
    /// Format: VNode://Group/Folder/.../File.ext
    /// 
    /// Examples:
    ///   VNode://wiki/docs/pages/home.xml
    ///   VNode://Hashes/a1b2c3d4...
    ///   VNode://mygroup/data/config.json
    /// </summary>
    public class VNodePath
    {
        private const string SCHEME = "VNode://";

        /// <summary>The group (top-level namespace) this path belongs to.</summary>
        public string Group { get; }

        /// <summary>The full relative path within the group (including filename).</summary>
        public string RelativePath { get; }

        /// <summary>The filename (last segment) of the path.</summary>
        public string FileName { get; }

        /// <summary>The parent directory path (everything except the filename).</summary>
        public string ParentPath { get; }

        /// <summary>The full VNode:// URI string.</summary>
        public string FullUri { get; }

        public VNodePath(string group, string relativePath)
        {
            Group = group;
            RelativePath = relativePath.Replace('\\', '/').Trim('/');
            FullUri = $"{SCHEME}{Group}/{RelativePath}";

            var lastSlash = RelativePath.LastIndexOf('/');
            FileName = lastSlash >= 0 ? RelativePath[(lastSlash + 1)..] : RelativePath;
            ParentPath = lastSlash >= 0 ? RelativePath[..lastSlash] : "";
        }

        /// <summary>
        /// Parse a VNode:// URI string into a VNodePath.
        /// Also accepts plain relative paths like "wiki/docs/pages/home.xml" (group defaults to "default").
        /// </summary>
        public static VNodePath Parse(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
                throw new ArgumentException("VNode path cannot be empty.");

            var path = uri;

            // Strip scheme prefix (case-insensitive)
            if (path.StartsWith(SCHEME, StringComparison.OrdinalIgnoreCase))
                path = path[SCHEME.Length..];

            // Split into group + relative
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                throw new ArgumentException($"Invalid VNode path: {uri}");

            var group = segments[0];
            var relative = segments.Length > 1 ? string.Join('/', segments[1..]) : "";

            return new VNodePath(group, relative);
        }

        /// <summary>Try to parse a VNode path without throwing.</summary>
        public static bool TryParse(string uri, out VNodePath? result)
        {
            try
            {
                result = Parse(uri);
                return true;
            }
            catch
            {
                result = null;
                return false;
            }
        }

        /// <summary>Create a child path under this path.</summary>
        public VNodePath Child(string childName)
        {
            var relative = string.IsNullOrEmpty(RelativePath)
                ? childName
                : $"{RelativePath}/{childName}";
            return new VNodePath(Group, relative);
        }

        /// <summary>Create a path pointing to the parent directory.</summary>
        public VNodePath? Parent()
        {
            if (string.IsNullOrEmpty(ParentPath))
                return null;
            return new VNodePath(Group, ParentPath);
        }

        /// <summary>Convert to storage-local path (relative to a storage root).</summary>
        public string ToLocalPath()
        {
            return RelativePath;
        }

        public override string ToString() => FullUri;
        public override bool Equals(object? obj) =>
            obj is VNodePath other && string.Equals(FullUri, other.FullUri, StringComparison.OrdinalIgnoreCase);
        public override int GetHashCode() => FullUri.ToLowerInvariant().GetHashCode();
    }
}
