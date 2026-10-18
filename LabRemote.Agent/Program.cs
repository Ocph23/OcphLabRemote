using System.Diagnostics;
using System.Net.NetworkInformation;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();
var token = Environment.GetEnvironmentVariable("LAB_REMOTE_AGENT_TOKEN")
            ?? builder.Configuration["Agent:Token"]
            ?? "change-me";

if (token == "change-me")
    Console.WriteLine("WARNING: set Agent:Token or LAB_REMOTE_AGENT_TOKEN before production use.");

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status = "online",
    machine = Environment.MachineName,
    operatingSystem = Environment.OSVersion.VersionString,
    macAddress = NetworkInterface.GetAllNetworkInterfaces()
        .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                          network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .Select(network => network.GetPhysicalAddress().ToString())
        .FirstOrDefault(mac => !string.IsNullOrWhiteSpace(mac)),
    utc = DateTimeOffset.UtcNow
}));

app.MapPost("/power/{action}", (string action, HttpRequest request) =>
{
    if (!request.Headers.TryGetValue("X-Agent-Token", out var suppliedToken) || suppliedToken != token)
        return Results.Unauthorized();

    if (action is not ("shutdown" or "restart"))
        return Results.BadRequest(new { error = "Action must be shutdown or restart." });

    var command = OperatingSystem.IsWindows()
        ? (action == "shutdown" ? ("shutdown", "/s /t 5") : ("shutdown", "/r /t 5"))
        : (action == "shutdown" ? ("systemctl", "poweroff") : ("systemctl", "reboot"));

    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = command.Item1,
            Arguments = command.Item2,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        return Results.Accepted($"/power/{action}", new { action, message = "Perintah diterima." });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Gagal menjalankan perintah: {ex.Message}");
    }
});

app.Run();
