namespace VNode
{
    /// <summary>
    /// VNode global constants and defaults.
    /// </summary>
    public static class G
    {
        /// <summary>Default TCP port for VNode asset serving.</summary>
        public const int DefaultPort = 2560;

        /// <summary>Default UDP port for relay server.</summary>
        public const int DefaultRelayPort = 2565;

        /// <summary>VNode URI scheme prefix.</summary>
        public const string Scheme = "VNode://";

        /// <summary>Maximum metadata size in bytes (1 MB).</summary>
        public const int MaxMetadataSize = 1024 * 1024;

        /// <summary>Maximum file size for a single transfer (100 MB).</summary>
        public const long MaxFileSize = 100 * 1024 * 1024;
    }
}
