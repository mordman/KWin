using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyboardLayoutSwitcher;

// Ядро приложения: значок в трее, хук на смену активного окна, переключение раскладки.
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly AppSettings _settings;
    private readonly LayoutService _layoutService = new();
    private readonly System.Windows.Forms.Timer _iconTimer;   // опрос текущей раскладки для значка
    private readonly System.Windows.Forms.Timer _verifyTimer; // отложенная проверка фактической смены

    private IntPtr _winEventHook;
    private NativeMethods.WinEventDelegate? _winEventProc; // держим ссылку, чтобы GC не собрал делегат
    private bool _paused;
    private bool _disposed;
    private string _lastIconCode = "";

    // Данные отложенной проверки фактической смены раскладки
    private bool _verifyPending;
    private IntPtr _verifyHwnd;
    private uint _verifyThreadId;
    private string _verifyExpected = "";

    public TrayApplicationContext()
    {
        _settings = AppSettings.Load();
        Logger.Enabled = _settings.EnableLogging;

        Logger.Info("=== Старт KeyboardLayoutSwitcher ===");
        Logger.Info($"Версия {Application.ProductVersion}; {Environment.OSVersion.VersionString}; " +
                    $"64-разрядный процесс: {Environment.Is64BitProcess}");
        LogInstalledLayouts();
        LogRules();

        var menu = new ContextMenuStrip();
        menu.Items.Add("&Settings...", null, (s, e) => ShowSettings());
        menu.Items.Add("&Pause switching", null, (s, e) => TogglePause((ToolStripMenuItem)s!));
        menu.Items.Add("&Open log", null, (s, e) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("E&xit", null, (s, e) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true,
            Text = "Keyboard Layout Switcher",
        };
        _trayIcon.DoubleClick += (s, e) => ShowSettings();
        UpdateTrayIcon();

        // Периодически обновляем значок: ловит и ручное переключение (Win+Space)
        _iconTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _iconTimer.Tick += (s, e) => UpdateTrayIcon();
        _iconTimer.Start();

        // Отложенная проверка: действительно ли применилась запрошенная раскладка
        _verifyTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _verifyTimer.Tick += (s, e) => { _verifyTimer.Stop(); DoVerify(); };

        InstallHook();
        TryApplyToForeground("старт");
    }

    // ---------- Стартовая диагностика ----------

    private void LogInstalledLayouts()
    {
        var layouts = LayoutService.GetSystemInputLayouts();
        if (layouts.Count == 0)
        {
            Logger.Warn("GetKeyboardLayoutList не вернул ни одной раскладки");
            return;
        }
        foreach (IntPtr hkl in layouts)
            Logger.Info($"Установленная раскладка: {NativeMethods.LayoutCode(hkl)} " +
                        $"(HKL 0x{hkl.ToInt64():X}, {NativeMethods.LanguageName(hkl)})");
    }

    private void LogRules()
    {
        if (_settings.Rules.Count == 0)
            Logger.Warn("Правила не заданы — переключение выполняться не будет");
        else
            foreach (var rule in _settings.Rules)
                Logger.Info($"Правило: {rule.ProcessName} → {rule.Layout}");
    }

    // ---------- Хук на смену активного окна ----------

    private void InstallHook()
    {
        _winEventProc = WinEventProc;
        _winEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,   // событие «окно стало активным»
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventProc,
            0, 0, 0);
        if (_winEventHook == IntPtr.Zero)
            Logger.Error($"SetWinEventHook не удался, Win32-ошибка {Marshal.GetLastWin32Error()}");
        else
            Logger.Info("Хук на смену активного окна установлен");
    }

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Интересуют только реальные окна
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)
            return;

        if (_paused) return;

        TryApplyToWindow(hwnd, "активация окна");
    }

    // ---------- Переключение ----------

    private void TryApplyToForeground(string reason)
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
            TryApplyToWindow(hwnd, reason);
    }

    private void TryApplyToWindow(IntPtr hwnd, string reason)
    {
        try
        {
            string? exe = NativeMethods.GetProcessExeName(hwnd);
            if (string.IsNullOrEmpty(exe))
            {
                Logger.Info($"[{reason}] окно 0x{hwnd.ToInt64():X}: не удалось определить процесс, пропускаем");
                return;
            }

            string title = Truncate(NativeMethods.GetWindowTitle(hwnd), 60);
            string className = NativeMethods.GetWindowClassName(hwnd);
            string current = NativeMethods.LayoutCode(NativeMethods.GetWindowLayout(hwnd));

            var rule = _settings.Rules.FirstOrDefault(r =>
                string.Equals(r.ProcessName?.Trim().ToLowerInvariant(), exe, StringComparison.Ordinal));

            if (rule == null || string.IsNullOrEmpty(rule.Layout))
            {
                Logger.Info($"[{reason}] {exe} (класс «{className}», «{title}»): правило не задано, раскладка не меняется");
                return;
            }

            Logger.Info($"[{reason}] {exe} (класс «{className}», «{title}»): правило → {rule.Layout}, сейчас {current}");

            if (string.Equals(current, rule.Layout, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info("Раскладка уже соответствует правилу — переключение не требуется");
                return;
            }

            if (_layoutService.ApplyLayout(hwnd, rule.Layout))
                ScheduleVerify(hwnd, rule.Layout);
        }
        catch (Exception ex)
        {
            Logger.Error($"Ошибка при обработке окна 0x{hwnd.ToInt64():X}: {ex.Message}");
        }
    }

    // ---------- Отложенная проверка фактической смены ----------

    private void ScheduleVerify(IntPtr hwnd, string expectedCode)
    {
        _verifyHwnd = hwnd;
        _verifyThreadId = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        _verifyExpected = expectedCode;
        _verifyPending = true;
        _verifyTimer.Stop();
        _verifyTimer.Start();
    }

    private void DoVerify()
    {
        if (!_verifyPending) return;
        _verifyPending = false;

        string now = NativeMethods.LayoutCode(NativeMethods.GetKeyboardLayout(_verifyThreadId));
        if (string.Equals(now, _verifyExpected, StringComparison.OrdinalIgnoreCase))
        {
            Logger.Info($"Проверка: раскладка окна 0x{_verifyHwnd.ToInt64():X} теперь {now} — переключение применено");
        }
        else
        {
            Logger.Warn($"Проверка: раскладка окна 0x{_verifyHwnd.ToInt64():X} по-прежнему {now}, а ожидалась " +
                        $"{_verifyExpected}. Возможно, окно запущено от администратора — тогда и переключатель " +
                        "нужно запустить от администратора (UIPI блокирует сообщения повышенному окну)");
        }
    }

    // ---------- Трей ----------

    private void TogglePause(ToolStripMenuItem item)
    {
        _paused = !_paused;
        item.Checked = _paused;
        Logger.Info(_paused ? "Автопереключение приостановлено" : "Автопереключение возобновлено");
        UpdateTrayIcon();
    }

    private void UpdateTrayIcon()
    {
        IntPtr hkl = NativeMethods.GetForegroundLayout();
        string code = _paused ? "||" : NativeMethods.LayoutCode(hkl);
        if (code == _lastIconCode) return; // не дёргаем иконку без изменений

        _lastIconCode = code;
        var oldIcon = _trayIcon.Icon;
        _trayIcon.Icon = IconHelper.CreateTextIcon(code, _paused);
        oldIcon?.Dispose();
        _trayIcon.Text = _paused
            ? "Keyboard Layout Switcher (paused)"
            : $"Keyboard Layout Switcher — {code}";
    }

    internal void RefreshTray() => UpdateTrayIcon();

    private void OpenLog()
    {
        try
        {
            string path = Logger.LogPath;
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(Path.GetDirectoryName(path)!) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error($"Не удалось открыть журнал: {ex.Message}");
            MessageBox.Show($"Не удалось открыть журнал.{Environment.NewLine}{Logger.LogPath}",
                "Keyboard Layout Switcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowSettings()
    {
        using var form = new SettingsForm(_settings, this);
        form.ShowDialog();
        Logger.Enabled = _settings.EnableLogging;
        // После закрытия настроек фокус возвращается в прежнее окно — применяем его правило
        TryApplyToForeground("возврат фокуса после настроек");
    }

    private void ExitApp()
    {
        Logger.Info("Выход по команде меню");
        _trayIcon.Visible = false;
        Application.Exit();
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..(max - 1)] + "…";

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
        if (_winEventHook != IntPtr.Zero)
            NativeMethods.UnhookWinEvent(_winEventHook);
        _iconTimer.Stop();
        _iconTimer.Dispose();
        _verifyTimer.Stop();
        _verifyTimer.Dispose();
        _trayIcon.Dispose();
        base.Dispose(disposing);
    }
}
