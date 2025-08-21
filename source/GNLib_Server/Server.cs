using GameNet.Common;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Security;

namespace GameNetServer
{
    public sealed class Server : IDisposable
    {
        private readonly ServerOptions _opt;
        private TcpListener _listener;
        private readonly ConcurrentDictionary<Guid, ClientSession> _clients = new ConcurrentDictionary<Guid, ClientSession>();
        private CancellationTokenSource _cts;

        // Events
        public event Action<Guid> ClientConnected;
        public event Action<Guid> ClientDisconnected;
        public event Action<Guid, MessageType, byte[]> DataReceived;

        public Server(ServerOptions options)
        {
            _opt = options ?? throw new ArgumentNullException(nameof(options));
            if (_opt.UseTls && _opt.ServerCertificate == null && !_opt.AllowInsecureForTesting)
                throw new InvalidOperationException("TLS enabled but no ServerCertificate provided.");
        }

        public async Task StartAsync()
        {
            if (_listener != null) throw new InvalidOperationException("Already started.");
            _cts = new CancellationTokenSource();

            _listener = new TcpListener(_opt.BindAddress, _opt.Port);
            _listener.Start();
            Console.WriteLine($"[Server] Listening on {_opt.BindAddress}:{_opt.Port} (TLS={_opt.UseTls}, InsecureForTesting={_opt.AllowInsecureForTesting})");

            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
            await Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (_listener == null) return;
            _cts.Cancel();
            _listener.Stop();
            _listener = null;

            foreach (var kv in _clients)
                await kv.Value.StopAsync().ConfigureAwait(false);

            _clients.Clear();
            Console.WriteLine("[Server] Stopped.");
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpClient tcp;
                    try
                    {
                        tcp = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException) { break; } // listener stopped
                    catch (Exception ex)
                    {
                        Console.WriteLine("[Server] Accept error: " + ex.Message);
                        continue;
                    }

                    _ = Task.Run(() => HandleClientAsync(tcp, ct));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Server] Accept loop crashed: " + ex);
            }
        }

        private async Task HandleClientAsync(TcpClient tcp, CancellationToken serverCt)
        {
            var id = Guid.NewGuid();
            Console.WriteLine($"[Server] Client connected: {id}");

            NetworkStream netStream = null;
            SslStream ssl = null;
            Stream stream = null;

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
            var session = new ClientSession(id, this, linkedCts);

            try
            {
                _clients[id] = session;
                ClientConnected?.Invoke(id);

                netStream = tcp.GetStream();

                if (_opt.UseTls && !_opt.AllowInsecureForTesting)
                {
                    ssl = new SslStream(netStream, false);
                    await ssl.AuthenticateAsServerAsync(_opt.ServerCertificate, clientCertificateRequired: false,
                        enabledSslProtocols: SslProtocols.Tls12, checkCertificateRevocation: true
                    ).ConfigureAwait(false);

                    stream = ssl;
                }
                else
                {
                    stream = netStream;
                    Console.WriteLine("[Server] WARNING: Insecure (non-TLS) session allowed for testing.");
                }

                await session.RunAsync(stream, tcp, _opt).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] Session error ({id}): {ex.Message}");
            }
            finally
            {
                await session.StopAsync().ConfigureAwait(false);
                ssl?.Dispose();
                netStream?.Dispose();
                tcp.Close();

                _clients.TryRemove(id, out _);
                ClientDisconnected?.Invoke(id);
                Console.WriteLine($"[Server] Client disconnected: {id}");
            }
        }

        internal void RaiseData(Guid id, MessageType type, byte[] payload)
            => DataReceived?.Invoke(id, type, payload);

        public async Task SendAsync(Guid clientId, MessageType type, byte[] payload)
        {
            if (_clients.TryGetValue(clientId, out var session))
            {
                if (payload.Length <= Protocol.MaxMessageSize)
                { await session.SendAsync(type, payload).ConfigureAwait(false); }
                else
                {
                    MemoryStream source = new MemoryStream(payload);
                    await session.SendLargeAsync(type, source, payload.Length); 
                }
            }
        }

        public async Task BroadcastAsync(MessageType type, byte[] payload)
        {
            if (payload.Length <= Protocol.MaxMessageSize)
            {
                foreach (var kv in _clients)
                {
                    try { await kv.Value.SendAsync(type, payload).ConfigureAwait(false); }
                    catch { /* ignore per-client send errors */ }
                }
            }
            else
            {
                MemoryStream source = new MemoryStream(payload);

                foreach (var kv in _clients)
                {
                    try { await kv.Value.SendLargeAsync(type, source, payload.Length).ConfigureAwait(false); }
                    catch { /* ignore per-client send errors */ }
                }
            }
        }

        public void Dispose() => _cts?.Cancel();
    }
}
