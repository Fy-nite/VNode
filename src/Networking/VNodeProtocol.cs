using System.Text.Json;

namespace VNode.Networking
{
    /// <summary>
    /// Request sent from client to VNode server.
    /// Wire format: 4-byte little-endian length prefix + JSON body.
    /// </summary>
    public class VNodeRequest
    {
        public VNodeRequestOp Op { get; set; }
        public string Path { get; set; } = "";
        public byte[]? Data { get; set; }
        public string RequestId { get; set; } = Guid.NewGuid().ToString("N")[..8];

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public byte[] Serialize()
        {
            // For Read/Write ops we send metadata + data separately
            var meta = new RequestMeta
            {
                Op = Op,
                Path = Path,
                RequestId = RequestId,
                DataLength = Data?.Length ?? 0
            };
            var metaBytes = JsonSerializer.SerializeToUtf8Bytes(meta, JsonOpts);
            var lengthBytes = BitConverter.GetBytes(metaBytes.Length);

            var result = new byte[4 + metaBytes.Length + (Data?.Length ?? 0)];
            Buffer.BlockCopy(lengthBytes, 0, result, 0, 4);
            Buffer.BlockCopy(metaBytes, 0, result, 4, metaBytes.Length);
            if (Data != null)
                Buffer.BlockCopy(Data, 0, result, 4 + metaBytes.Length, Data.Length);

            return result;
        }

        public static VNodeRequest Deserialize(byte[] lengthPrefix, Stream? bodyStream = null)
        {
            var metaLen = BitConverter.ToInt32(lengthPrefix, 0);
            return new VNodeRequest { Op = VNodeRequestOp.None };
        }

        private class RequestMeta
        {
            public VNodeRequestOp Op { get; set; }
            public string Path { get; set; } = "";
            public string RequestId { get; set; } = "";
            public int DataLength { get; set; }
        }
    }

    /// <summary>
    /// Response sent from VNode server back to client.
    /// </summary>
    public class VNodeResponse
    {
        public bool Success { get; set; }
        public string RequestId { get; set; } = "";
        public string? Error { get; set; }
        public byte[]? Data { get; set; }
        public VNodeEntry[]? Entries { get; set; }

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public byte[] Serialize()
        {
            var meta = new ResponseMeta
            {
                Success = Success,
                RequestId = RequestId,
                Error = Error,
                DataLength = Data?.Length ?? 0,
                Entries = Entries
            };
            var metaBytes = JsonSerializer.SerializeToUtf8Bytes(meta, JsonOpts);
            var lengthBytes = BitConverter.GetBytes(metaBytes.Length);

            var result = new byte[4 + metaBytes.Length + (Data?.Length ?? 0)];
            Buffer.BlockCopy(lengthBytes, 0, result, 0, 4);
            Buffer.BlockCopy(metaBytes, 0, result, 4, metaBytes.Length);
            if (Data != null)
                Buffer.BlockCopy(Data, 0, result, 4 + metaBytes.Length, Data.Length);

            return result;
        }

        private class ResponseMeta
        {
            public bool Success { get; set; }
            public string RequestId { get; set; } = "";
            public string? Error { get; set; }
            public int DataLength { get; set; }
            public VNodeEntry[]? Entries { get; set; }
        }
    }

    public enum VNodeRequestOp
    {
        None = 0,
        Read,       // Read file content
        Write,      // Write file content
        Delete,     // Delete a file
        List,       // List directory contents
        Exists,     // Check if file/directory exists
        Stat,       // Get file metadata (size, modified, hash)
        Ping        // Health check
    }

    /// <summary>
    /// Metadata about a file or directory entry, sent in List responses.
    /// </summary>
    public class VNodeEntry
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public long LastModified { get; set; }
    }
}
