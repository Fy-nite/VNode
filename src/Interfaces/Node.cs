namespace VNode.Interfaces
{
    /// <summary>
    /// Base interface for all VNode filesystem entries.
    /// Every item in a VNode tree (branches and leaves) implements this.
    /// </summary>
    public interface INode
    {
        /// <summary>Display name of this node (filename or folder name).</summary>
        string Name { get; }

        /// <summary>Full VNode:// path from the group root to this node.</summary>
        string Path { get; }

        /// <summary>Unix-style last-modified timestamp.</summary>
        long LastModified { get; }

        /// <summary>Whether this node is a branch (directory) or leaf (file).</summary>
        bool IsDirectory { get; }
    }
}
