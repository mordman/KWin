namespace KeyboardLayoutSwitcher;

// Точка входа. Это НЕ Windows-служба (службы живут в сессии 0 и не имеют доступа
// к пользовательскому рабочему столу), а обычное WinForms-приложение в трее.
internal static class Program
{
    private const string MutexName = "KeyboardLayoutSwitcher__SingleInstance";

    [STAThread]
    private static void Main()
    {
        // Защита от второго запуска
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
            return; // уже запущено — тихо выходим

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApplicationContext());
    }
}
