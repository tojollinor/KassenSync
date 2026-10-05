using System.Diagnostics;

namespace KassenSync.Service;

internal static class ServiceInstaller
{
    private const string ServiceName = "KassenSync Service";

    public static int Install()
    {
        StopAndDelete();
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Programmpfad konnte nicht bestimmt werden.");

        RunSc("create", ServiceName, "binPath=", $"\"{exe}\"", "start=", "auto", "DisplayName=", ServiceName);
        RunSc("description", ServiceName, "Überwacht den KassenSync-Quellordner und kopiert neue Dateien auf das konfigurierte Zielmedium.");
        RunSc("failure", ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/5000/restart/5000");
        RunSc("start", ServiceName);
        return 0;
    }

    public static int Uninstall()
    {
        StopAndDelete();
        return 0;
    }

    public static int Stop()
    {
        RunScIgnoreErrors("stop", ServiceName);
        return 0;
    }

    private static void StopAndDelete()
    {
        RunScIgnoreErrors("stop", ServiceName);
        Thread.Sleep(1200);
        RunScIgnoreErrors("delete", ServiceName);
        Thread.Sleep(300);
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
