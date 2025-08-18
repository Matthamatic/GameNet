using GameNet.Common;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Security;

namespace GameNet.Server
{
    public sealed class ServerOptions
    {
        public IPAddress BindAddress { get; set; } = IPAddress.Any;
        public int Port { get; set; } = 9000;

        public bool UseTls { get; set; } = true;
        public X509Certificate2 ServerCertificate { get; set; }  // REQUIRED if UseTls
        public bool AllowInsecureForTesting { get; set; } = false;

        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(15);
    }

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
            foreach (var kv in _clients)
            {
                try { await kv.Value.SendAsync(type, payload).ConfigureAwait(false); }
                catch { /* ignore per-client send errors */ }
            }
        }

        public void Dispose() => _cts?.Cancel();
    }

    internal sealed class ClientSession
    {
        private readonly Guid _id;
        private readonly Server _server;
        private readonly CancellationTokenSource _cts;
        private Stream _stream;
        private TcpClient _tcp;
        private int _seqSend = 0;
        private DateTime _lastReceiveUtc = DateTime.UtcNow;

        public bool IsAuthenticated { get; private set; }

        private readonly FragmentReassembler _reassembler = new FragmentReassembler();

        public ClientSession(Guid id, Server server, CancellationTokenSource cts)
        {
            _id = id;
            _server = server;
            _cts = cts;
        }
        public async Task SendLargeAsync(MessageType type, Stream source, long totalLength, int chunkSize = 256 * 1024)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (totalLength < 0) throw new ArgumentOutOfRangeException(nameof(totalLength));
            if (chunkSize <= 0) chunkSize = 256 * 1024;

            // Ensure each frame <= MaxMessageSize
            chunkSize = Math.Min(chunkSize, Protocol.MaxFragmentData);

            var tid = Guid.NewGuid();
            var buf = new byte[chunkSize];
            long offset = 0;

            while (offset < totalLength)
            {
                int toRead = (int)Math.Min(buf.Length, totalLength - offset);
                int n = await source.ReadAsync(buf, 0, toRead, _cts.Token).ConfigureAwait(false);
                if (n <= 0) break;

                byte flags = Protocol.FlagFragment;
                if (offset + n >= totalLength) flags |= Protocol.FlagFragmentLast;

                var payload = Protocol.BuildFragmentPayload(tid, offset, totalLength, buf, n);
                await Protocol.SendAsync(_stream, type, payload, NextSeq(), flags, _cts.Token).ConfigureAwait(false);

                offset += n;
            }
        }


        public async Task RunAsync(Stream stream, TcpClient tcp, ServerOptions opt)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _tcp = tcp ?? throw new ArgumentNullException(nameof(tcp));

            // Kick off heartbeat
            _ = Task.Run(() => HeartbeatLoopAsync(opt, _cts.Token));

            // Receive loop
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var (type, flags, seq, payload) = await Protocol.ReceiveAsync(_stream, _cts.Token).ConfigureAwait(false);
                    _lastReceiveUtc = DateTime.UtcNow;

                    switch (type)
                    {
                        case MessageType.Ping:
                            await Protocol.SendAsync(_stream, MessageType.Pong, Array.Empty<byte>(), NextSeq(), Protocol.FlagNone, _cts.Token).ConfigureAwait(false);
                            break;

                        case MessageType.Pong:
                            // ignore; just a keepalive
                            break;

                        case MessageType.AuthRequest:
                            HandleAuthRequest(payload);
                            break;

                        case MessageType.RegisterRequest:
                            HandleRegisterRequest(payload);
                            break;

                        default:
                            if (!IsAuthenticated)
                            {
                                // Require auth before data
                                await CloseWithProtocolErrorAsync("Unauthenticated data").ConfigureAwait(false);
                                return;
                            }

                            if ((flags & Protocol.FlagFragment) != 0)
                            {
                                Protocol.ParseFragmentPayload(payload, out var tid, out long off, out long tot, out var chunkSeg);

                                // Add the piece; only raise once complete
                                if (_reassembler.Add(tid, tot, off, chunkSeg, out var full))
                                {
                                    // Deliver as a normal Data message (type preserved – here we're fragmenting any type)
                                    _server.RaiseData(_id, type, full);
                                }
                                // If not complete yet, do nothing (wait for more fragments)
                                continue; // handled
                            }

                            _server.RaiseData(_id, type, payload);
                            break;
                    }
                }
                catch (EndOfStreamException)
                {
                    // client closed
                    break;
                }
                catch (IOException)
                {
                    // network error
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Server] {_id} receive error: {ex.Message}");
                    break;
                }
            }
        }

        private void HandleAuthRequest(byte[] payload)
        {
            using (var ms = new MemoryStream(payload ?? Array.Empty<byte>()))
            using (var br = new BinaryReader(ms))
            {
                string user = Protocol.ReadLPString(br);
                string passHash = Protocol.ReadLPString(br);

                bool ok = ClientAuth.Authenticate(user, passHash);
                IsAuthenticated = ok;

                using (var msOut = new MemoryStream())
                using (var bw = new BinaryWriter(msOut))
                {
                    bw.Write(ok ? 1 : 0);
                    Protocol.SendAsync(_stream, MessageType.AuthResponse, msOut.ToArray(), NextSeq(), Protocol.FlagNone, _cts.Token).Wait();
                }
            }
        }

        private void HandleRegisterRequest(byte[] payload)
        {
            using (var ms = new MemoryStream(payload ?? Array.Empty<byte>()))
            using (var br = new BinaryReader(ms))
            {
                string user = Protocol.ReadLPString(br);
                string passHash = Protocol.ReadLPString(br);
                string email = Protocol.ReadLPString(br);
                string info = Protocol.ReadLPString(br);

                bool ok = ClientAuth.Register(user, passHash, email, info);

                using (var msOut = new MemoryStream())
                using (var bw = new BinaryWriter(msOut))
                {
                    bw.Write(ok ? 1 : 0);
                    Protocol.SendAsync(_stream, MessageType.RegisterResponse, msOut.ToArray(), NextSeq(), Protocol.FlagNone, _cts.Token).Wait();
                }
            }
        }

        private async Task HeartbeatLoopAsync(ServerOptions opt, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(opt.HeartbeatInterval, ct).ConfigureAwait(false);

                    // If no receive for (interval + timeout), we consider dead.
                    if (DateTime.UtcNow - _lastReceiveUtc > (opt.HeartbeatInterval + opt.HeartbeatTimeout))
                    {
                        Console.WriteLine($"[Server] {_id} heartbeat timeout.");
                        await StopAsync().ConfigureAwait(false);
                        return;
                    }

                    // Send ping
                    await Protocol.SendAsync(_stream, MessageType.Ping, Array.Empty<byte>(), NextSeq(), Protocol.FlagNone, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception) { /* ignore heartbeat errors */ }
            }
        }

        private int NextSeq() => System.Threading.Interlocked.Increment(ref _seqSend);

        private async Task CloseWithProtocolErrorAsync(string reason)
        {
            Console.WriteLine($"[Server] {_id} protocol error: {reason}");
            await StopAsync().ConfigureAwait(false);
        }

        public async Task SendAsync(MessageType type, byte[] payload)
        {
            if (_stream == null) return;
            await Protocol.SendAsync(_stream, type, payload, NextSeq(), Protocol.FlagNone, _cts.Token).ConfigureAwait(false);
        }

        public async Task StopAsync()
        {
            try { _cts.Cancel(); } catch { }
            try { _stream?.Dispose(); } catch { }
            try { _tcp?.Close(); } catch { }
            await Task.CompletedTask;
        }
    }
}
