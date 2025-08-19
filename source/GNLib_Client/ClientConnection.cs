using GameNet.Common;
using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace GameNet.Client
{
    public sealed class ClientOptions
    {
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 9000;

        public bool UseTls { get; set; } = true;
        public string TlsTargetHost { get; set; } = "localhost"; // CN/SNI for cert validation
        public bool AllowInvalidServerCertForTesting { get; set; } = false;

        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
    }

    public sealed class ClientConnection : IDisposable
    {
        private readonly ClientOptions _opt;
        private TcpClient _tcp;
        private Stream _stream;
        private CancellationTokenSource _cts;
        private int _seqSend;

        public bool IsConnected => _tcp?.Connected == true;
        public bool IsAuthenticated { get; private set; }

        public event Action<MessageType, byte[]> DataReceived;
        public event Action Disconnected;
        public event Action Connected;
        public event Action<Exception> ConnectFail;


        public ClientConnection(ClientOptions options) => _opt = options ?? new ClientOptions();

        public async Task ConnectAsync()
        {
            try
            {
                _cts = new CancellationTokenSource();
                _tcp = new TcpClient();
                await _tcp.ConnectAsync(_opt.Host, _opt.Port).ConfigureAwait(false);

                var net = _tcp.GetStream();
                if (_opt.UseTls && !_opt.AllowInvalidServerCertForTesting)
                {
                    var ssl = new SslStream(net, false, (sender, cert, chain, errs) => false);
                    await ssl.AuthenticateAsClientAsync(_opt.TlsTargetHost, null, SslProtocols.Tls12, checkCertificateRevocation: true)
                             .ConfigureAwait(false);
                    _stream = ssl;
                }
                else if (_opt.UseTls && _opt.AllowInvalidServerCertForTesting)
                {
                    var ssl = new SslStream(net, false, (sender, cert, chain, errs) => true);
                    await ssl.AuthenticateAsClientAsync(_opt.TlsTargetHost, null, SslProtocols.Tls12, checkCertificateRevocation: false)
                             .ConfigureAwait(false);
                    _stream = ssl;
                    Console.WriteLine("[Client] WARNING: accepting invalid server certificate (testing only).");
                }
                else
                {
                    _stream = net;
                    Console.WriteLine("[Client] WARNING: Insecure (non-TLS) connection (testing only).");
                }

                _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
                _ = Task.Run(() => HeartbeatLoopAsync(_cts.Token));
                Connected?.Invoke();
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"{ex.GetType()}!\n{ex.Message}");
                ConnectFail?.Invoke(ex);
            }

            
        }

        public async Task DisconnectAsync()
        {
            try { _cts?.Cancel(); } catch { }
            try { _stream?.Dispose(); } catch { }
            try { _tcp?.Close(); } catch { }
            Disconnected?.Invoke();
            await Task.CompletedTask;
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var (type, flags, seq, payload) = await Protocol.ReceiveAsync(_stream, ct).ConfigureAwait(false);

                    switch (type)
                    {
                        case MessageType.Ping:
                            await Protocol.SendAsync(_stream, MessageType.Pong, Array.Empty<byte>(), NextSeq(), Protocol.FlagNone, ct).ConfigureAwait(false);
                            break;

                        case MessageType.AuthResponse:
                            using (var ms = new MemoryStream(payload))
                            using (var br = new BinaryReader(ms))
                            {
                                IsAuthenticated = br.ReadInt32() == 1;
                            }
                            break;

                        case MessageType.RegisterResponse:
                            // Currently only an OK flag
                            break;

                        default:
                            
                            break;
                    }
                    DataReceived?.Invoke(type, payload);
                }
            }
            catch (EndOfStreamException) { /* server closed */ }
            catch (IOException) { /* net error */ }
            catch (OperationCanceledException) { /* closing */ }
            finally
            {
                await DisconnectAsync().ConfigureAwait(false);
            }
        }

        private async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_opt.HeartbeatInterval, ct).ConfigureAwait(false);
                    await Protocol.SendAsync(_stream, MessageType.Ping, Array.Empty<byte>(), NextSeq(), Protocol.FlagNone, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
                catch { /* ignore */ }
            }
        }

        private int NextSeq() => System.Threading.Interlocked.Increment(ref _seqSend);

        // ------------ Public API ------------
        public async Task LoginAsync(string username, string password)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                Protocol.WriteLPString(bw, username ?? "");
                Protocol.WriteLPString(bw, password);
                await Protocol.SendAsync(_stream, MessageType.AuthRequest, ms.ToArray(), NextSeq(), Protocol.FlagNone, _cts.Token).ConfigureAwait(false);
            }
        }

        public async Task RegisterAsync(string username, string password, string email = "", string info = "", bool alreadyHashed = false)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                Protocol.WriteLPString(bw, username ?? "");
                Protocol.WriteLPString(bw, password);
                Protocol.WriteLPString(bw, email ?? "");
                Protocol.WriteLPString(bw, info ?? "");
                await Protocol.SendAsync(_stream, MessageType.RegisterRequest, ms.ToArray(), NextSeq(), Protocol.FlagNone, _cts.Token).ConfigureAwait(false);
            }
        }

        public async Task SendDataAsync(byte[] payload, MessageType type = MessageType.Data)
        {
            if (payload.Length <= Protocol.MaxMessageSize)
            { await Protocol.SendAsync(_stream, type, payload ?? Array.Empty<byte>(), NextSeq(), Protocol.FlagNone, _cts.Token); }
            else
            {
                MemoryStream source = new MemoryStream(payload);
                await SendLargeAsync(type, source, payload.Length);
            }
        }


        //=> Protocol.SendAsync(_stream, type, payload ?? Array.Empty<byte>(), NextSeq(), Protocol.FlagNone, _cts.Token);

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


        public void Dispose() => _cts?.Cancel();
    }
}
