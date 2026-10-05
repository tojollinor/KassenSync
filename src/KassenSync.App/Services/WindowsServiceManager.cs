using System.IO;
using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;

namespace KassenSync.App.Services;

public sealed record WindowsServiceInfo(
    bool Installed,
    string ServiceName,
    string StatusText,
    bool? AutoStartEnabled);

public sealed class WindowsServiceManager
{
    public const string ServiceName = "OrdnerSync Service";
    private const string LegacyServiceName = "KassenSync Service";

    public WindowsServiceInfo GetInfo()
    {
        foreach (var service in ServiceController.GetServices())
        {
            using (service)
            {
                if (string.Equals(service.ServiceName, ServiceName, StringComparison.OrdinalIgnoreCase))
                    return new WindowsServiceInfo(
                        true,
                        ServiceName,
                        Translate(service.Status),
                        ReadAutoStart(ServiceName));
            }
        }

        foreach (var service in ServiceController.GetServices())
        {
            using (service)
            {
                if (string.Equals(service.ServiceName, LegacyServiceName, StringComparison.OrdinalIgnoreCase))
                    return new WindowsServiceInfo(
                        true,
                        LegacyServiceName,
                        $"Alt: {Translate(service.Status)}",
                        ReadAutoStart(LegacyServiceName));
            }
        }

        return new WindowsServiceInfo(false, ServiceName, "Nicht installiert", null);
    }

    public async Task RunElevatedActionAsync(string argument)
    {
        var serviceExe = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "Service", "OrdnerSync.Service.exe"));

        if (!File.Exists(serviceExe))
            throw new FileNotFoundException(
                "Die OrdnerSync-Dienstdatei wurde nicht gefunden.",
                serviceExe);

        using var process = Process.Start(new ProcessStartInfo(serviceExe)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = argument,
            WorkingDirectory = Path.GetDirectoryName(serviceExe)!
        }) ?? throw new InvalidOperationException(
            "Die Dienstaktion konnte nicht gestartet werden.");

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Die Dienstaktion ist fehlgeschlagen. ExitCode={process.ExitCode}");
    }

    private static bool? ReadAutoStart(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{serviceName}");

            if (key?.GetValue("Start") is not int startValue)
                return null;

            return startValue == 2;
        }
        catch
        {
            return null;
        }
    }

    private static string Translate(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => "Läuft",
        ServiceControllerStatus.Stopped => "Gestoppt",
        ServiceControllerStatus.StartPending => "Wird gestartet",
        ServiceControllerStatus.StopPending => "Wird gestoppt",
        ServiceControllerStatus.Paused => "Pausiert",
        ServiceControllerStatus.PausePending => "Wird pausiert",
        ServiceControllerStatus.ContinuePending => "Wird fortgesetzt",
        _ => status.ToString()
    };
}
