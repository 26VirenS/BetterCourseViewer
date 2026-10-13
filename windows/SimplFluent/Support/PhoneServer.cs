using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Simpl.Support;

/// <summary>What Send from Phone's server tells the panel, from its own threads.</summary>
internal abstract record PhoneEvent
{
    /// <summary>Listening: the address the QR code holds.</summary>
    public sealed record Ready(Uri Url) : PhoneEvent;
    /// <summary>The phone opened the page: it is connected.</summary>
    public sealed record Opened : PhoneEvent;
    public sealed record Began(int Id, string Name, long Size) : PhoneEvent;
    public sealed record Progress(int Id, long Got) : PhoneEvent;
    /// <summary>A file arrived whole, at <c>Path</c> (the session's folder), with the type the phone gave it.</summary>
    public sealed record Received(int Id, string Name, string Type, string Path) : PhoneEvent;
    /// <summary>A file stopped coming part way (the phone left the Wi-Fi, its page was closed).</summary>
    public sealed record Lost(int Id) : PhoneEvent;
    public sealed record Failed(string Why) : PhoneEvent;
}

/// <summary>
/// Send from Phone's server (the Mac's PhoneServer.swift): a small web server on this PC's own Wi-Fi address and a
/// random port, up only while Hand In's Send from Phone panel is open. It serves one page — the phone's
/// (Assets/PhoneHandIn.html, the assignment's facts written in) — at an address holding a one-time 128-bit key, and
/// takes the files the phone sends to <c>&lt;key&gt;/upload</c>, each written to a folder of the hand-in's own. Nothing
/// goes anywhere else: the phone talks to this PC alone, and the student still reviews and presses Submit here. Every
/// other path is 404; a connection from anything but a private address is dropped; a file is at most the hand-in's own
/// limit, a session 500 MB. Plain sockets (TcpListener), not HttpListener — which needs an address reserved by an
/// administrator; the first time it listens, Windows Firewall asks whether Simpl may use the network.
/// </summary>
internal sealed class PhoneServer
{
    public string Token { get; }
    private readonly byte[] tokenBytes;
    private readonly IPAddress host;
    private readonly byte[] page;
    private readonly string folder;
    private readonly long fileLimit, totalLimit;
    private readonly Func<string, string?> refuse;
    private readonly Action<PhoneEvent> events;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<TcpClient, byte> open = new();
    private readonly List<TcpListener> listeners = new();
    private long taken;
    private int nextId;
    private volatile bool stopped;

    /// <param name="host">this PC's private address to listen on (PhoneLan.Best()).</param>
    /// <param name="page">the phone's page, its facts already written in.</param>
    /// <param name="refuse">why a file of this name is not taken (null: it is) — the assignment's own types.</param>
    public PhoneServer(IPAddress host, byte[] page, string folder, long fileLimit, long totalLimit, Func<string, string?> refuse, Action<PhoneEvent> events)
    {
        Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        tokenBytes = Encoding.ASCII.GetBytes(Token);
        this.host = host;
        this.page = page;
        this.folder = folder;
        this.fileLimit = fileLimit;
        this.totalLimit = totalLimit;
        this.refuse = refuse;
        this.events = events;
    }

    /// <summary>Listening on the address, the port Windows picks. With <paramref name="loopback"/> (the screenshot suite's
    /// test), on this PC's own 127.0.0.1 too, at the same key: its address is returned.</summary>
    public Uri? Start(bool loopback = false)
    {
        Uri? local = null;
        try
        {
            var l = new TcpListener(host, 0);
            l.Start(16);
            listeners.Add(l);
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            if (loopback && !IPAddress.IsLoopback(host))
            {
                var lo = new TcpListener(IPAddress.Loopback, 0);
                lo.Start(16);
                listeners.Add(lo);
                local = new Uri($"http://127.0.0.1:{((IPEndPoint)lo.LocalEndpoint).Port}/{Token}/");
            }
            foreach (var each in listeners) _ = Task.Run(() => Accept(each));
            Tell(new PhoneEvent.Ready(new Uri($"http://{host}:{port}/{Token}/")));
            return local ?? (loopback ? new Uri($"http://{host}:{port}/{Token}/") : null);
        }
        catch (Exception e)
        {
            Stop();
            events(new PhoneEvent.Failed(e is SocketException s ? $"This PC wouldn’t let Simpl listen ({s.SocketErrorCode})." : e.Message));
            return null;
        }
    }

    /// <summary>Stopped: the listener closed, every connection dropped and any file part way through thrown away.</summary>
    public void Stop()
    {
        if (stopped) return;
        stopped = true;
        try { stop.Cancel(); } catch { }
        foreach (var l in listeners) { try { l.Stop(); } catch { } }
        foreach (var c in open.Keys) { try { c.Dispose(); } catch { } }
    }

    private void Tell(PhoneEvent e)
    {
        if (!stopped) events(e);
    }

    private async Task Accept(TcpListener l)
    {
        while (!stopped)
        {
            TcpClient c;
            try { c = await l.AcceptTcpClientAsync(stop.Token); }
            catch { break; }
            // (only the phone on this network: a private address, and a handful of connections at once)
            if (stopped || open.Count >= 12 || c.Client.RemoteEndPoint is not IPEndPoint { Address: var a } || !PhoneLan.IsLocal(a))
            {
                c.Dispose();
                continue;
            }
            open[c] = 0;
            _ = Task.Run(async () =>
            {
                try { await Serve(c); }
                catch { /* the phone went: its file, if any, is thrown away below */ }
                finally
                {
                    open.TryRemove(c, out _);
                    c.Dispose();
                }
            });
        }
    }

    private async Task Serve(TcpClient c)
    {
        c.NoDelay = true;
        var s = c.GetStream();
        using var time = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        time.CancelAfter(15000); // (the request's head must come soon)
        var buf = new byte[16 * 1024];
        int n = 0, end;
        while ((end = HeadEnd(buf, n)) < 0)
        {
            if (n == buf.Length) { await Reply(s, 431, new { error = "Too much was sent." }); return; }
            var r = await s.ReadAsync(buf.AsMemory(n), time.Token);
            if (r == 0) return;
            n += r;
        }
        var lines = Encoding.UTF8.GetString(buf, 0, end).Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length < 2) { await Reply(s, 400, new { error = "Bad request." }); return; }
        var method = first[0];
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        var path = first[1].Split('?')[0];
        var parts = path.Split('/');
        // "/<key>", "/<key>/" and "/<key>/upload": nothing else is here
        if (parts.Length < 2 || parts[0].Length != 0 || !Matches(parts[1])) { await Reply(s, 404, new { error = "Not found." }); return; }
        var rest = string.Join('/', parts.Skip(2));
        var body = buf.AsMemory(end + 4, n - end - 4);
        if (method == "GET" && parts.Length == 2)
        {
            await Reply(s, 301, Array.Empty<byte>(), "text/plain", $"Location: /{Token}/\r\n");
        }
        else if (method is "GET" or "HEAD" && parts.Length == 3 && rest.Length == 0)
        {
            Tell(new PhoneEvent.Opened());
            await Reply(s, 200, method == "HEAD" ? Array.Empty<byte>() : page, "text/html; charset=utf-8",
                "Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; img-src blob: data:; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\r\n");
        }
        else if (method == "POST" && parts.Length == 3 && rest == "upload")
        {
            await Receive(s, headers, body, time);
        }
        else
        {
            await Reply(s, 404, new { error = "Not found." });
        }
    }

    private static int HeadEnd(byte[] b, int n)
    {
        for (var i = 0; i + 3 < n; i++) if (b[i] == '\r' && b[i + 1] == '\n' && b[i + 2] == '\r' && b[i + 3] == '\n') return i;
        return -1;
    }

    /// <summary>The key compared in constant time (no answer comes back sooner for a nearer guess).</summary>
    private bool Matches(string s) => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(s), tokenBytes);

    /// <summary>A file sent: its length known before it comes, its name made safe, the assignment's types and the limits
    /// kept to; then read to the end into the folder, its progress told as it comes.</summary>
    private async Task Receive(NetworkStream s, Dictionary<string, string> headers, ReadOnlyMemory<byte> start, CancellationTokenSource time)
    {
        if (headers.ContainsKey("Transfer-Encoding") || !long.TryParse(headers.GetValueOrDefault("Content-Length"), out var length) || length < 0)
        {
            await Reply(s, 411, new { error = "Send the file's length." });
            return;
        }
        if (length == 0) { await Reply(s, 400, new { error = "This file is empty." }); return; }
        // (too large to be worth reading through: answered at once)
        if (length > fileLimit) { await Reply(s, 413, new { error = $"Too large to send — the most is {fileLimit / 1048576} MB." }); return; }
        var type = (headers.GetValueOrDefault("Content-Type") ?? "application/octet-stream").Split(';')[0].Trim();
        var name = PhoneLan.CleanName(headers.GetValueOrDefault("X-File-Name") ?? "", type);
        string? refusal = null;
        var code = 0;
        if (Interlocked.Read(ref taken) + length > totalLimit) { refusal = "That’s all this hand-in can take from your phone. Hand in what is there first."; code = 413; }
        else if (refuse(name) is { } why) { refusal = why; code = 415; }
        if (refusal != null)
        {
            // (read through to the end first, so the phone hears why rather than a dropped connection)
            await Drain(s, length - start.Length, time);
            await Reply(s, code, new { error = refusal });
            return;
        }
        var id = Interlocked.Increment(ref nextId);
        var file = PhoneLan.Unique(name, folder);
        Interlocked.Add(ref taken, length);
        Tell(new PhoneEvent.Began(id, Path.GetFileName(file), length));
        var done = false;
        try
        {
            await using (var f = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                long got = 0, told = 0;
                var part = start.Length > length ? start[..(int)length] : start;
                if (part.Length > 0) { await f.WriteAsync(part, stop.Token); got = part.Length; }
                var buf = new byte[256 * 1024];
                while (got < length)
                {
                    time.CancelAfter(30000);
                    var r = await s.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, length - got)), time.Token);
                    if (r == 0) break;
                    await f.WriteAsync(buf.AsMemory(0, r), stop.Token);
                    got += r;
                    // (told every quarter of a megabyte or so, and at the end)
                    if (got - told >= 256 * 1024 || got == length) { told = got; Tell(new PhoneEvent.Progress(id, got)); }
                }
                done = got == length;
            }
            if (!done) return;
            Tell(new PhoneEvent.Received(id, Path.GetFileName(file), type, file));
            await Reply(s, 200, new { ok = true, name = Path.GetFileName(file) });
        }
        finally
        {
            if (!done)
            {
                Interlocked.Add(ref taken, -length);
                try { File.Delete(file); } catch { }
                Tell(new PhoneEvent.Lost(id));
            }
        }
    }

    private async Task Drain(NetworkStream s, long left, CancellationTokenSource time)
    {
        var buf = new byte[64 * 1024];
        while (left > 0)
        {
            time.CancelAfter(30000);
            var r = await s.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, left)), time.Token);
            if (r == 0) return;
            left -= r;
        }
    }

    private static Task Reply(NetworkStream s, int code, object json) =>
        Reply(s, code, JsonSerializer.SerializeToUtf8Bytes(json), "application/json; charset=utf-8");

    private static async Task Reply(NetworkStream s, int code, byte[] body, string type, string extra = "")
    {
        var words = code switch
        {
            200 => "OK", 301 => "Moved Permanently", 400 => "Bad Request", 404 => "Not Found", 411 => "Length Required",
            413 => "Payload Too Large", 415 => "Unsupported Media Type", 431 => "Request Header Fields Too Large", _ => "Error",
        };
        var head = $"HTTP/1.1 {code} {words}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\n"
                   + $"Connection: close\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\n{extra}\r\n";
        await s.WriteAsync(Encoding.UTF8.GetBytes(head));
        if (body.Length > 0) await s.WriteAsync(body);
        await s.FlushAsync();
        try { s.Socket.Shutdown(SocketShutdown.Send); } catch { }
    }
}

/// <summary>The assignment's facts, for the phone's page and for what the server takes.</summary>
internal sealed record PhoneSetup(string Title, string Course, string? Color, IReadOnlyList<string> Allowed, IReadOnlyList<string> Convertible, long Limit);

/// <summary>The phone's page (Assets/PhoneHandIn.html, the Mac app's same file) with an assignment's facts written in, and
/// the rules for what the server takes.</summary>
internal static class PhonePage
{
    public static readonly string[] Images = { "jpg", "jpeg", "png", "heic", "heif", "gif", "webp", "tif", "tiff", "bmp" };


    /// <summary>The phone's page with the assignment's facts written in, as JSON that cannot end its script element (the
    /// serializer's own escaping: &lt; &gt; &amp; and every character past ASCII as \u….).</summary>
    public static byte[]? Build(PhoneSetup s)
    {
        string html;
        try
        {
            using var stream = typeof(PhonePage).Assembly.GetManifestResourceStream("PhoneHandIn.html");
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            html = reader.ReadToEnd();
        }
        catch { return null; }
        if (!html.Contains("__SIMPL_CONFIG__")) return null;
        var allowed = s.Allowed.Select(x => x.ToLowerInvariant().TrimStart('.')).ToList();
        var photos = PhotosRule(s);
        var accept = allowed.Count == 0 ? new List<string>() : allowed.Concat(s.Convertible.Select(x => x.ToLowerInvariant().TrimStart('.'))).Concat(photos == "no" ? Array.Empty<string>() : Images).Distinct().ToList();
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["title"] = s.Title, ["course"] = s.Course, ["color"] = s.Color ?? "", ["allowed"] = allowed, ["accept"] = accept,
            ["photos"] = photos, ["max"] = s.Limit, ["device"] = "PC",
        });
        json = json.Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");
        return System.Text.Encoding.UTF8.GetBytes(html.Replace("__SIMPL_CONFIG__", json));
    }

    /// <summary>What the phone's camera may send: "yes" (pictures as they are), "pdf" (made into a PDF here), "no".</summary>
    public static string PhotosRule(PhoneSetup s)
    {
        var a = s.Allowed.Select(x => x.ToLowerInvariant()).ToList();
        if (a.Count == 0 || a.Any(Images.Contains)) return "yes";
        if (a.Contains("pdf")) return "pdf";
        return s.Convertible.Any(x => Images.Contains(x.ToLowerInvariant())) ? "yes" : "no";
    }

    /// <summary>Why the server turns a file away (null: it is taken): the assignment's types, those it can be converted
    /// to, and photos where they become a PDF.</summary>
    public static Func<string, string?> Refusal(PhoneSetup s)
    {
        var allowed = s.Allowed.Select(x => x.ToLowerInvariant().TrimStart('.')).ToHashSet();
        var convertible = s.Convertible.Select(x => x.ToLowerInvariant().TrimStart('.')).ToHashSet();
        var photos = PhotosRule(s) != "no";
        var list = string.Join(", ", s.Allowed.Select(x => "." + x.ToLowerInvariant().TrimStart('.')));
        return name =>
        {
            if (allowed.Count == 0) return null;
            var ext = System.IO.Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
            if (allowed.Contains(ext) || convertible.Contains(ext) || (photos && Images.Contains(ext))) return null;
            return $"This assignment takes {list} files.";
        };
    }
}

/// <summary>
/// Where this PC is on the local network, for Send from Phone: its private IPv4 address — Wi-Fi's first, then
/// Ethernet's, one with a way out (a gateway) before one without — and what kind of network that is. Never a VPN's,
/// a virtual machine's switch or Bluetooth's.
/// </summary>
internal static class PhoneLan
{
    public sealed record Address(IPAddress Ip, string Kind);

    private static readonly string[] Skip = { "virtual", "vethernet", "vmware", "virtualbox", "wsl", "loopback", "bluetooth", "vpn", "tap-", "wireguard", "tailscale", "zerotier", "hamachi" };

    public static Address? Best()
    {
        Address? best = null;
        var rank = int.MaxValue;
        NetworkInterface[] all;
        try { all = NetworkInterface.GetAllNetworkInterfaces(); }
        catch { return null; }
        foreach (var nic in all)
        {
            try
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp) continue;
                var said = (nic.Name + " " + nic.Description).ToLowerInvariant();
                if (Skip.Any(said.Contains)) continue;
                var props = nic.GetIPProperties();
                var wifi = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                var wired = nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT;
                var r = (wifi ? 0 : wired ? 1 : 2) + (props.GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any)) ? 0 : 3);
                foreach (var u in props.UnicastAddresses)
                {
                    if (u.Address.AddressFamily != AddressFamily.InterNetwork || !IsPrivate(u.Address) || r >= rank) continue;
                    rank = r;
                    best = new Address(u.Address, wifi ? "Wi-Fi" : wired ? "Ethernet" : "network");
                }
            }
            catch { /* an adapter that cannot say: passed over */ }
        }
        return best;
    }

    /// <summary>10/8, 172.16/12 and 192.168/16: a home's, a school's or a phone hotspot's own addresses.</summary>
    public static bool IsPrivate(IPAddress a)
    {
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (a.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = a.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    }

    /// <summary>A private address, a link-local one, or this PC itself: who may connect.</summary>
    public static bool IsLocal(IPAddress a)
    {
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (IsPrivate(a) || IPAddress.IsLoopback(a)) return true;
        var b = a.GetAddressBytes();
        return a.AddressFamily == AddressFamily.InterNetwork && b[0] == 169 && b[1] == 254;
    }

    /// <summary>The name the phone gave, made safe for a file: no folders, nothing a file name may not hold, not too long,
    /// not one of Windows' reserved names, and an extension from its type when it had none.</summary>
    public static string CleanName(string raw, string type)
    {
        string s;
        try { s = Uri.UnescapeDataString(raw); } catch { s = raw; }
        s = s.Split('/', '\\').Last();
        var bad = Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '|', '?', '*' }).ToHashSet();
        s = new string(s.Where(ch => !bad.Contains(ch) && !char.IsControl(ch)).ToArray()).Trim().Trim('.').Trim();
        var ext = Path.GetExtension(s).TrimStart('.');
        var stem = ext.Length > 0 ? Path.GetFileNameWithoutExtension(s) : s;
        if (ext.Length == 0 || ext.Length > 10)
        {
            if (ext.Length > 0) stem = s;
            ext = type switch
            {
                "image/jpeg" => "jpg", "image/png" => "png", "image/heic" => "heic", "image/heif" => "heif", "image/gif" => "gif",
                "image/webp" => "webp", "application/pdf" => "pdf", "text/plain" => "txt", "video/mp4" => "mp4", "video/quicktime" => "mov",
                _ => "",
            };
        }
        if (stem.Length > 100) stem = stem[..100].Trim();
        if (stem.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(stem, "^(con|prn|aux|nul|com[0-9]|lpt[0-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) stem = stem.Length == 0 ? "File" : "File " + stem;
        return ext.Length == 0 ? stem : $"{stem}.{ext}";
    }

    /// <summary>A file in <paramref name="folder"/> by that name, or "name (2).ext" when it is taken.</summary>
    public static string Unique(string name, string folder)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var path = Path.Combine(folder, name);
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{stem} ({n}){ext}");
        return path;
    }
}
