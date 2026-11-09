using VNode.Interfaces;

namespace VNode.FileSystem
{
    /// <summary>
    /// A branch represents a directory in the VNode tree.
    /// Branches contain child branches (subdirectories) and leaves (files).
    /// </summary>
    public class Branch : INode
    {
        private readonly IStorageProvider _storage;
        private readonly List<INode> _children = new();

        public string Name { get; }
        public string Path { get; }
        public long LastModified { get; }
        public bool IsDirectory => true;

        /// <summary>All child nodes (branches and leaves).</summary>
        public IReadOnlyList<INode> Children => _children;

        public Branch(string name, string path, long lastModified, IStorageProvider storage)
        {
            Name = name;
            Path = path;
            LastModified = lastModified;
            _storage = storage;
        }

        /// <summary>List immediate children from the storage provider.</summary>
        public void Refresh()
        {
            _children.Clear();
            _children.AddRange(_storage.List(Path));
        }

        /// <summary>Find a child by name (case-insensitive).</summary>
        public INode? Find(string name)
        {
            return _children.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Navigate into a sub-path relative to this branch.</summary>
        public INode? Navigate(string relativePath)
        {
            var segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            INode current = this;

            foreach (var segment in segments)
            {
                if (current is not Branch branch)
                    return null;

                var child = branch.Find(segment);
                if (child == null)
                    return null;
                current = child;
            }

            return current;
        }

        public override string ToString() => $"Branch({Path}, {_children.Count} children)";
    }
}
