using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();
var logDirectory = AgentLogPath.Resolve();
builder.Logging.AddProvider(new FileLoggerProvider(logDirectory));
var token = Environment.GetEnvironmentVariable("LAB_REMOTE_AGENT_TOKEN")
            ?? builder.Configuration["Agent:Token"]
            ?? "change-me";
var allowedApplications = builder.Configuration.GetSection("Agent:AllowedApplications")
    .GetChildren()
    .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
    .ToDictionary(entry => entry.Key, entry => entry.Value!, StringComparer.OrdinalIgnoreCase);

var app = builder.Build();
var logger = app.Logger;
logger.LogInformation(
    "LabRemote Agent started. OS={OperatingSystem}, Version={Version}, LogDirectory={LogDirectory}",
    Environment.OSVersion.VersionString,
    typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
    logDirectory);

if (token == "change-me")
    logger.LogWarning("Agent token is still the default value. Set Agent:Token or LAB_REMOTE_AGENT_TOKEN before production use.");

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
    {
        logger.LogWarning("Unauthorized power request. Action={Action}, RemoteIp={RemoteIp}", action, request.HttpContext.Connection.RemoteIpAddress);
        return Results.Unauthorized();
    }

    if (action is not ("shutdown" or "restart"))
    {
        logger.LogWarning("Rejected unsupported power action. Action={Action}, RemoteIp={RemoteIp}", action, request.HttpContext.Connection.RemoteIpAddress);
        return Results.BadRequest(new { error = "Action must be shutdown or restart." });
    }

    var command = OperatingSystem.IsWindows()
        ? (action == "shutdown" ? ("shutdown", "/s /t 5") : ("shutdown", "/r /t 5"))
        : (action == "shutdown" ? ("systemctl", "poweroff") : ("systemctl", "reboot"));

    try
    {
        logger.LogInformation("Power command accepted. Action={Action}, Command={Command}, RemoteIp={RemoteIp}",
            action, command.Item1, request.HttpContext.Connection.RemoteIpAddress);

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
        logger.LogError(ex, "Failed to execute power action. Action={Action}", action);
        return Results.Problem($"Gagal menjalankan perintah: {ex.Message}");
    }
});

app.MapPost("/app/launch/{name}", (string name, HttpRequest request) =>
{
    if (!request.Headers.TryGetValue("X-Agent-Token", out var suppliedToken) || suppliedToken != token)
    {
        logger.LogWarning("Unauthorized application launch request. Application={Application}, RemoteIp={RemoteIp}",
            name, request.HttpContext.Connection.RemoteIpAddress);
        return Results.Unauthorized();
    }

    if (!allowedApplications.TryGetValue(name, out var executable))
    {
        logger.LogWarning("Application launch rejected because it is not allowlisted. Application={Application}", name);
        return Results.NotFound(new { error = $"Aplikasi '{name}' tidak terdaftar di allowlist agent." });
    }

    try
    {
        logger.LogInformation("Application launch accepted. Application={Application}, Executable={Executable}, RemoteIp={RemoteIp}",
            name, executable, request.HttpContext.Connection.RemoteIpAddress);
        InteractiveProcessLauncher.Start(executable);

        return Results.Accepted($"/app/launch/{name}", new { application = name, message = "Aplikasi dijalankan." });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to launch application. Application={Application}", name);
        return Results.Problem($"Gagal menjalankan aplikasi: {ex.Message}");
    }
});

app.Run();

static class InteractiveProcessLauncher
{
    public static void Start(string executable)
    {
        if (!OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
            });
            return;
        }

        StartOnActiveWindowsSession(executable);
    }

    private static void StartOnActiveWindowsSession(string executable)
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == uint.MaxValue)
            throw new InvalidOperationException("Tidak ada user Windows yang sedang login.");

        if (!WTSQueryUserToken(sessionId, out var userToken))
            ThrowLastError("WTSQueryUserToken");

        try
        {
            if (!DuplicateTokenEx(userToken, 0xF01FF, IntPtr.Zero, 2, 1, out var primaryToken))
                ThrowLastError("DuplicateTokenEx");

            try
            {
                if (!CreateEnvironmentBlock(out var environment, primaryToken, false))
                    ThrowLastError("CreateEnvironmentBlock");

                try
                {
                    var startup = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
                    var commandLine = new StringBuilder($"\"{executable}\"");
                    var workingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory;
                    const uint createNewConsole = 0x00000010;
                    const uint unicodeEnvironment = 0x00000400;

                    if (!CreateProcessAsUser(
                            primaryToken,
                            executable,
                            commandLine,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            false,
                            createNewConsole | unicodeEnvironment,
                            environment,
                            workingDirectory,
                            ref startup,
                            out var processInformation))
                    {
                        ThrowLastError("CreateProcessAsUser");
                    }

                    CloseHandle(processInformation.hProcess);
                    CloseHandle(processInformation.hThread);
                }
                finally
                {
                    DestroyEnvironmentBlock(environment);
                }
            }
            finally
            {
                CloseHandle(primaryToken);
            }
        }
        finally
        {
            CloseHandle(userToken);
        }
    }

    private static void ThrowLastError(string operation) =>
        throw new InvalidOperationException($"{operation} gagal: {Marshal.GetLastWin32Error()}");

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr primaryToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr token,
        string? applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref STARTUPINFO startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int processId;
        public int threadId;
    }
}

static class AgentLogPath
{
    public static string Resolve()
    {
        var primary = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ocphlabremote-agent", "logs")
            : "/var/log/ocphlabremote-agent";

        try
        {
            Directory.CreateDirectory(primary);
            return primary;
        }
        catch
        {
            return Path.Combine(AppContext.BaseDirectory, "logs");
        }
    }
}

sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly string _fallbackDirectory;
    private readonly object _sync = new();

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
        _fallbackDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() { }

    internal void Write(string categoryName, LogLevel logLevel, string message, Exception? exception)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{logLevel}] [{categoryName}] {message}";
        if (exception is not null)
            line += Environment.NewLine + exception;

        lock (_sync)
        {
            try
            {
                Append(_directory, line);
            }
            catch
            {
                // A service account may not have permission for /var/log or ProgramData.
                // Keep the agent operational and write beside the application instead.
                try { Append(_fallbackDirectory, line); } catch { /* logging must never stop the agent */ }
            }
        }
    }

    private static void Append(string directory, string line)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"agent-{DateTime.Now:yyyy-MM-dd}.log");
        File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
    }
}

sealed class FileLogger(FileLoggerProvider provider, string categoryName) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        provider.Write(categoryName, logLevel, formatter(state, exception), exception);
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
