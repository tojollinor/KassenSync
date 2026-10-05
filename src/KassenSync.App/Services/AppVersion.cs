using System.Reflection;

namespace KassenSync.App.Services;

public static class AppVersion
{
    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
    public static string Display => $"{Current.Major}.{Current.Minor}.{Current.Build}";
}
