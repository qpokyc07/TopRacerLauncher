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

    // ---- Кто сейчас говорит (для оверлея звонка) ----

    private static readonly Dictionary<Guid, DateTime> SpeakingSince = new();

    /// <summary>Говорит ли участник: слышали его пакеты в последние 600 мс.</summary>
    public static bool IsPeerSpeaking(Guid id)
        => SpeakingSince.TryGetValue(id, out var at) && (DateTime.UtcNow - at).TotalMilliseconds < 600;

    private static void MarkSpeaking(Guid id) => SpeakingSince[id] = DateTime.UtcNow;

    /// <summary>Масштабирует 16-битный PCM на месте.</summary>
    // ---- Шумоподавление и выравнивание громкости ----

    /// <summary>Порог тишины: всё тише этого считается шумом и глушится (16-бит PCM).</summary>
    public static int NoiseGateLevel { get; set; } = 900;

    /// <summary>Автовыравнивание: тихую речь подтягиваем, громкую приглушаем.</summary>
    public static bool AutoLevel { get; set; } = true;

    /// <summary>
    /// Мягкий шумовой затвор + компрессор: давит фоновый шум (вентилятор, клавиатуру)
    /// и выравнивает громкость речи, чтобы собеседник слышал ровно.
    /// </summary>
    private static void Denoise(byte[] buffer, int offset, int count)
    {
        if (count < 2) return;

        int samples = count / 2;

        // Средняя громкость блока — по ней решаем, шум это или речь.
        long sum = 0;
        for (int i = 0; i < samples; i++)
        {
            short s = (short)(buffer[offset + i * 2] | (buffer[offset + i * 2 + 1] << 8));
            sum += Math.Abs((int)s);
        }

        double level = samples > 0 ? (double)sum / samples : 0;
        double gate = Math.Max(200, NoiseGateLevel);

        // Ниже порога — плавно приглушаем (мягкий затвор, без «щелчков»).
        double factor = level >= gate ? 1.0 : Math.Max(0.12, level / gate);

        // Компрессор: тихую речь подтягиваем до ~35 % от максимума, громкую не даём уйти в клиппинг.
        if (AutoLevel && level > gate)
        {
            double target = 9000.0;
            double gain = Math.Clamp(target / Math.Max(level, 1.0), 1.0, 2.6);
            factor *= gain;
        }

        factor = Math.Min(factor, 3.2);

        for (int i = 0; i < samples; i++)
        {
            int index = offset + i * 2;
            short s = (short)(buffer[index] | (buffer[index + 1] << 8));
            int value = (int)(s * factor);

            if (value > short.MaxValue) value = short.MaxValue;
            if (value < short.MinValue) value = short.MinValue;

            buffer[index] = (byte)(value & 0xFF);
            buffer[index + 1] = (byte)((value >> 8) & 0xFF);
        }
    }
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
        SpeakingSince.Clear();

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

        Denoise(packet, HeaderSize, e.BytesRecorded);
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
                MarkSpeaking(sender);
            }
            catch { /* битый пакет */ }
        }
    }
}
