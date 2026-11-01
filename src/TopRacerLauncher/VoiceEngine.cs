using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace TopRacerLauncher;

/// <summary>
/// Голосовой чат по локальной сети: комната на 4 человека, режим «по кнопке» или
/// «постоянно», выбор микрофона и устройства вывода.
/// </summary>
public static class VoiceEngine
{
    private const int Port = 47772;
    private const int HeaderSize = 32;   // 16 байт room + 16 байт sender

    private static UdpClient? _udp;
    private static NAudio.Wave.WaveInEvent? _mic;
    private static NAudio.Wave.WaveOutEvent? _speaker;
    private static NAudio.Wave.BufferedWaveProvider? _buffer;
    private static CancellationTokenSource? _cts;

    public static Guid RoomId { get; private set; }
    public static bool InCall => RoomId != Guid.Empty;

    /// <summary>Зажат PTT. В режиме «постоянно» не используется.</summary>
    public static bool PushToTalkHeld { get; set; }

    public static bool OpenMic { get; set; }
    public static string InputDeviceName { get; set; } = "";
    public static string OutputDeviceName { get; set; } = "";
    public static int VoiceKey { get; set; } = 0x56;

    /// <summary>Усиление своего микрофона, проценты (100 = без изменений).</summary>
    public static int InputGain { get; set; } = 100;

    private static readonly Dictionary<Guid, int> PeerVolumes = new();

    public static int GetPeerVolume(Guid id) => PeerVolumes.TryGetValue(id, out int v) ? v : 100;

    public static void SetPeerVolume(Guid id, int percent) => PeerVolumes[id] = Math.Clamp(percent, 0, 200);

    /// <summary>Масштабирует 16-битный PCM на месте.</summary>
    private static void ApplyGain(byte[] buffer, int offset, int count, double gain)
    {
        if (Math.Abs(gain - 1.0) < 0.01) return;

        int end = offset + count;
        for (int i = offset; i + 1 < end; i += 2)
        {
            short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            int value = (int)(sample * gain);

            if (value > short.MaxValue) value = short.MaxValue;
            else if (value < short.MinValue) value = short.MinValue;

            buffer[i] = (byte)(value & 0xFF);
            buffer[i + 1] = (byte)((value >> 8) & 0xFF);
        }
    }

    public static event Action? StateChanged;

    /// <summary>Идёт ли сейчас передача нашего голоса.</summary>
    public static bool IsTransmitting => InCall && (OpenMic || PushToTalkHeld);

    public static List<string> GetInputDevices()
    {
        var list = new List<string>();
        for (int i = 0; i < NAudio.Wave.WaveIn.DeviceCount; i++)
            list.Add(NAudio.Wave.WaveIn.GetCapabilities(i).ProductName);
        return list;
    }

    public static List<string> GetOutputDevices()
    {
        var list = new List<string>();
        for (int i = 0; i < NAudio.Wave.WaveOut.DeviceCount; i++)
            list.Add(NAudio.Wave.WaveOut.GetCapabilities(i).ProductName);
        return list;
    }

    private static int FindDeviceNumber(IReadOnlyList<string> names, string wanted, int fallback)
    {
        if (string.IsNullOrWhiteSpace(wanted)) return fallback;
        for (int i = 0; i < names.Count; i++)
            if (string.Equals(names[i], wanted, StringComparison.OrdinalIgnoreCase)) return i;
        return fallback;
    }

    public static bool StartCall(Guid room)
    {
        if (InCall) LeaveCall();

        RoomId = room;

        try
        {
            _udp = new UdpClient { EnableBroadcast = true };
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
        }
        catch
        {
            RoomId = Guid.Empty;
            _udp?.Dispose();
            _udp = null;
            return false;
        }

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ReceiveLoop(_cts.Token));

        // Воспроизведение
        _buffer = new NAudio.Wave.BufferedWaveProvider(new NAudio.Wave.WaveFormat(16000, 16, 1))
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromMilliseconds(300),
        };

        var outs = GetOutputDevices();
        int outNumber = FindDeviceNumber(outs, OutputDeviceName, -1);
        _speaker = new NAudio.Wave.WaveOutEvent { DeviceNumber = outNumber, DesiredLatency = 200 };
        _speaker.Init(_buffer);
        _speaker.Play();

        // Захват
        var ins = GetInputDevices();
        int inNumber = FindDeviceNumber(ins, InputDeviceName, -1);
        _mic = new NAudio.Wave.WaveInEvent
        {
            DeviceNumber = inNumber,
            WaveFormat = new NAudio.Wave.WaveFormat(16000, 16, 1),
            BufferMilliseconds = 40,
        };
        _mic.DataAvailable += MicOnDataAvailable;

        try
        {
            _mic.StartRecording();
        }
        catch
        {
            // Микрофон занят или отключён — звонок не поднимаем.
            LeaveCall();
            return false;
        }

        StateChanged?.Invoke();
        return true;
    }

    public static void LeaveCall()
    {
        try { _cts?.Cancel(); } catch { }
        try { _udp?.Dispose(); } catch { }

        try { _mic?.StopRecording(); } catch { }
        if (_mic != null) _mic.DataAvailable -= MicOnDataAvailable;
        try { _mic?.Dispose(); } catch { }

        try { _speaker?.Stop(); } catch { }
        try { _speaker?.Dispose(); } catch { }

        try { _buffer?.ClearBuffer(); } catch { }

        _udp = null;
        _cts = null;
        _mic = null;
        _speaker = null;
        _buffer = null;
        RoomId = Guid.Empty;
        PushToTalkHeld = false;

        StateChanged?.Invoke();
    }

    private static void MicOnDataAvailable(object? sender, NAudio.Wave.WaveInEventArgs e)
    {
        if (!IsTransmitting) return;
        if (_udp == null || e.BytesRecorded <= 0) return;

        var packet = new byte[HeaderSize + e.BytesRecorded];
        RoomId.TryWriteBytes(packet.AsSpan(0, 16));
        SelfIdBytes().CopyTo(packet, 16);
        Buffer.BlockCopy(e.Buffer, 0, packet, HeaderSize, e.BytesRecorded);

        ApplyGain(packet, HeaderSize, e.BytesRecorded, InputGain / 100.0);

        try { _udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, Port)); }
        catch { /* сеть моргнула — пропускаем пакет */ }
    }

    private static byte[] SelfIdBytes()
    {
        var bytes = new byte[16];
        LanPresence.SelfGuid.TryWriteBytes(bytes);
        return bytes;
    }

    private static async Task ReceiveLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try { result = await _udp!.ReceiveAsync(); }
            catch
            {
                if (token.IsCancellationRequested) return;
                await Task.Delay(500);
                continue;
            }

            try
            {
                var data = result.Buffer;
                if (data.Length <= HeaderSize) continue;

                var room = new Guid(data.AsSpan(0, 16));
                if (room != RoomId) continue;

                var sender = new Guid(data.AsSpan(16, 16));
                if (sender == LanPresence.SelfGuid) continue;

                int volume = GetPeerVolume(sender);
                if (volume <= 0) continue;

                ApplyGain(data, HeaderSize, data.Length - HeaderSize, volume / 100.0);
                _buffer?.AddSamples(data, HeaderSize, data.Length - HeaderSize);
            }
            catch { /* битый пакет */ }
        }
    }
}
