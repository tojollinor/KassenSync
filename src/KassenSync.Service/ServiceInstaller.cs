using System.Diagnostics;

namespace KassenSync.Service;

internal static class ServiceInstaller
{
    public const string ServiceName = "OrdnerSync Service";
    private const string LegacyServiceName = "KassenSync Service";

    public static int Install()
    {
        StopAndDelete(ServiceName);
        StopAndDelete(LegacyServiceName);

        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Programmpfad konnte nicht bestimmt werden.");
        RunSc("create", ServiceName, "binPath=", $"\"{exe}\"", "start=", "auto", "DisplayName=", ServiceName);
        RunSc("description", ServiceName, "Überwacht den OrdnerSync-Quellordner und kopiert neue Dateien auf das konfigurierte Zielmedium.");
        RunSc("failure", ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/5000/restart/5000");
        RunSc("start", ServiceName);
        return 0;
    }

    public static int Uninstall()
    {
        StopAndDelete(ServiceName);
        StopAndDelete(LegacyServiceName);
        return 0;
    }

    public static int Start()
    {
        RunScIgnoreErrors("start", ServiceName);
        return 0;
    }

    public static int Stop()
    {
        RunScIgnoreErrors("stop", ServiceName);
        return 0;
    }

    public static int Restart()
    {
        RunScIgnoreErrors("stop", ServiceName);
        Thread.Sleep(1200);
        RunSc("start", ServiceName);
        return 0;
    }

    public static int SetAutoStart(bool enabled)
    {
        RunSc("config", ServiceName, "start=", enabled ? "auto" : "demand");
        return 0;
    }

    private static void StopAndDelete(string serviceName)
    {
        RunScIgnoreErrors("stop", serviceName);
        Thread.Sleep(800);
        RunScIgnoreErrors("delete", serviceName);
        Thread.Sleep(250);
    }

    private static void RunSc(params string[] arguments)
    {
        var result = RunScCore(arguments);
        if (result != 0)
            throw new InvalidOperationException($"Windows-Dienst konnte nicht konfiguriert werden. sc.exe ExitCode={result}.");
    }

    private static void RunScIgnoreErrors(params string[] arguments) => _ = RunScCore(arguments);

    private static int RunScCore(IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo("sc.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("sc.exe konnte nicht gestartet werden.");
        process.WaitForExit(15000);
        return process.HasExited ? process.ExitCode : -1;
    }
}
