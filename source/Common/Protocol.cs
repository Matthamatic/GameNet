using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GameNet.Common
{
    public enum MessageType : byte
    {
        Data = 0x01,
        Ping = 0x02,
        Pong = 0x03,

        AuthRequest = 0x10,
        AuthResponse = 0x11,
        RegisterRequest = 0x12,
        RegisterResponse = 0x13
    }

    public enum DataType : int
    {
        Invalid = -1,
        Chat = 0,
        GameAction = 10,
        LoadComplete = 11,
        GameObject = 31,
    }


    public struct GameDataResult
    {
        public GameDataResult(byte[] data)
        {
            if (data.Length < 4)
            {
                DataType = DataType.Invalid;
                UntypedObject = null;
            }
            else
            {
                int index = 0;
                DataType = (DataType)BitConverter.ToUInt32(data, index);
                index += 4;
                switch (DataType)
                {
                    case DataType.Chat:
                        UntypedObject = Encoding.UTF8.GetString(data, index, data.Length - 4);
                        break;
                    default:
                        byte[] sub = new byte[data.Length - 4];
                        Array.Copy(data, 4, sub, 0, sub.Length);
                        UntypedObject = sub;
                        break;
                }
            }   
        }

        public DataType DataType { get; private set; }
        public object UntypedObject { get; private set; }
    }

    public static class Protocol
    {
        // Header: [Length:4][Type:1][Flags:1][Seq:4] => 10 bytes total
        public const int HeaderSize = 10;
        public const int MaxMessageSize = 4 * 1024 * 1024; // 4 MB per message (tune as needed)

        // Flags bitfield (reserved for future use)
        public const byte FlagNone = 0x00;

        // --- Add to Protocol ---
        public const byte FlagFragment = 0x01;
        public const byte FlagFragmentLast = 0x02;

        // Fragment envelope = [TransferId:16][Offset:8][TotalLen:8] + ChunkData
        public const int FragmentMetaSize = 16 + 8 + 8;
        public static int MaxFragmentData => MaxMessageSize - FragmentMetaSize;

        public static byte[] BuildFragmentPayload(Guid transferId, long offset, long totalLen, byte[] chunk, int chunkLen)
        {
            using (var ms = new MemoryStream(FragmentMetaSize + chunkLen))
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(transferId.ToByteArray());
                bw.Write(offset);
                bw.Write(totalLen);
                bw.Write(chunk, 0, chunkLen);
                return ms.ToArray();
            }
        }

        public static void ParseFragmentPayload(
            byte[] payload,
            out Guid transferId,
            out long offset,
            out long totalLen,
            out ArraySegment<byte> chunk)
        {
            if (payload == null || payload.Length < FragmentMetaSize)
                throw new InvalidDataException("Bad fragment payload");

            // .NET Fx 4.7.2: no ReadOnlySpan, no Guid(ReadOnlySpan<byte>)
            var guidBytes = new byte[16];
            Buffer.BlockCopy(payload, 0, guidBytes, 0, 16);
            transferId = new Guid(guidBytes);

            offset = BitConverter.ToInt64(payload, 16);
            totalLen = BitConverter.ToInt64(payload, 24);

            chunk = new ArraySegment<byte>(payload, FragmentMetaSize, payload.Length - FragmentMetaSize);
        }


        public static byte[] BuildHeader(int payloadLength, MessageType type, byte flags, int seq)
        {
            if (payloadLength < 0 || payloadLength > MaxMessageSize)
                throw new ArgumentOutOfRangeException(nameof(payloadLength));

            var header = new byte[HeaderSize];
            // Length (little endian)
            Array.Copy(BitConverter.GetBytes(payloadLength), 0, header, 0, 4);
            // Type
            header[4] = (byte)type;
            // Flags
            header[5] = flags;
            // Seq (little endian)
            Array.Copy(BitConverter.GetBytes(seq), 0, header, 6, 4);
            return header;
        }

        public static void ParseHeader(byte[] header, out int payloadLength, out MessageType type, out byte flags, out int seq)
        {
            if (header == null || header.Length != HeaderSize)
            { throw new ArgumentException("Invalid header size."); }

            payloadLength = BitConverter.ToInt32(header, 0);
            type = (MessageType)header[4];
            flags = header[5];
            seq = BitConverter.ToInt32(header, 6);

            if (payloadLength < 0 || payloadLength > MaxMessageSize)
            { throw new InvalidDataException("Invalid payload length.");}
        }

        public static async Task ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            int readTotal = 0;
            while (readTotal < count)
            {
                int read = await stream.ReadAsync(buffer, offset + readTotal, count - readTotal, ct).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("Remote closed the connection.");
                readTotal += read;
            }
        }

        public static async Task WriteAllAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            await stream.WriteAsync(buffer, offset, count, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        // Simple helpers for string <-> bytes (length-prefixed UTF8)
        public static void WriteLPString(BinaryWriter bw, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? string.Empty);
            bw.Write(bytes.Length);
            bw.Write(bytes);
        }

        public static string ReadLPString(BinaryReader br)
        {
            int len = br.ReadInt32();
            if (len < 0 || len > MaxMessageSize) throw new InvalidDataException("Invalid string length.");
            var bytes = br.ReadBytes(len);
            return Encoding.UTF8.GetString(bytes);
        }

        // Packs one message (header + payload) into two writes.
        public static async Task SendAsync(Stream stream, MessageType type, byte[] payload, int seq, byte flags, CancellationToken ct)
        {
            payload = payload ?? Array.Empty<byte>();
            var header = BuildHeader(payload.Length, type, flags, seq);
            await WriteAllAsync(stream, header, 0, header.Length, ct).ConfigureAwait(false);
            if (payload.Length > 0)
                await WriteAllAsync(stream, payload, 0, payload.Length, ct).ConfigureAwait(false);
        }

        public static async Task<(MessageType type, byte flags, int seq, byte[] payload)> ReceiveAsync(Stream stream, CancellationToken ct)
        {
            var header = new byte[HeaderSize];
            await ReadExactAsync(stream, header, 0, HeaderSize, ct).ConfigureAwait(false);
            ParseHeader(header, out int len, out MessageType type, out byte flags, out int seq);

            var payload = new byte[len];
            if (len > 0)
                await ReadExactAsync(stream, payload, 0, len, ct).ConfigureAwait(false);

            return (type, flags, seq, payload);
        }
    }
}
