namespace VNode.Interfaces
{
    /// <summary>
    /// A single permission entry controlling access to a VNode path.
    /// </summary>
    public class PermEntry
    {
        /// <summary>Relative path this entry applies to.</summary>
        public string Path { get; set; } = "";

        /// <summary>SHA-256 content hash of the file (for integrity verification).</summary>
        public string Hash { get; set; } = "";

        /// <summary>Owner identity: "user", "group", or a hardware/software ID.</summary>
        public string Owner { get; set; } = "";

        /// <summary>Access attributes (look/speak/run and custom).</summary>
        public Dictionary<string, bool> Attributes { get; set; } = new();
    }

    /// <summary>
    /// A .perms file manifest containing permission entries for a directory.
    /// </summary>
    public class PermManifest
    {
        /// <summary>All permission entries in this manifest.</summary>
        public List<PermEntry> Entries { get; set; } = new();

        /// <summary>Base path this manifest applies to (redundant but useful for validation).</summary>
        public string BasePath { get; set; } = string.Empty;
    }
}
