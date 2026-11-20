namespace KeyboardLayoutSwitcher;

// Простой потокобезопасный журнал переключений:
// %APPDATA%\KeyboardLayoutSwitcher\switcher.log
// При превышении 2 МБ текущий файл переименовывается в switcher.log.old.
internal static class Logger
{
    private static readonly object Gate = new();
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KeyboardLayoutSwitcher");
    private static readonly string LogPathValue = Path.Combine(LogDir, "switcher.log");
    private const long MaxBytes = 2 * 1024 * 1024;

    public static bool Enabled = true;
    public static string LogPath => LogPathValue;

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDir);
                if (File.Exists(LogPathValue) && new FileInfo(LogPathValue).Length > MaxBytes)
                {
                    string old = LogPathValue + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(LogPathValue, old);
                }
                File.AppendAllText(LogPathValue,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // журнал не должен ронять приложение
        }
    }
}
