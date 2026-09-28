using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ArkAutomata;

// Source RCON.
public sealed class RconClient : IDisposable
{
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private int _nextId;
    private readonly int _timeoutMs;

    // Source RCON packet types
    private const int SERVERDATA_AUTH = 3;
    private const int SERVERDATA_EXECCOMMAND = 2;
    private const int SERVERDATA_RESPONSE_VALUE = 0;

    public RconClient(int timeoutMs = 30000)
    {
        _timeoutMs = timeoutMs;
    }

    public async Task ConnectAsync(string host, int port, string password)
    {
        _tcp = new TcpClient();

        using var connectCts = new CancellationTokenSource(_timeoutMs);
        await _tcp.ConnectAsync(host, port, connectCts.Token);
        _stream = _tcp.GetStream();

        int authId = ++_nextId;
        await SendPacketAsync(authId, SERVERDATA_AUTH, password);

        // Empty RESPONSE_VALUE first, then the auth result. Id -1 means bad password.
        var resp = await ReadPacketAsync();
        if (resp.Type == SERVERDATA_RESPONSE_VALUE)
            resp = await ReadPacketAsync();

        if (resp.Id == -1)
            throw new InvalidOperationException("RCON authentication failed: invalid password");
    }

    public async Task<string> SendCommandAsync(string command)
    {
        if (_stream == null)
            throw new InvalidOperationException("Not connected — call ConnectAsync first");

        int cmdId = ++_nextId;
        await SendPacketAsync(cmdId, SERVERDATA_EXECCOMMAND, command);

        var resp = await ReadPacketAsync();
        return resp.Body;
    }

    private async Task SendPacketAsync(int id, int type, string body)
    {
        if (_stream == null)
            throw new InvalidOperationException("Not connected");

        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

        // Packet layout after the size field:
        //   [int32 id] [int32 type] [null-terminated body] [empty null-terminated string]
        // Size = 4 (id) + 4 (type) + bodyLen + 1 (null) + 1 (null)
        int packetSize = 4 + 4 + bodyBytes.Length + 2;

        using var ms = new MemoryStream(4 + packetSize);
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        bw.Write(packetSize); // size prefix is not part of the size value
        bw.Write(id);
        bw.Write(type);
        bw.Write(bodyBytes);
        bw.Write((byte)0);
        bw.Write((byte)0); // second null is the empty string the protocol requires

        byte[] packet = ms.ToArray();

        using var cts = new CancellationTokenSource(_timeoutMs);
        await _stream.WriteAsync(packet, cts.Token);
        await _stream.FlushAsync(cts.Token);
    }

    private async Task<RconPacket> ReadPacketAsync()
    {
        if (_stream == null)
            throw new InvalidOperationException("Not connected");

        using var cts = new CancellationTokenSource(_timeoutMs);

        byte[] sizeBuffer = new byte[4];
        await ReadExactAsync(sizeBuffer, 4, cts.Token);
        int size = BitConverter.ToInt32(sizeBuffer, 0);

        if (size < 10 || size > 65536)
            throw new InvalidDataException($"Invalid RCON packet size: {size}");

        byte[] payload = new byte[size];
        await ReadExactAsync(payload, size, cts.Token);

        int id = BitConverter.ToInt32(payload, 0);
        int type = BitConverter.ToInt32(payload, 4);

        // Body length = total size - id(4) - type(4) - body_null(1) - empty_null(1)
        int bodyLen = size - 10;
        string body = bodyLen > 0
            ? Encoding.UTF8.GetString(payload, 8, bodyLen)
            : string.Empty;

        return new RconPacket(id, type, body);
    }

    private async Task ReadExactAsync(byte[] buffer, int count, CancellationToken ct)
    {
        int offset = 0;
        while (offset < count)
        {
            int read = await _stream!.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
            if (read == 0)
                throw new IOException("RCON connection closed by remote host");
            offset += read;
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _tcp?.Dispose();
        _stream = null;
        _tcp = null;
    }

    private readonly record struct RconPacket(int Id, int Type, string Body);
}
