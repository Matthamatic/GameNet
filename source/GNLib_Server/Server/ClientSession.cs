using GameNet.Common;
using GameNetServer.Data;
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace GameNetServer
{
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
                            await HandleAuthRequest(payload);
                            break;

                        case MessageType.RegisterRequest:
                            await HandleRegisterRequest(payload);
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
                    Console.WriteLine($"ERROR reading stream in ClientSession!\n" +
                        $"GUID: {_id}\n" +
                        $"{ex.Message}");
                    break;
                }
            }
        }

        private async Task HandleAuthRequest(byte[] payload)
        {
            using (var ms = new MemoryStream(payload ?? Array.Empty<byte>()))
            using (var br = new BinaryReader(ms))
            {
                string user = Protocol.ReadLPString(br);
                string passHash = Protocol.ReadLPString(br);

                var result = await AuthService.LoginAsync(user, passHash);
                IsAuthenticated = result.Accepted;

                using (var msOut = new MemoryStream())
                using (var bw = new BinaryWriter(msOut))
                {
                    bw.Write(result.Accepted ? 1 : 0);
                    Protocol.WriteLPString(bw, result.Message);
                    Protocol.SendAsync(_stream, MessageType.AuthResponse, msOut.ToArray(), NextSeq(), Protocol.FlagNone, _cts.Token).Wait();
                }
            }
        }

        private async Task HandleRegisterRequest(byte[] payload)
        {
            using (var ms = new MemoryStream(payload ?? Array.Empty<byte>()))
            using (var br = new BinaryReader(ms))
            {
                string user = Protocol.ReadLPString(br);
                string pass = Protocol.ReadLPString(br);
                string email = Protocol.ReadLPString(br);
                string info = Protocol.ReadLPString(br);

                var result = await AuthService.RegisterAsync(user, pass);
                //bool ok = AuthService.Register(user, passHash, email, info);

                using (var msOut = new MemoryStream())
                using (var bw = new BinaryWriter(msOut))
                {
                    bw.Write(result.Accepted ? 1 : 0);
                    Protocol.WriteLPString(bw, result.Message);
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
