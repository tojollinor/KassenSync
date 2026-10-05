using KassenSync.Core.Services;
using KassenSync.Service;

if (args.Any(x => string.Equals(x, "--install-service", StringComparison.OrdinalIgnoreCase)))
{
    Environment.ExitCode = ServiceInstaller.Install();
    return;
}

if (args.Any(x => string.Equals(x, "--uninstall-service", StringComparison.OrdinalIgnoreCase)))
{
    Environment.ExitCode = ServiceInstaller.Uninstall();
    return;
}

if (args.Any(x => string.Equals(x, "--stop-service", StringComparison.OrdinalIgnoreCase)))
{
    Environment.ExitCode = ServiceInstaller.Stop();
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "KassenSync Service");
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<IndexDatabase>();
builder.Services.AddSingleton<FileHashService>();
builder.Services.AddSingleton<FileStabilityService>();
builder.Services.AddSingleton<FolderIndexer>();
builder.Services.AddSingleton<TargetPathService>();
builder.Services.AddSingleton<FileCopyService>();
builder.Services.AddSingleton<CopyStateStore>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<FolderWatchHostedService>();
builder.Services.AddHostedService<CopyQueueHostedService>();
builder.Services.AddHostedService<IpcServerHostedService>();

var host = builder.Build();
await host.Services.GetRequiredService<IndexDatabase>().InitializeAsync();
await host.RunAsync();
