using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Collections.ObjectModel;
using Terminal.Gui;



[DllImport("iphlpapi.dll", ExactSpelling = true)]
static extern int SendARP(int destIp, int srcIp, byte[] macAddr, ref uint phyAddrLen);

static string GetMacAddress(string ipAddress)
{
    try
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ip",
                Arguments = $"neigh show {ipAddress}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            var output = process?.StandardOutput.ReadToEnd() ?? "";
            process?.WaitForExit(1000);
            var mac = output.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(value => value.Count(character => character == ':') == 5 && value.Length == 17);
            return NormalizeMac(mac);
        }

        IPAddress ip = IPAddress.Parse(ipAddress);
        byte[] macAddr = new byte[6];
        uint macAddrLen = (uint)macAddr.Length;

        // Konversi IP ke format integer untuk SendARP
        int destIP = BitConverter.ToInt32(ip.GetAddressBytes(), 0);

        if (SendARP(destIP, 0, macAddr, ref macAddrLen) == 0)
        {
            string[] str = new string[(int)macAddrLen];
            for (int i = 0; i < macAddrLen; i++)
                str[i] = macAddr[i].ToString("X2");

            return string.Join(":", str);
        }
    }
    catch { }
    return "Tidak Ditemukan";
}

static async Task<string> GetHostNameAsync(string ipAddress)
{
    try
    {
        // Dns.GetHostEntryAsync mencari nama domain / NetBIOS di jaringan lokal
        IPHostEntry entry = await Dns.GetHostEntryAsync(ipAddress);
        return entry.HostName;
    }
    catch
    {
        return "Unknown";
    }
}

Application.Init();

var midnightNeon = new ColorScheme
{
    Normal = Application.Driver.MakeAttribute(new Color(222, 232, 240), new Color(11, 18, 32)),
    Focus = Application.Driver.MakeAttribute(new Color(11, 18, 32), new Color(244, 114, 182)),
    HotNormal = Application.Driver.MakeAttribute(new Color(34, 211, 238), new Color(11, 18, 32)),
    HotFocus = Application.Driver.MakeAttribute(new Color(11, 18, 32), new Color(34, 211, 238)),
    Disabled = Application.Driver.MakeAttribute(new Color(115, 130, 150), new Color(11, 18, 32))
};

var context = new DeviceContext(DeviceStore.Load(), midnightNeon);
ShowSplash(midnightNeon);
RunGroupPage(context);
Application.Shutdown();

static void ShowSplash(ColorScheme scheme)
{
    var splash = new Window(" SYSTEM BOOT ") { Width = 54, Height = 10, ColorScheme = scheme };
    splash.Add(
        new Label("SMKN 8 TIK KOTA JAYAPURA") { X = 8, Y = 2, ColorScheme = scheme },
        new Label("OCPH23-LABREMOTE // NETWORK CONTROL") { X = 8, Y = 4, ColorScheme = scheme },
        new Label("INITIALIZING...") { X = 8, Y = 6, ColorScheme = scheme });
    Application.MainLoop.AddTimeout(TimeSpan.FromSeconds(5), _ => { Application.RequestStop(); return false; });
    Application.Run(splash);
}

static void EnableEscape(View view, Action action)
{
    view.KeyDown += (s, args) =>
    {
        if (args == Key.Esc)
        {
            action();
            args.Handled = true;
        }
    };
}

static void RunGroupPage(DeviceContext context)
{
    var page = new Window(" SELECT GROUP ") { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), ColorScheme = context.Scheme };
    var groupList = new ListView(context.Config.Groups) { X = 2, Y = 2, Width = Dim.Fill(4), Height = Dim.Fill(7), ColorScheme = context.Scheme };
    var status = new Label("Pilih group untuk melihat perangkat.") { X = 2, Y = Pos.AnchorEnd(5), Width = Dim.Fill(2), ColorScheme = context.Scheme };
    var discover = new Button("Discover") { X = 2, Y = Pos.AnchorEnd(3), ColorScheme = context.Scheme };
    var addGroup = new Button("Add group") { X = Pos.Right(discover) + 2, Y = Pos.AnchorEnd(3), ColorScheme = context.Scheme };
    var removeGroup = new Button("Remove group") { X = Pos.Right(addGroup) + 2, Y = Pos.AnchorEnd(3), ColorScheme = context.Scheme };
    var open = new Button("Open group") { X = Pos.Right(removeGroup) + 2, Y = Pos.AnchorEnd(3), ColorScheme = context.Scheme };
    var showToken = new Button("Show Token") { X = Pos.Right(open) + 2, Y = Pos.AnchorEnd(3), ColorScheme = context.Scheme };
    var quit = new Button("Quit") { X = Pos.AnchorEnd(8), Y = Pos.AnchorEnd(3), ColorScheme = context.Scheme };

    discover.Accepting += (_, _) => RunDiscoverPage(context);
    addGroup.Accepting += (_, _) => AddGroup(context, groupList, status);
    removeGroup.Accepting += (_, _) => RemoveGroup(context, groupList, status);
    Action openSelectedGroup = () =>
    {
        var index = groupList.SelectedItem;
        if (index >= 0 && index < context.Config.Groups.Count)
            RunDevicePage(context, context.Config.Groups[index]);
    };
    open.Accepting += (_, _) => openSelectedGroup();
    groupList.KeyDown += (s, args) =>
    {
        if (args == Key.Enter)
        {
            openSelectedGroup();
            args.Handled = true;
        }
    };
    quit.Accepting += (_, _) => ConfirmQuit();
    showToken.Accepting += (_, _) => ShowTokenDialog(context);
    EnableEscape(page, ConfirmQuit);

    page.Add(groupList, status, discover, addGroup, removeGroup, open, showToken, quit);
    Application.Run(page);
}

static void ConfirmQuit()
{
    var answer = MessageBox.Query("Keluar", "Apakah Anda yakin ingin keluar dari LabRemote?", "Ya", "Tidak");
    if (answer == 0) Application.RequestStop();
}

static void RemoveGroup(DeviceContext context, ListView list, Label status)
{
    var index = list.SelectedItem;
    if (index < 0 || index >= context.Config.Groups.Count)
    {
        status.Text = "Pilih group terlebih dahulu.";
        return;
    }

    if (context.Config.Groups.Count <= 1)
    {
        MessageBox.ErrorQuery("Remove group", "Group terakhir tidak dapat dihapus.", "OK");
        return;
    }

    var group = context.Config.Groups[index];
    var deviceCount = context.Config.Devices.Count(device =>
        string.Equals(device.Group, group, StringComparison.OrdinalIgnoreCase));
    if (deviceCount > 0)
    {
        MessageBox.ErrorQuery(
            "Remove group",
            $"Group '{group}' masih memiliki {deviceCount} device. Pindahkan atau hapus device terlebih dahulu.",
            "OK");
        return;
    }

    var answer = MessageBox.Query("Remove group", $"Hapus group '{group}'?", "Hapus", "Batal");
    if (answer != 0) return;

    context.Config.Groups.RemoveAt(index);
    DeviceStore.Save(context.Config.Devices, context.Config.Groups);
    context.Reload();
    list.SetSource(context.Config.Groups);
    status.Text = $"Group '{group}' dihapus.";
}

static void RunDiscoverPage(DeviceContext context)
{
    context.Reload();
    if (!context.Config.Groups.Contains("Discovered")) context.Config.Groups.Add("Discovered");
    context.CurrentGroup = "Discovered";
    var devices = context.VisibleDevices;
    var page = new Window(" DISCOVER LOCAL NETWORK ") { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), ColorScheme = context.Scheme };
    var list = new ListView(DeviceLines(devices))
    {
        X = 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(6), ColorScheme = context.Scheme,
        AllowsMarking = true,
        AllowsMultipleSelection = true
    };
    ConfigureDeviceListColors(list, () => context.VisibleDevices);
    var status = new Label("Space: pilih device | Move group: pindahkan pilihan") { X = 1, Y = Pos.AnchorEnd(5), Width = Dim.Fill(1), ColorScheme = context.Scheme };
    var scan = new Button("Scan now") { X = 1, Y = Pos.AnchorEnd(3), ColorScheme = context.Scheme };
    var move = new Button("Move group") { X = Pos.Right(scan) + 1, Y = Pos.AnchorEnd(3), ColorScheme = context.Scheme };
    var remove = new Button("Remove selected") { X = Pos.Right(move) + 1, Y = Pos.AnchorEnd(3), ColorScheme = context.Scheme };
    var back = new Button("Back") { X = Pos.AnchorEnd(8), Y = Pos.AnchorEnd(3), ColorScheme = context.Scheme };
    scan.Accepting += async (_, _) => await DiscoverDevicesAsync(context, list, status);
    move.Accepting += (_, _) => MoveSelectedDevices(context, list, status);
    remove.Accepting += (_, _) => RemoveSelectedDevices(context, list, status);
    back.Accepting += (_, _) => Application.RequestStop();
    list.KeyDown += (s, args) =>
    {
        if (args == Key.Space)
        {
            list.MarkUnmarkSelectedItem();
            status.Text = $"Terpilih: {MarkedDevices(list, context.VisibleDevices).Count} device | Space: pilih/batal pilih";
            args.Handled = true;
        }
    };
    EnableEscape(page, () => Application.RequestStop());
    page.Add(list, status, scan, move, remove, back);
    _ = DiscoverDevicesAsync(context, list, status);
    Application.Run(page);
}

static void RunDevicePage(DeviceContext context, string group)
{
    context.Reload();
    context.CurrentGroup = group;
    var computers = context.VisibleDevices;
    var page = new Window($" {group.ToUpperInvariant()} // DEVICES ") { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), ColorScheme = context.Scheme };
    var list = new ListView(DeviceLines(computers))
    {
        X = 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(7), ColorScheme = context.Scheme,
        AllowsMarking = true,
        AllowsMultipleSelection = true
    };
    ConfigureDeviceListColors(list, () => computers);
    var status = new Label($"{group}: {computers.Count} device") { X = 1, Y = Pos.AnchorEnd(6), Width = Dim.Fill(1), ColorScheme = context.Scheme };
    var back = new Button("Back") { X = 1, Y = Pos.AnchorEnd(4), ColorScheme = context.Scheme };
    var add = new Button("Add device") { X = Pos.Right(back) + 1, Y = Pos.AnchorEnd(4), ColorScheme = context.Scheme };
    var wakeAll = new Button("Wake All") { X = Pos.Right(add) + 1, Y = Pos.AnchorEnd(4), ColorScheme = context.Scheme };
    var shutdownAll = new Button("Shutdown All") { X = Pos.Right(wakeAll) + 1, Y = Pos.AnchorEnd(4), ColorScheme = context.Scheme };
    var restartAll = new Button("Restart All") { X = Pos.Right(shutdownAll) + 1, Y = Pos.AnchorEnd(4), ColorScheme = context.Scheme };
    var refresh = new Button("Refresh") { X = Pos.Right(restartAll) + 1, Y = Pos.AnchorEnd(4), ColorScheme = context.Scheme };
    var removeSelected = new Button("Remove selected") { X = Pos.Right(refresh) + 1, Y = Pos.AnchorEnd(4), ColorScheme = context.Scheme };
    var active = true;

    back.Accepting += (_, _) => { active = false; Application.RequestStop(); };
    add.Accepting += (_, _) => AddDevice(context, list, status);
    Action refreshStatus = () => _ = RefreshStatusesAsync(list, computers, status);
    refresh.Accepting += (_, _) => refreshStatus();
    wakeAll.Accepting += (_, _) => WakeAll(computers, status);
    shutdownAll.Accepting += async (_, _) => await ExecutePowerActionAllAsync("shutdown", computers, list, status);
    restartAll.Accepting += async (_, _) => await ExecutePowerActionAllAsync("restart", computers, list, status);
    removeSelected.Accepting += (_, _) => RemoveSelectedDevices(context, list, status);
    list.KeyDown += (s, args) =>
    {
        if (args == Key.Space)
        {
            list.MarkUnmarkSelectedItem();
            status.Text = $"Terpilih: {MarkedDevices(list, computers).Count} device | Space: pilih/batal pilih";
            args.Handled = true;
            return;
        }
        if (args == Key.Delete)
        {
            RemoveSelectedDevices(context, list, status);
            args.Handled = true;
            return;
        }
        if (args == (Key.F.WithAlt))
        {
            refreshStatus();
            args.Handled = true;
            return;
        }
        if (args == Key.Enter)
        {
            ShowDeviceContextMenu(context, list, computers, status);
            args.Handled = true;
        }
    };
    page.KeyDown += (s, args) =>
    {
        if (args == (Key.F.WithAlt))
        {
            refreshStatus();
            args.Handled = true;
        }
    };
    EnableEscape(page, () => { active = false; Application.RequestStop(); });
    page.Add(list, status, back, add, wakeAll, shutdownAll, restartAll, refresh, removeSelected);
    Application.MainLoop.AddTimeout(TimeSpan.FromSeconds(5), _timer => { if (!active) return false; _ = RefreshStatusesAsync(list, context.VisibleDevices, status); return true; });
    Application.Run(page);
}

static string[] DeviceLines(List<ComputerDevice> computers) => computers
    .Select(c => $"{StatusIndicator(c.Status)} [{c.Group,-8}] {c.Name,-16} {c.IpAddress,-15} {c.Status,-8} {c.LastSeenText}")
    .ToArray();

static string StatusIndicator(string status) => status switch
{
    "Online" => "[ON] ",
    "Offline" => "[OFF]",
    "Error" => "[ERR]",
    _ => "[ ? ]"
};

static void ConfigureDeviceListColors(ListView list, Func<List<ComputerDevice>> getDevices)
{
    list.RowRender += (_, args) =>
    {
        var devices = getDevices();
        if (args.Row < 0 || args.Row >= devices.Count) return;
        args.RowAttribute = args.Row == list.SelectedItem
            ? Application.Driver.MakeAttribute(new Color(11, 18, 32), new Color(244, 114, 182))
            : StatusAttribute(devices[args.Row].Status);
    };
}

static Terminal.Gui.Attribute StatusAttribute(string status) => status switch
{
    "Online" => Application.Driver.MakeAttribute(new Color(74, 222, 128), new Color(11, 18, 32)),
    "Offline" => Application.Driver.MakeAttribute(new Color(148, 163, 184), new Color(11, 18, 32)),
    "Error" => Application.Driver.MakeAttribute(new Color(248, 113, 113), new Color(11, 18, 32)),
    _ => Application.Driver.MakeAttribute(new Color(250, 204, 21), new Color(11, 18, 32))
};

static ComputerDevice? SelectedDevice(ListView list, List<ComputerDevice> computers)
{
    var index = list.SelectedItem;
    return index >= 0 && index < computers.Count ? computers[index] : null;
}

#pragma warning disable CS8321
static async Task LegacyDiscoverDevicesAsync(ListView list, List<ComputerDevice> computers, List<string> groups, Label status)
{
    var hosts = LocalNetworkHosts().ToList();
    Application.MainLoop.Invoke(() => status.Text = $"Mendeteksi agent pada jaringan lokal ({hosts.Count} alamat)...");

    using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(700) };
    var found = 0;
    var gate = new object();

    await Parallel.ForEachAsync(hosts, new ParallelOptions { MaxDegreeOfParallelism = 32 }, async (ip, cancellationToken) =>
    {
        try
        {
            var health = await client.GetFromJsonAsync<AgentHealth>($"http://{ip}:5050/health", cancellationToken);
            if (health is null) return;

            lock (gate)
            {
                var device = computers.FirstOrDefault(existing => existing.IpAddress == ip.ToString());
                if (device is null)
                {
                    device = new ComputerDevice(
                        health.Machine ?? $"PC-{ip}",
                        ip.ToString(),
                        NormalizeMac(health.MacAddress),
                        "Discovered");
                    computers.Add(device);
                }
                else
                {
                    device.Name = health.Machine ?? device.Name;
                    if (!string.IsNullOrWhiteSpace(health.MacAddress))
                        device.MacAddress = NormalizeMac(health.MacAddress);
                }

                device.Status = "Online";
                device.LastSeen = DateTimeOffset.Now;
                found++;
            }
        }
        catch
        {
            // Host tidak menjalankan LabRemote.Agent atau port 5050 tidak tersedia.
        }
    });

    DeviceStore.Save(computers, groups);
    Application.MainLoop.Invoke(() =>
    {
        list.SetSource(DeviceLines(computers));
        status.Text = $"Discovery selesai: {found} agent ditemukan | Total: {computers.Count} device";
    });
}

static IEnumerable<IPAddress> LocalNetworkHosts()
{
    var seen = new HashSet<string>();
    var networks = NetworkInterface.GetAllNetworkInterfaces()
        .Where(item => item.OperationalStatus == OperationalStatus.Up &&
                       (item.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                        item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                       item.GetIPProperties().GatewayAddresses.Any(gateway =>
                           gateway.Address.AddressFamily == AddressFamily.InterNetwork))
        .ToList();

    // Gunakan semua LAN/Wi-Fi fisik aktif, bukan VPN, Docker, Hyper-V, atau adapter virtual lain.
    foreach (var network in networks)
    {
        foreach (var address in network.GetIPProperties().UnicastAddresses
                     .Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork && item.IPv4Mask is not null))
        {
            var ip = ToUInt32(address.Address);
            var mask = ToUInt32(address.IPv4Mask);
            var first = (ip & mask) + 1;
            var last = (ip & mask) | ~mask;
            if (last - first > 4096) continue;

            for (var value = first; value < last; value++)
            {
                var candidate = new IPAddress(BitConverter.GetBytes(value).Reverse().ToArray());
                if (seen.Add(candidate.ToString())) yield return candidate;
            }
        }
    }
}

static uint ToUInt32(IPAddress address)
    => BitConverter.ToUInt32(address.GetAddressBytes().Reverse().ToArray(), 0);

static string NormalizeMac(string? value)
{
    var hex = new string((value ?? "").Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
    return hex.Length == 12 ? string.Join("-", Enumerable.Range(0, 6).Select(index => hex.Substring(index * 2, 2))) : value ?? "";
}

static async Task RefreshStatusesAsync(ListView list, List<ComputerDevice> computers, Label status)
{



    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    foreach (var computer in computers)
    {
        try
        {
            using var response = await client.GetAsync($"{computer.AgentUrl}/health");
            computer.Status = response.IsSuccessStatusCode ? "Online" : "Error";
            computer.LastSeen = DateTimeOffset.Now;
        }
        catch
        {
            computer.Status = "Offline";
        }
    }

    Application.MainLoop.Invoke(() =>
    {
        list.SetSource(DeviceLines(computers));
        status.Text = $"Status diperbarui: {DateTime.Now:HH:mm:ss} | Total: {computers.Count} perangkat ";


    });
}

static async Task ExecutePowerActionAsync(string action, ListView list, List<ComputerDevice> computers, Label status)
{
    var selected = SelectedDevice(list, computers);
    if (selected is null) { status.Text = "Pilih perangkat terlebih dahulu."; return; }

    var answer = MessageBox.Query("Konfirmasi", $"{action} {selected.Name}?", "Ya", "Batal");
    if (answer != 0) { status.Text = "Operasi dibatalkan."; return; }

    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{selected.AgentUrl}/power/{action}");
        request.Headers.Add("X-Agent-Token", selected.AgentToken);
        using var response = await client.SendAsync(request);
        selected.LastAction = response.IsSuccessStatusCode ? $"{action} OK" : $"{action} gagal ({(int)response.StatusCode})";
        status.Text = $"{selected.Name}: {selected.LastAction}";
        list.SetSource(DeviceLines(computers));
    }
    catch (Exception ex)
    {
        selected.Status = "Offline";
        status.Text = $"{selected.Name}: {ex.Message}";
    }
}

static void WakeAll(List<ComputerDevice> computers, Label status)
{
    var sent = 0;
    foreach (var computer in computers)
    {
        try { WakeOnLan.Send(computer.MacAddress, computer.IpAddress); computer.LastAction = "wake OK"; sent++; }
        catch { computer.LastAction = "wake gagal"; }
    }
    status.Text = $"Wake dikirim ke {sent}/{computers.Count} device.";
}

static async Task ExecutePowerActionAllAsync(string action, List<ComputerDevice> computers, ListView list, Label status)
{
    if (computers.Count == 0) { status.Text = "Tidak ada device dalam group ini."; return; }
    var answer = MessageBox.Query("Konfirmasi", $"{action} semua {computers.Count} device?", "Ya", "Batal");
    if (answer != 0) { status.Text = "Operasi dibatalkan."; return; }

    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    var success = 0;
    foreach (var computer in computers)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{computer.AgentUrl}/power/{action}");
            request.Headers.Add("X-Agent-Token", computer.AgentToken);
            using var response = await client.SendAsync(request);
            computer.LastAction = response.IsSuccessStatusCode ? $"{action} OK" : $"{action} gagal";
            if (response.IsSuccessStatusCode) success++;
        }
        catch { computer.LastAction = $"{action} offline"; }
    }
    list.SetSource(DeviceLines(computers));
    status.Text = $"{action}: {success}/{computers.Count} agent menerima perintah.";
}

static void ShowDeviceContextMenu(DeviceContext context, ListView list, List<ComputerDevice> computers, Label status)
{
    var selected = SelectedDevice(list, computers);
    if (selected is null) { status.Text = "Pilih device terlebih dahulu."; return; }

    var menu = new Dialog($" {selected.Name} ", 40, 14);
    var wake = new Button("Wake") { X = 2, Y = 1, ColorScheme = context.Scheme };
    var launch = new Button("Launch") { X = 2, Y = 3, ColorScheme = context.Scheme };
    launch.Enabled = string.Equals(selected.Status, "Online", StringComparison.OrdinalIgnoreCase);
    var shutdown = new Button("Shutdown") { X = 2, Y = 5, ColorScheme = context.Scheme };
    var restart = new Button("Restart") { X = 2, Y = 7, ColorScheme = context.Scheme };
    var remove = new Button("Remove") { X = 2, Y = 9, ColorScheme = context.Scheme };
    var cancel = new Button("Cancel") { X = 2, Y = 11, ColorScheme = context.Scheme };

    wake.Accepting += (_, _) =>
    {
        try { WakeOnLan.Send(selected.MacAddress, selected.IpAddress); selected.LastAction = "wake OK"; status.Text = $"Wake dikirim ke {selected.Name}."; }
        catch (Exception ex) { status.Text = $"Wake gagal: {ex.Message}"; }
        Application.RequestStop();
    };
    launch.Accepting += (_, _) => ShowLaunchDialog(context, selected, status);
    shutdown.Accepting += async (_, _) => { await ExecutePowerActionAsync("shutdown", list, computers, status); Application.RequestStop(); };
    restart.Accepting += async (_, _) => { await ExecutePowerActionAsync("restart", list, computers, status); Application.RequestStop(); };
    remove.Accepting += (_, _) => RemoveDevice(context, selected, list, computers, status);
    cancel.Accepting += (_, _) => Application.RequestStop();
    EnableEscape(menu, () => Application.RequestStop());
    menu.Add(new Label("Pilih aksi:") { X = 2, Y = 0, ColorScheme = context.Scheme }, wake, launch, shutdown, restart, remove, cancel);
    Application.Run(menu);
}

static void RemoveDevice(DeviceContext context, ComputerDevice device, ListView list, List<ComputerDevice> computers, Label status)
{
    var answer = MessageBox.Query("Hapus device", $"Hapus {device.Name} ({device.IpAddress})?", "Hapus", "Batal");
    if (answer != 0) return;

    context.Config.Devices.Remove(device);
    DeviceStore.Save(context.Config.Devices, context.Config.Groups);
    context.Reload();
    list.SetSource(DeviceLines(context.VisibleDevices));
    status.Text = $"Device {device.Name} dihapus.";
    Application.RequestStop();
}

static void ShowTokenDialog(DeviceContext context)
{
    var token = Environment.GetEnvironmentVariable("LAB_REMOTE_AGENT_TOKEN") ?? "";
    var dialog = new Dialog(" AGENT TOKEN ", 82, 11) { ColorScheme = context.Scheme };
    var value = new Label(string.IsNullOrWhiteSpace(token) ? "Token not found" : token)
    {
        X = 2, Y = 2, Width = Dim.Fill(2), ColorScheme = context.Scheme
    };
    var copy = new Button("Copy Token") { X = 2, Y = 5, ColorScheme = context.Scheme };
    var close = new Button("Close") { X = Pos.Right(copy) + 2, Y = 5, ColorScheme = context.Scheme };
    copy.Accepting += (_, _) =>
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            MessageBox.ErrorQuery("Token", "Token tidak ditemukan.", "OK");
            return;
        }

        var result = ClipboardHelper.Copy(token);
        if (result.Success)
            MessageBox.Query("Token", "Token berhasil disalin ke clipboard.", "OK");
        else
            MessageBox.ErrorQuery("Clipboard", result.Message, "OK");
    };
    close.Accepting += (_, _) => Application.RequestStop();
    EnableEscape(dialog, () => Application.RequestStop());
    dialog.Add(new Label("Token controller:") { X = 2, Y = 1, ColorScheme = context.Scheme }, value, copy, close);
    Application.Run(dialog);
}

static void ShowLaunchDialog(DeviceContext context, ComputerDevice device, Label status)
{
    if (!string.Equals(device.Status, "Online", StringComparison.OrdinalIgnoreCase))
    {
        status.Text = $"{device.Name}: Launch hanya tersedia saat device Online.";
        return;
    }

    var dialog = new Dialog(" LAUNCH APPLICATION ", 62, 11) { ColorScheme = context.Scheme };
    var alias = new TextField("notepad") { X = 20, Y = 2, Width = 32, ColorScheme = context.Scheme };
    var launch = new Button("Jalankan") { X = 20, Y = 5, ColorScheme = context.Scheme };
    var cancel = new Button("Batal") { X = Pos.Right(launch) + 2, Y = 5, ColorScheme = context.Scheme };
    launch.Accepting += (_, _) =>
    {
        var name = alias.Text?.ToString()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name)) return;
        Application.RequestStop();
        _ = LaunchApplicationAsync(device, name, status);
    };
    cancel.Accepting += (_, _) => Application.RequestStop();
    EnableEscape(dialog, () => Application.RequestStop());
    dialog.Add(new Label("Alias aplikasi:") { X = 2, Y = 2, ColorScheme = context.Scheme }, alias, launch, cancel);
    Application.Run(dialog);
}

static async Task LaunchApplicationAsync(ComputerDevice device, string application, Label status)
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{device.AgentUrl}/app/launch/{Uri.EscapeDataString(application)}");
        request.Headers.Add("X-Agent-Token", device.AgentToken);
        using var response = await client.SendAsync(request);
        device.LastAction = response.IsSuccessStatusCode
            ? $"launch {application} OK"
            : $"launch gagal ({(int)response.StatusCode})";
        status.Text = $"{device.Name}: {device.LastAction}.";
    }
    catch (Exception ex)
    {
        device.LastAction = "launch gagal";
        status.Text = $"{device.Name}: {ex.Message}";
    }
}

static void AddGroup(DeviceContext context, ListView list, Label status)
{
    LegacyAddGroup(context.Config.Groups, context.Config.Devices, list, status);
    context.Reload();
    list.SetSource(context.Config.Groups);
}

static void AddDevice(DeviceContext context, ListView list, Label status)
{
    var dialog = new Dialog("Tambah perangkat", 60, 16);
    var group = new TextField(context.CurrentGroup) { X = 18, Y = 1, Width = 35 };
    var name = new TextField("") { X = 18, Y = 3, Width = 35 };
    var ip = new TextField("") { X = 18, Y = 5, Width = 35 };
    var mac = new TextField("") { X = 18, Y = 7, Width = 35 };
    var save = new Button("Simpan") { X = 18, Y = 10, ColorScheme = context.Scheme };
    var cancel = new Button("Batal") { X = Pos.Right(save) + 2, Y = 10, ColorScheme = context.Scheme };
    dialog.Add(new Label("Group:") { X = 2, Y = 1 }, group, new Label("Nama:") { X = 2, Y = 3 }, name,
        new Label("IP address:") { X = 2, Y = 5 }, ip, new Label("MAC address:") { X = 2, Y = 7 }, mac, save, cancel);
    save.Accepting += (_, _) =>
    {
        var deviceName = name.Text?.ToString()?.Trim() ?? "";
        var deviceIp = ip.Text?.ToString()?.Trim() ?? "";
        var deviceMac = mac.Text?.ToString()?.Trim() ?? "";
        var deviceGroup = group.Text?.ToString()?.Trim() ?? context.CurrentGroup;
        if (string.IsNullOrWhiteSpace(deviceName) || string.IsNullOrWhiteSpace(deviceIp) || string.IsNullOrWhiteSpace(deviceMac))
        {
            MessageBox.ErrorQuery("Validasi", "Nama, IP, dan MAC wajib diisi.", "OK"); return;
        }
        if (!context.Config.Groups.Contains(deviceGroup, StringComparer.OrdinalIgnoreCase)) context.Config.Groups.Add(deviceGroup);
        context.Config.Devices.Add(new ComputerDevice(deviceName, deviceIp, deviceMac, deviceGroup));
        DeviceStore.Save(context.Config.Devices, context.Config.Groups);
        context.Reload();
        list.SetSource(DeviceLines(context.VisibleDevices));
        status.Text = $"Perangkat {deviceName} ditambahkan ke {deviceGroup}.";
        Application.RequestStop();
    };
    cancel.Accepting += (_, _) => Application.RequestStop();
    EnableEscape(dialog, () => Application.RequestStop());
    Application.Run(dialog);
}

static void MoveSelectedDevice(DeviceContext context, ListView list, Label status)
{
    var selected = SelectedDevice(list, context.VisibleDevices);
    if (selected is null) { status.Text = "Pilih perangkat terlebih dahulu."; return; }
    var dialog = new Dialog($"Pindahkan {selected.Name}", 55, 15);
    var groups = new ListView(context.Config.Groups) { X = 2, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(4), ColorScheme = context.Scheme };
    var move = new Button("Pindahkan") { X = 2, Y = Pos.AnchorEnd(2), ColorScheme = context.Scheme };
    var cancel = new Button("Batal") { X = Pos.Right(move) + 2, Y = Pos.AnchorEnd(2), ColorScheme = context.Scheme };
    dialog.Add(groups, move, cancel);
    move.Accepting += (_, _) =>
    {
        var index = groups.SelectedItem;
        if (index < 0 || index >= context.Config.Groups.Count) return;
        selected.Group = context.Config.Groups[index];
        DeviceStore.Save(context.Config.Devices, context.Config.Groups);
        context.Reload();
        list.SetSource(DeviceLines(context.VisibleDevices));
        status.Text = $"{selected.Name} dipindahkan ke {selected.Group}.";
        Application.RequestStop();
    };
    cancel.Accepting += (_, _) => Application.RequestStop();
    EnableEscape(dialog, () => Application.RequestStop());
    Application.Run(dialog);
}

static List<ComputerDevice> MarkedDevices(ListView list, List<ComputerDevice> devices)
{
    if (list.Source is null) return [];
    return Enumerable.Range(0, devices.Count)
        .Where(index => list.Source.IsMarked(index))
        .Select(index => devices[index])
        .ToList();
}

static void MoveSelectedDevices(DeviceContext context, ListView list, Label status)
{
    var devices = context.VisibleDevices;
    var selected = MarkedDevices(list, devices);
    if (selected.Count == 0)
    {
        var current = SelectedDevice(list, devices);
        if (current is not null) selected.Add(current);
    }

    if (selected.Count == 0)
    {
        status.Text = "Pilih minimal satu device dengan Space.";
        return;
    }

    var dialog = new Dialog($"Pindahkan {selected.Count} device", 55, 15);
    var groups = new ListView(context.Config.Groups) { X = 2, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(4), ColorScheme = context.Scheme };
    var move = new Button("Pindahkan") { X = 2, Y = Pos.AnchorEnd(2), ColorScheme = context.Scheme };
    var cancel = new Button("Batal") { X = Pos.Right(move) + 2, Y = Pos.AnchorEnd(2), ColorScheme = context.Scheme };
    dialog.Add(groups, move, cancel);
    move.Accepting += (_, _) =>
    {
        var index = groups.SelectedItem;
        if (index < 0 || index >= context.Config.Groups.Count) return;
        var target = context.Config.Groups[index];
        foreach (var device in selected) device.Group = target;
        DeviceStore.Save(context.Config.Devices, context.Config.Groups);
        context.Reload();
        list.SetSource(DeviceLines(context.VisibleDevices));
        status.Text = $"{selected.Count} device dipindahkan ke {target}.";
        Application.RequestStop();
    };
    cancel.Accepting += (_, _) => Application.RequestStop();
    EnableEscape(dialog, () => Application.RequestStop());
    Application.Run(dialog);
}

static void RemoveSelectedDevices(DeviceContext context, ListView list, Label status)
{
    var devices = context.VisibleDevices;
    var selected = MarkedDevices(list, devices);
    if (selected.Count == 0)
    {
        status.Text = "Pilih device dengan Space terlebih dahulu.";
        return;
    }

    var answer = MessageBox.Query(
        "Hapus device",
        $"Hapus {selected.Count} device yang dipilih?",
        "Hapus",
        "Batal");
    if (answer != 0) return;

    foreach (var device in selected) context.Config.Devices.Remove(device);
    DeviceStore.Save(context.Config.Devices, context.Config.Groups);
    context.Reload();
    list.SetSource(DeviceLines(context.VisibleDevices));
    status.Text = $"{selected.Count} device berhasil dihapus.";
}

static async Task DiscoverDevicesAsync(DeviceContext context, ListView list, Label status)
{
    var hosts = LocalNetworkHosts().ToList();
    Application.MainLoop.Invoke(() => status.Text = $"Scan IP aktif, MAC, hostname, dan agent ({hosts.Count} alamat)...");
    using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(900) };
    var active = 0;
    var agents = 0;
    var gate = new object();
    await Parallel.ForEachAsync(hosts, new ParallelOptions { MaxDegreeOfParallelism = 32 }, async (ip, cancellationToken) =>
    {
        var result = await ProbeHostAsync(ip, client, cancellationToken);
        if (result is null) return;

        lock (gate)
        {
            var device = context.Config.Devices.FirstOrDefault(existing => existing.IpAddress == ip.ToString());
            if (device is null)
            {
                device = new ComputerDevice(result.Name, ip.ToString(), result.MacAddress, "Discovered");
                context.Config.Devices.Add(device);
                if (!context.Config.Groups.Contains("Discovered")) context.Config.Groups.Add("Discovered");
            }
            device.Name = result.Name;
            if (!string.IsNullOrWhiteSpace(result.MacAddress)) device.MacAddress = result.MacAddress;
            device.Status = result.AgentOnline ? "Online" : "Offline";
            device.LastSeen = DateTimeOffset.Now;
            active++;
            if (result.AgentOnline) agents++;
        }
    });
    DeviceStore.Save(context.Config.Devices, context.Config.Groups);
    context.Reload();
    Application.MainLoop.Invoke(() =>
    {
        list.SetSource(DeviceLines(context.VisibleDevices));
        status.Text = $"Discovery selesai: {active} aktif, {agents} agent | {context.CurrentGroup}: {context.VisibleDevices.Count} device";
    });
}

static async Task<NetworkProbeResult?> ProbeHostAsync(IPAddress ip, HttpClient client, CancellationToken cancellationToken)
{
    try
    {
        using var ping = new Ping();
        var reply = await ping.SendPingAsync(ip, 800);
        if (reply.Status != IPStatus.Success) return null;

        var mac = GetMacAddress(ip.ToString());
        var hostname = await GetHostNameAsync(ip.ToString());
        AgentHealth? health = null;
        try
        {
            health = await client.GetFromJsonAsync<AgentHealth>($"http://{ip}:5050/health", cancellationToken);
        }
        catch { }

        var name = health?.Machine ?? (hostname == "Unknown" ? $"PC-{ip}" : hostname);
        if (!string.IsNullOrWhiteSpace(health?.MacAddress)) mac = NormalizeMac(health.MacAddress);
        return new NetworkProbeResult(name, mac, health is not null);
    }
    catch (PingException)
    {
        return null;
    }
    catch (OperationCanceledException)
    {
        return null;
    }
}

static void LegacyAddGroup(List<string> groups, List<ComputerDevice> computers, ListView list, Label status)
{
    var dialog = new Dialog("Tambah group", 50, 10);
    var field = new TextField("") { X = 16, Y = 1, Width = 28 };
    var save = new Button("Simpan") { X = 16, Y = 3 };
    var cancel = new Button("Batal") { X = Pos.Right(save) + 2, Y = 3 };
    dialog.Add(new Label("Nama group:") { X = 2, Y = 1 }, field, save, cancel);

    save.Accepting += (_, _) =>
    {
        var group = field.Text?.ToString()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(group))
        {
            MessageBox.ErrorQuery("Validasi", "Nama group wajib diisi.", "OK");
            return;
        }
        if (!groups.Contains(group, StringComparer.OrdinalIgnoreCase)) groups.Add(group);
        DeviceStore.Save(computers, groups);
        status.Text = $"Group '{group}' tersedia.";
        Application.RequestStop();
    };
    cancel.Accepting += (_, _) => Application.RequestStop();
    EnableEscape(dialog, () => Application.RequestStop());
    Application.Run(dialog);
}

static void LegacyMoveSelectedDevice(ListView list, List<ComputerDevice> computers, List<string> groups, Label status)
{
    var selected = SelectedDevice(list, computers);
    if (selected is null) { status.Text = "Pilih perangkat terlebih dahulu."; return; }
    if (groups.Count == 0) { status.Text = "Belum ada group. Tambahkan group terlebih dahulu."; return; }

    var dialog = new Dialog($"Pindahkan {selected.Name}", 55, 15);
    var groupList = new ListView(groups) { X = 2, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(4) };
    var move = new Button("Pindahkan") { X = 2, Y = Pos.AnchorEnd(2) };
    var cancel = new Button("Batal") { X = Pos.Right(move) + 2, Y = Pos.AnchorEnd(2) };
    dialog.Add(groupList, move, cancel);

    move.Accepting += (_, _) =>
    {
        var index = groupList.SelectedItem;
        if (index < 0 || index >= groups.Count) return;
        selected.Group = groups[index];
        DeviceStore.Save(computers, groups);
        list.SetSource(DeviceLines(computers));
        status.Text = $"{selected.Name} dipindahkan ke {selected.Group}.";
        Application.RequestStop();
    };
    cancel.Accepting += (_, _) => Application.RequestStop();
    EnableEscape(dialog, () => Application.RequestStop());
    Application.Run(dialog);
}

static void LegacyAddDevice(ListView list, List<ComputerDevice> computers, List<string> groups, Label status)
{
    var dialog = new Dialog("Tambah perangkat", 60, 16);
    var group = new TextField(groups.FirstOrDefault() ?? "Lab 1") { X = 18, Y = 1, Width = 35 };
    var name = new TextField("") { X = 18, Y = 3, Width = 35 };
    var ip = new TextField("") { X = 18, Y = 5, Width = 35 };
    var mac = new TextField("") { X = 18, Y = 7, Width = 35 };
    var save = new Button("Simpan") { X = 18, Y = 10 };
    var cancel = new Button("Batal") { X = Pos.Right(save) + 2, Y = 10 };

    dialog.Add(new Label("Group/Lab:") { X = 2, Y = 1 }, group,
        new Label("Nama:") { X = 2, Y = 3 }, name,
        new Label("IP address:") { X = 2, Y = 5 }, ip,
        new Label("MAC address:") { X = 2, Y = 7 }, mac,
        save, cancel);

    save.Accepting += (_, _) =>
    {
        if (string.IsNullOrWhiteSpace(name.Text?.ToString()) || string.IsNullOrWhiteSpace(ip.Text?.ToString()) || string.IsNullOrWhiteSpace(mac.Text?.ToString()))
        {
            MessageBox.ErrorQuery("Validasi", "Nama, IP, dan MAC wajib diisi.", "OK");
            return;
        }

        var deviceName = name.Text?.ToString() ?? "";
        var deviceIp = ip.Text?.ToString() ?? "";
        var deviceMac = mac.Text?.ToString() ?? "";
        var deviceGroup = group.Text?.ToString() ?? "Lab 1";
        computers.Add(new ComputerDevice(deviceName, deviceIp, deviceMac, deviceGroup));
        if (!groups.Contains(deviceGroup, StringComparer.OrdinalIgnoreCase)) groups.Add(deviceGroup);
        DeviceStore.Save(computers, groups);
        list.SetSource(DeviceLines(computers));
        status.Text = $"Perangkat {deviceName} ditambahkan ke {deviceGroup}.";
        Application.RequestStop();
    };
    cancel.Accepting += (_, _) => Application.RequestStop();
    EnableEscape(dialog, () => Application.RequestStop());

    Application.Run(dialog);
}
#pragma warning restore CS8321

public sealed record NetworkProbeResult(string Name, string MacAddress, bool AgentOnline);

public static class ClipboardHelper
{
    public static (bool Success, string Message) Copy(string text)
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { (FileName: "clip.exe", Arguments: "") }
            : OperatingSystem.IsMacOS()
                ? new[] { (FileName: "pbcopy", Arguments: "") }
                : new[]
                {
                    (FileName: "wl-copy", Arguments: ""),
                    (FileName: "xclip", Arguments: "-selection clipboard"),
                    (FileName: "xsel", Arguments: "--clipboard --input")
                };

        foreach (var candidate in candidates)
        {
            try
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = candidate.FileName,
                    Arguments = candidate.Arguments,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    CreateNoWindow = true
                });
                if (process is null) continue;
                process.StandardInput.Write(text);
                process.StandardInput.Close();
                process.WaitForExit(3000);
                if (process.ExitCode == 0)
                    return (true, "Token berhasil disalin.");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Coba utilitas clipboard berikutnya.
            }
        }

        return (false, OperatingSystem.IsLinux()
            ? "Clipboard tidak tersedia. Instal wl-clipboard, xclip, atau xsel."
            : "Clipboard tidak tersedia pada sistem ini.");
    }
}

public sealed class ComputerDevice(string name, string ipAddress, string macAddress, string group)
{
    public string Name { get; set; } = name;
    public string IpAddress { get; set; } = ipAddress;
    public string MacAddress { get; set; } = macAddress;
    public string Group { get; set; } = group;
    public string Status { get; set; } = "Offline";
    public DateTimeOffset? LastSeen { get; set; }
    public string LastAction { get; set; } = "-";
    public string AgentUrl => $"http://{IpAddress}:5050";
    public string AgentToken => Environment.GetEnvironmentVariable("LAB_REMOTE_AGENT_TOKEN") ?? "change me";
    public string LastSeenText => LastSeen?.ToString("HH:mm:ss") ?? "-";
}

public sealed class AgentHealth
{
    public string? Machine { get; set; }
    public string? MacAddress { get; set; }
}

public sealed class DeviceConfig
{
    public List<ComputerDevice> Devices { get; set; } = [];
    public List<string> Groups { get; set; } = ["Lab 1", "Lab 2"];
}

public sealed class DeviceContext(DeviceConfig config, ColorScheme scheme)
{
    public DeviceConfig Config { get; private set; } = config;
    public ColorScheme Scheme { get; } = scheme;
    public string CurrentGroup { get; set; } = "";
    public List<ComputerDevice> VisibleDevices => Config.Devices
        .Where(device => string.Equals(device.Group, CurrentGroup, StringComparison.OrdinalIgnoreCase))
        .ToList();

    public void Reload() => Config = DeviceStore.Load();
}

public static class DeviceStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LabRemote", "devices.json");

    public static DeviceConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var config = JsonSerializer.Deserialize<DeviceConfig>(File.ReadAllText(FilePath)) ?? new DeviceConfig();
                if (config.Groups.Count == 0) config.Groups.AddRange(["Lab 1", "Lab 2"]);
                foreach (var device in config.Devices)
                {
                    device.Status = "Offline";
                    device.LastSeen = null;
                }
                return config;
            }
        }
        catch { }

        var defaults = new DeviceConfig();
        Save(defaults.Devices, defaults.Groups);
        return defaults;
    }

    public static void Save(List<ComputerDevice> devices, List<string> groups)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new DeviceConfig { Devices = devices, Groups = groups }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public static class WakeOnLan
{
    public static void Send(string macAddress, string deviceIpAddress)
    {
        var mac = macAddress.Split([':', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => Convert.ToByte(part, 16)).ToArray();
        if (mac.Length != 6) throw new ArgumentException("MAC address tidak valid.", nameof(macAddress));

        var packet = Enumerable.Repeat((byte)0xFF, 6)
            .Concat(Enumerable.Repeat(mac, 16).SelectMany(x => x)).ToArray();

        var broadcast = GetSubnetBroadcast(deviceIpAddress);
        using var client = new UdpClient { EnableBroadcast = true };
        foreach (var port in new[] { 9, 7 })
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                client.Send(packet, packet.Length, new IPEndPoint(broadcast, port));
                client.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, port));
            }
        }
    }

    private static IPAddress GetSubnetBroadcast(string deviceIpAddress)
    {
        if (!IPAddress.TryParse(deviceIpAddress, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            return IPAddress.Broadcast;

        var bytes = address.GetAddressBytes();
        bytes[3] = 255; // Default lab network: /24, e.g. 192.168.1.255.
        return new IPAddress(bytes);
    }
}

// Compatibility adapters used while the application moves from Terminal.Gui v1
// construction syntax to the v2 property/event model.
sealed class Window : Terminal.Gui.Window
{
    public Window() : base() { }
    public Window(string title) : this() => Title = title;
}

sealed class Label : Terminal.Gui.Label
{
    public Label() : base() { }
    public Label(string text) : this() => Text = text;
}

sealed class Button : Terminal.Gui.Button
{
    public Button() : base() { }
    public Button(string text) : this() => Text = text;
}

sealed class TextField : Terminal.Gui.TextField
{
    public TextField() : base() { }
    public TextField(string text) : this() => Text = text;
}

sealed class ListView : Terminal.Gui.ListView
{
    public ListView() : base() { }

    public ListView(IEnumerable<string> items) : this()
    {
        SetSource(items);
    }

    public void SetSource(IEnumerable<string> items)
    {
        Source = new ListWrapper<string>(new ObservableCollection<string>(items));
    }
}

sealed class Dialog : Terminal.Gui.Dialog
{
    public Dialog() : base() { }

    public Dialog(string title, int width, int height) : this()
    {
        Title = title;
        Width = width;
        Height = height;
    }
}

static class Application
{
    public static DriverFacade Driver { get; } = new();

    public static void Init() => Terminal.Gui.Application.Init();
    public static void Shutdown() => Terminal.Gui.Application.Shutdown();
    public static void Run(Terminal.Gui.Toplevel view) => Terminal.Gui.Application.Run(view);
    public static void RequestStop() => Terminal.Gui.Application.RequestStop();
    public static CompatMainLoop MainLoop { get; } = new();

    public sealed class DriverFacade
    {
        public Terminal.Gui.Attribute MakeAttribute(Terminal.Gui.Color foreground, Terminal.Gui.Color background) =>
            new(foreground, background);
    }

    public sealed class CompatMainLoop
    {
        public object? AddTimeout(TimeSpan timeout, Func<object?, bool> callback) =>
            Terminal.Gui.Application.AddTimeout(timeout, () => callback(null));

        public void Invoke(Action action) => Terminal.Gui.Application.Invoke(action);
    }
}
