using System.Net.Sockets;
using System.Text.Json;

namespace VNode.Networking
{
    /// <summary>
    /// TCP client for connecting to a VNode server.
    /// Sends VNode:// path-based requests and receives responses.
    /// 
    /// Usage:
    ///   var client = new VNodeClient("127.0.0.1", 2560);
    ///   await client.ConnectAsync();
    ///   var data = await client.ReadAsync("wiki/docs/pages/home.xml");
    ///   var entries = await client.ListAsync("wiki/docs/pages");
    /// </summary>
    public class VNodeClient : IDisposable
    {
        private TcpClient? _tcp;
        private NetworkStream? _stream;
        private readonly string _host;
        private readonly int _port;
        private bool _disposed;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public bool IsConnected => _tcp?.Connected ?? false;

        public VNodeClient(string host = "127.0.0.1", int port = 2560)
        {
            _host = host;
            _port = port;
        }

        /// <summary>Connect to the VNode server.</summary>
        public async Task ConnectAsync(CancellationToken ct = default)
        {
            _tcp = new TcpClient();
            await _tcp.ConnectAsync(_host, _port, ct);
            _stream = _tcp.GetStream();
            Console.WriteLine($"[VNodeClient] Connected to {_host}:{_port}");
        }

        /// <summary>Read a file's content by VNode path.</summary>
        public async Task<byte[]?> ReadAsync(string path, CancellationToken ct = default)
        {
            var resp = await SendRequestAsync(new RequestPayload
            {
                Op = "read",
                Path = path
            }, ct);

            return resp.Success ? resp.Data : null;
        }

        /// <summary>Read a file as text.</summary>
        public async Task<string?> ReadTextAsync(string path, CancellationToken ct = default)
        {
            var data = await ReadAsync(path, ct);
            return data != null ? System.Text.Encoding.UTF8.GetString(data) : null;
        }

        /// <summary>Write data to a VNode path.</summary>
        public async Task<bool> WriteAsync(string path, byte[] data, CancellationToken ct = default)
        {
            var resp = await SendRequestAsync(new RequestPayload
            {
                Op = "write",
                Path = path,
                Data = data
            }, ct);

            return resp.Success;
        }

        /// <summary>Write text to a VNode path.</summary>
        public Task<bool> WriteTextAsync(string path, string text, CancellationToken ct = default)
        {
            return WriteAsync(path, System.Text.Encoding.UTF8.GetBytes(text), ct);
        }

        /// <summary>Delete a file.</summary>
        public async Task<bool> DeleteAsync(string path, CancellationToken ct = default)
        {
            var resp = await SendRequestAsync(new RequestPayload
            {
                Op = "delete",
                Path = path
            }, ct);

            return resp.Success;
        }

        /// <summary>List files and directories in a VNode path.</summary>
        public async Task<VNodeEntry[]> ListAsync(string path, CancellationToken ct = default)
        {
            var resp = await SendRequestAsync(new RequestPayload
            {
                Op = "list",
                Path = path
            }, ct);

            return resp.Entries ?? Array.Empty<VNodeEntry>();
        }

        /// <summary>Check if a file or directory exists.</summary>
        public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        {
            var resp = await SendRequestAsync(new RequestPayload
            {
                Op = "exists",
                Path = path
            }, ct);

            return resp.Success;
        }

        /// <summary>Get file metadata (size, last modified).</summary>
        public async Task<VNodeEntry?> StatAsync(string path, CancellationToken ct = default)
        {
            var resp = await SendRequestAsync(new RequestPayload
            {
                Op = "stat",
                Path = path
            }, ct);

            return resp.Entries?.FirstOrDefault();
        }

        /// <summary>Ping the server.</summary>
        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            var resp = await SendRequestAsync(new RequestPayload
            {
                Op = "ping"
            }, ct);

            return resp.Success;
        }

        private async Task<VNodeResponsePayload> SendRequestAsync(RequestPayload req, CancellationToken ct)
        {
            if (_stream == null || !IsConnected)
                throw new InvalidOperationException("Not connected. Call ConnectAsync() first.");

            var requestId = Guid.NewGuid().ToString("N")[..8];
            var meta = new RequestMeta
            {
                Op = req.Op,
                Path = req.Path ?? "",
                RequestId = requestId,
                DataLength = req.Data?.Length ?? 0
            };

            var metaBytes = JsonSerializer.SerializeToUtf8Bytes(meta, JsonOpts);
            var lengthBytes = BitConverter.GetBytes(metaBytes.Length);
            Console.WriteLine($"[VNodeClient] Sending: op={req.Op} path={req.Path} metaLen={metaBytes.Length} rawLen={BitConverter.ToString(lengthBytes)} json={System.Text.Encoding.UTF8.GetString(metaBytes)}");

            // Send length + metadata + optional data
            await _stream.WriteAsync(lengthBytes, ct);
            await _stream.WriteAsync(metaBytes, ct);
            if (req.Data != null)
                await _stream.WriteAsync(req.Data, ct);
            await _stream.FlushAsync(ct);

            // Read response length
            var respLengthBytes = await ReadExactAsync(4, ct);
            if (respLengthBytes == null)
                return new VNodeResponsePayload { Success = false, Error = "Connection closed" };

            var respMetaLen = BitConverter.ToInt32(respLengthBytes, 0);
            if (respMetaLen <= 0 || respMetaLen > 1024 * 1024)
                return new VNodeResponsePayload { Success = false, Error = "Invalid response length" };

            // Read response metadata
            var respMetaBytes = await ReadExactAsync(respMetaLen, ct);
            if (respMetaBytes == null)
                return new VNodeResponsePayload { Success = false, Error = "Connection closed" };

            var respMeta = JsonSerializer.Deserialize<ResponseMeta>(respMetaBytes, JsonOpts);
            if (respMeta == null)
                return new VNodeResponsePayload { Success = false, Error = "Invalid response" };

            // Read response data if present
            byte[]? respData = null;
            if (respMeta.DataLength > 0)
            {
                respData = await ReadExactAsync(respMeta.DataLength, ct);
            }

            return new VNodeResponsePayload
            {
                Success = respMeta.Success,
                Error = respMeta.Error,
                Data = respData,
                Entries = respMeta.Entries
            };
        }

        private async Task<byte[]?> ReadExactAsync(int count, CancellationToken ct)
        {
            if (_stream == null) return null;
            var buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = await _stream.ReadAsync(buffer[offset..(count - offset + offset)], ct);
                if (read == 0) return null;
                offset += read;
            }
            return buffer;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stream?.Dispose();
            _tcp?.Dispose();
        }

        private class RequestPayload
        {
            public string Op { get; set; } = "";
            public string? Path { get; set; }
            public byte[]? Data { get; set; }
        }

        private class RequestMeta
        {
            public string Op { get; set; } = "";
            public string Path { get; set; } = "";
            public string RequestId { get; set; } = "";
            public int DataLength { get; set; }
        }

        private class ResponseMeta
        {
            public bool Success { get; set; }
            public string? Error { get; set; }
            public int DataLength { get; set; }
            public VNodeEntry[]? Entries { get; set; }
        }

        private class VNodeResponsePayload
        {
            public bool Success { get; set; }
            public string? Error { get; set; }
            public byte[]? Data { get; set; }
            public VNodeEntry[]? Entries { get; set; }
        }
    }
}
