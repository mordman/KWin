using System.Diagnostics;

namespace KeyboardLayoutSwitcher;

// Ярлык автозапуска в HKCU\Software\Microsoft\Windows\CurrentVersion\Run
// (реестр текущего пользователя — права администратора не нужны).
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "KeyboardLayoutSwitcher";

    public static bool IsEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        var value = key?.GetValue(AppName) as string;
        return !string.IsNullOrEmpty(value);
    }

    public static void SetEnabled(bool enable)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable)
            key.SetValue(AppName, "\"" + Process.GetCurrentProcess().MainModule?.FileName + "\"");
        else if (key.GetValue(AppName) != null)
            key.DeleteValue(AppName);
    }
}
