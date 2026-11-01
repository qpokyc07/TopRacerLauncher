using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TopRacerLauncher;

/// <summary>Друг в локальной сети.</summary>
public sealed class LanPeer
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Game { get; init; } = "";
    public IPEndPoint? EndPoint { get; init; }
    public DateTime LastSeenUtc { get; set; }

    public string Room { get; init; } = "";

    public string Status => !string.IsNullOrWhiteSpace(Room)
        ? "в звонке"
        : string.IsNullOrWhiteSpace(Game) ? "в сети" : "играет: " + Game;
}

/// <summary>
/// Присутствие в локальной сети: раз в 3 секунды рассылаем UDP-broadcast со своим
/// именем и запущенной игрой, слушаем чужие пакеты и текстовые сообщения.
/// </summary>
public static class LanPresence
{
    private const int Port = 47771;
    private static readonly TimeSpan BeaconInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(12);

    private static readonly object Gate = new();
    private static readonly Dictionary<string, LanPeer> Peers = new();
    public static Guid SelfGuid { get; } = Guid.NewGuid();
    private static readonly string SelfId = SelfGuid.ToString("N");

    private static UdpClient? _udp;
    private static CancellationTokenSource? _cts;

    public static event Action? PeersChanged;

    /// <summary>Друг приглашает в голосовой звонок: (комната, имя зовущего).</summary>
    public static event Action<Guid, string>? CallInvited;

    /// <summary>Пришло сообщение от друга: (отправитель, текст).</summary>
    public static event Action<LanPeer, string>? MessageReceived;

    public static string SelfName => string.IsNullOrWhiteSpace(App.Settings.UserName)
        ? Environment.UserName
        : App.Settings.UserName.Trim();

    public static void Start()
    {
        if (_udp != null) return;

        try
        {
            _udp = new UdpClient { EnableBroadcast = true };
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
        }
        catch
        {
            _udp?.Dispose();
            _udp = null;
            return;   // порт занят (второй экземпляр) — работаем без сети
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _ = Task.Run(() => ReceiveLoop(token));
        _ = Task.Run(() => BeaconLoop(token));
    }

    public static void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _udp?.Dispose(); } catch { }
        _udp = null;
        _cts = null;
    }

    public static List<LanPeer> GetPeers()
    {
        lock (Gate)
        {
            var dead = Peers.Values.Where(p => DateTime.UtcNow - p.LastSeenUtc > PeerTimeout).Select(p => p.Id).ToList();
            foreach (var id in dead) Peers.Remove(id);

            return Peers.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    public static void SendMessage(LanPeer peer, string text)
    {
        if (_udp == null || peer.EndPoint == null || string.IsNullOrWhiteSpace(text)) return;

        var payload = new { k = "msg", to = peer.Id, from = SelfId, name = SelfName, text };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));

        try { _udp.Send(bytes, bytes.Length, peer.EndPoint); } catch { }
    }

    /// <summary>Зовёт друга в комнату голосового звонка.</summary>
    public static void SendCallInvite(LanPeer peer, Guid room)
    {
        if (_udp == null || peer.EndPoint == null) return;

        var payload = new { k = "call", room = room.ToString(), name = SelfName };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));

        try { _udp.Send(bytes, bytes.Length, peer.EndPoint); } catch { }
    }

    private static async Task BeaconLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var payload = new
            {
                k = "hi",
                id = SelfId,
                name = SelfName,
                game = App.Runner.IsRunning ? (App.Runner.CurrentTitle ?? "") : "",
                room = VoiceEngine.InCall ? VoiceEngine.RoomId.ToString() : "",
            };

            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));

            try
            {
                _udp?.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, Port));
            }
            catch { /* сеть недоступна — попробуем в следующий раз */ }

            try { await Task.Delay(BeaconInterval, token); } catch { return; }
        }
    }

    private static async Task ReceiveLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udp!.ReceiveAsync();
            }
            catch
            {
                if (token.IsCancellationRequested) return;
                await Task.Delay(500);
                continue;
            }

            try
            {
                HandlePacket(result.Buffer, result.RemoteEndPoint);
            }
            catch { /* битый пакет — пропускаем */ }
        }
    }

    private static void HandlePacket(byte[] buffer, IPEndPoint from)
    {
        using var doc = JsonDocument.Parse(buffer);
        var root = doc.RootElement;

        if (!root.TryGetProperty("k", out var kind)) return;

        switch (kind.GetString())
        {
            case "hi":
            {
                var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                if (id.Length == 0 || id == SelfId) return;

                var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var game = root.TryGetProperty("game", out var g) ? g.GetString() ?? "" : "";
                var room = root.TryGetProperty("room", out var rr) ? rr.GetString() ?? "" : "";

                bool added;
                lock (Gate)
                {
                    added = !Peers.ContainsKey(id);
                    Peers[id] = new LanPeer
                    {
                        Id = id,
                        Name = string.IsNullOrWhiteSpace(name) ? "Без имени" : name,
                        Game = game,
                        Room = room,
                        EndPoint = from,
                        LastSeenUtc = DateTime.UtcNow,
                    };
                }

                if (added) PeersChanged?.Invoke();
                break;
            }

            case "call":
            {
                var roomText = root.TryGetProperty("room", out var rm) ? rm.GetString() ?? "" : "";
                var caller = root.TryGetProperty("name", out var cn) ? cn.GetString() ?? "Друг" : "Друг";
                if (Guid.TryParse(roomText, out var callRoom))
                    CallInvited?.Invoke(callRoom, caller);
                break;
            }

            case "msg":
            {
                var to = root.TryGetProperty("to", out var toEl) ? toEl.GetString() : null;
                if (to != SelfId) return;

                var fromId = root.TryGetProperty("from", out var f) ? f.GetString() ?? "" : "";
                var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";

                var peer = new LanPeer
                {
                    Id = fromId,
                    Name = string.IsNullOrWhiteSpace(name) ? "Друг" : name,
                    EndPoint = from,
                    LastSeenUtc = DateTime.UtcNow,
                };

                MessageReceived?.Invoke(peer, text);
                break;
            }
        }
    }
}
