using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using VNode.Interfaces;

namespace VNode.Networking
{
    /// <summary>
    /// VNode TCP server — listens on port 2560 and serves file operations over the network.
    /// Wire protocol: 4-byte little-endian length prefix + JSON metadata + optional binary payload.
    /// 
    /// Supports: Read, Write, Delete, List, Exists, Stat, Ping.
    /// Storage is pluggable via <see cref="IStorageProvider"/>.
    /// </summary>
    public class VNodeServer : IDisposable
    {
        private readonly IStorageProvider _storage;
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _acceptTask;
        private readonly List<VNodeClientHandler> _clients = new();
        private readonly object _lock = new();

        public int Port { get; }
        public bool IsRunning { get; private set; }
        public int ClientCount { get { lock (_lock) return _clients.Count; } }

        public VNodeServer(IStorageProvider storage, int port = 2560)
        {
            _storage = storage;
            Port = port;
        }

        /// <summary>Start accepting TCP connections.</summary>
        public void Start()
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            IsRunning = true;
            _acceptTask = AcceptLoop(_cts.Token);
            Console.WriteLine($"[VNode] Server listening on port {Port} (storage: {_storage.ProviderName})");
        }

        /// <summary>Stop the server and disconnect all clients.</summary>
        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            IsRunning = false;

            lock (_lock)
            {
                foreach (var client in _clients)
                    client.Dispose();
                _clients.Clear();
            }

            Console.WriteLine("[VNode] Server stopped.");
        }

        public void Dispose() => Stop();

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await _listener!.AcceptTcpClientAsync(ct);
                    var handler = new VNodeClientHandler(tcpClient, _storage);
                    handler.OnDisconnected += OnClientDisconnected;

                    lock (_lock)
                        _clients.Add(handler);

                    Console.WriteLine($"[VNode] Client connected: {tcpClient.Client.RemoteEndPoint}");
                    _ = handler.HandleLoop(ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[VNode] Accept error: {ex.Message}");
                }
            }
        }

        private void OnClientDisconnected(VNodeClientHandler handler)
        {
            handler.OnDisconnected -= OnClientDisconnected;
            lock (_lock)
                _clients.Remove(handler);
            Console.WriteLine($"[VNode] Client disconnected ({ClientCount} active)");
        }
    }

    /// <summary>
    /// Handles a single connected VNode client.
    /// Reads length-prefixed requests, dispatches to storage, sends responses.
    /// </summary>
    internal class VNodeClientHandler : IDisposable
    {
        private readonly TcpClient _tcp;
        private readonly NetworkStream _stream;
        private readonly IStorageProvider _storage;
        private bool _disposed;

        public event Action<VNodeClientHandler>? OnDisconnected;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            Console.WriteLine(line);
            try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "vnode-debug.log"), line + "\n"); } catch { }
        }

        public VNodeClientHandler(TcpClient tcp, IStorageProvider storage)
        {
            _tcp = tcp;
            _stream = tcp.GetStream();
            _storage = storage;
        }

        public async Task HandleLoop(CancellationToken ct)
        {
            try
            {
                Log($"HandleLoop started for {_tcp.Client.RemoteEndPoint}");
                while (!ct.IsCancellationRequested && _tcp.Connected)
                {
                    var lengthBytes = await ReadExactAsync(4, ct);
                    if (lengthBytes == null) { Log("ReadExact(4) returned null"); break; }

                    var metaLen = BitConverter.ToInt32(lengthBytes, 0);
                    Log($"raw len bytes: {BitConverter.ToString(lengthBytes)}, metaLen={metaLen}");
                    if (metaLen <= 0 || metaLen > 1024 * 1024) { Log($"Invalid metaLen: {metaLen}"); break; }

                    var metaBytes = await ReadExactAsync(metaLen, ct);
                    if (metaBytes == null) { Log("ReadExact(meta) returned null"); break; }

                    var metaJson = System.Text.Encoding.UTF8.GetString(metaBytes);
                    Log($"Received: {metaJson}");
                    var meta = JsonSerializer.Deserialize<RequestMeta>(metaBytes, JsonOpts);
                    if (meta == null) { Log("Deserialize returned null"); break; }

                    byte[]? payload = null;
                    if (meta.DataLength > 0)
                    {
                        payload = await ReadExactAsync(meta.DataLength, ct);
                        if (payload == null) { Log("ReadExact(payload) returned null"); break; }
                    }

                    var response = await Dispatch(meta, payload);
                    var respBytes = response.Serialize();
                    Log($"Sending {respBytes.Length} byte response, success={response.Success}");
                    await _stream.WriteAsync(respBytes, ct);
                    await _stream.FlushAsync(ct);
                    Log("Response sent OK");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log($"ERROR: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                Log("HandleLoop exiting");
                OnDisconnected?.Invoke(this);
            }
        }

        private async Task<VNodeResponse> Dispatch(RequestMeta meta, byte[]? payload)
        {
            var resp = new VNodeResponse { RequestId = meta.RequestId };

            try
            {
                switch (meta.Op)
                {
                    case "read":
                        if (!_storage.Exists(meta.Path))
                        {
                            resp.Success = false;
                            resp.Error = $"Not found: {meta.Path}";
                        }
                        else
                        {
                            using (var stream = _storage.Open(meta.Path))
                            using (var ms = new MemoryStream())
                            {
                                await stream.CopyToAsync(ms);
                                resp.Data = ms.ToArray();
                            }
                            resp.Success = true;
                        }
                        break;

                    case "write":
                        if (payload == null)
                        {
                            resp.Success = false;
                            resp.Error = "No data payload";
                        }
                        else
                        {
                            _storage.Save(meta.Path, payload);
                            resp.Success = true;
                        }
                        break;

                    case "delete":
                        resp.Success = _storage.Delete(meta.Path);
                        if (!resp.Success)
                            resp.Error = $"Not found: {meta.Path}";
                        break;

                    case "list":
                        var entries = _storage.List(meta.Path)
                            .Select(n => new VNodeEntry
                            {
                                Name = n.Name,
                                Path = n.Path,
                                IsDirectory = n.IsDirectory,
                                LastModified = n.LastModified
                            })
                            .ToArray();
                        resp.Entries = entries;
                        resp.Success = true;
                        break;

                    case "exists":
                        resp.Success = _storage.Exists(meta.Path) || _storage.DirectoryExists(meta.Path);
                        break;

                    case "stat":
                        if (!_storage.Exists(meta.Path))
                        {
                            resp.Success = false;
                            resp.Error = $"Not found: {meta.Path}";
                        }
                        else
                        {
                            resp.Entries = new[]
                            {
                                new VNodeEntry
                                {
                                    Path = meta.Path,
                                    Size = _storage.GetSize(meta.Path),
                                    LastModified = _storage.GetLastModified(meta.Path)
                                }
                            };
                            resp.Success = true;
                        }
                        break;

                    case "ping":
                        resp.Success = true;
                        resp.Data = System.Text.Encoding.UTF8.GetBytes("pong");
                        break;

                    default:
                        resp.Success = false;
                        resp.Error = $"Unknown op: {meta.Op}";
                        break;
                }
            }
            catch (Exception ex)
            {
                resp.Success = false;
                resp.Error = ex.Message;
            }

            return resp;
        }

        private async Task<byte[]?> ReadExactAsync(int count, CancellationToken ct)
        {
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

        private class RequestMeta
        {
            public string Op { get; set; } = "";
            public string Path { get; set; } = "";
            public string RequestId { get; set; } = "";
            public int DataLength { get; set; }
        }
    }
}
