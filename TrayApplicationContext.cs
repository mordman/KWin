using System.Runtime.InteropServices;

namespace KeyboardLayoutSwitcher;

// Ядро приложения: значок в трее, хук на смену активного окна, переключение раскладки.
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly AppSettings _settings;
    private readonly LayoutService _layoutService = new();
    private readonly System.Windows.Forms.Timer _iconTimer; // опрос текущей раскладки для значка

    private IntPtr _winEventHook;
    private NativeMethods.WinEventDelegate? _winEventProc; // держим ссылку, чтобы GC не собрал делегат
    private bool _paused;
    private bool _disposed;
    private string _lastIconCode = "";

    public TrayApplicationContext()
    {
        _settings = AppSettings.Load();

        var menu = new ContextMenuStrip();
        menu.Items.Add("&Settings...", null, (s, e) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("&Pause switching", null, (s, e) => TogglePause((ToolStripMenuItem)s!));
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

        InstallHook();
        // При старте применяем правило к текущему активному окну
        TryApplyToForeground();
    }

    // ---------- Хук на смену активного окна ----------

    private void InstallHook()
    {
        _winEventProc = WinEventProc;
        _winEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,   // событие "окно стало активным"
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventProc,
            0, 0, 0);
    }

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Интересуют только реальные окна
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)
            return;

        if (_paused) return;

        // События приходят на поток с накачкой сообщений; для наших собственных окон
        // (настройки, пикер) правила нет — сработает проверка в TryApplyToWindow.
        TryApplyToWindow(hwnd);
    }

    // ---------- Переключение ----------

    private void TryApplyToForeground()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
            TryApplyToWindow(hwnd);
    }

    private void TryApplyToWindow(IntPtr hwnd)
    {
        string? exe = NativeMethods.GetProcessExeName(hwnd);
        if (string.IsNullOrEmpty(exe)) return;

        var rule = _settings.Rules.FirstOrDefault(r =>
            string.Equals(r.ProcessName?.Trim().ToLowerInvariant(), exe, StringComparison.Ordinal));
        if (rule == null || string.IsNullOrEmpty(rule.Layout)) return; // правило не задано — не трогаем

        if (_layoutService.ApplyLayout(hwnd, rule.Layout) && _settings.ShowBalloonOnSwitch)
        {
            _trayIcon.BalloonTipTitle = "Keyboard Layout Switcher";
            _trayIcon.BalloonTipText = $"{exe} → {rule.Layout}";
            _trayIcon.ShowBalloonTip(1500);
        }
    }

    // ---------- Трей ----------

    private void TogglePause(ToolStripMenuItem item)
    {
        _paused = !_paused;
        item.Checked = _paused;
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

    private void ShowSettings()
    {
        using var form = new SettingsForm(_settings, this);
        form.ShowDialog();
        // После закрытия настроек фокус возвращается в прежнее окно — применяем его правило
        TryApplyToForeground();
    }

    private void ExitApp()
    {
        _trayIcon.Visible = false;
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
        if (_winEventHook != IntPtr.Zero)
            NativeMethods.UnhookWinEvent(_winEventHook);
        _iconTimer.Stop();
        _iconTimer.Dispose();
        _trayIcon.Dispose();
        base.Dispose(disposing);
    }
}
