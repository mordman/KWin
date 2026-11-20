using System.Runtime.InteropServices;
using System.Text;

namespace KeyboardLayoutSwitcher;

// Низкоуровневая работа с окнами, раскладками и процессами.
internal static class NativeMethods
{
    // ---------- Делегаты ----------
    internal delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    // ---------- Константы ----------
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const int OBJID_WINDOW = 0;
    public const int CHILDID_SELF = 0;

    public const int WM_INPUTLANGCHANGEREQUEST = 0x0050;
    public const int INPUTLANGCHANGE_FORWARD = 0x0002;
    public const int INPUTLANGCHANGE_BACKWARD = 0x0004;

    public const uint GWL_EXSTYLE = unchecked((uint)-20);
    public const int WS_EX_NOACTIVATE = 0x08000000;

    // ---------- Импорты ----------
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
        IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess,
        uint idThread, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);

    [DllImport("user32.dll")]
    public static extern int GetKeyboardLayoutList(int nBuff, IntPtr lpList);

    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetLocaleInfo(uint Locale, uint LCType, StringBuilder lpLCData, int cchData);

    // Отправка сообщения окну
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    public const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags,
        StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // ---------- Вспомогательные методы ----------

    // Возвращает имя exe-файла процесса-владельца окна (нижний регистр), либо null.
    public static string? GetProcessExeName(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return null;

        IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            if (!QueryFullProcessImageName(hProcess, 0, sb, ref size))
                return null;
            return Path.GetFileName(sb.ToString()).ToLowerInvariant();
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    // Возвращает заголовок окна.
    public static string GetWindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    // Текущая раскладка активного окна.
    public static IntPtr GetForegroundLayout()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;
        uint threadId = GetWindowThreadProcessId(hwnd, out _);
        return GetKeyboardLayout(threadId);
    }

    // Короткий код языка раскладки, например "RU" или "EN".
    public static string LayoutCode(IntPtr hkl)
    {
        if (hkl == IntPtr.Zero) return "??";
        uint lcid = (uint)hkl.ToInt64() & 0xFFFF;
        var sb = new StringBuilder(16);
        int len = GetLocaleInfo(lcid, 0x0002 /*LOCALE_SISO639LANGNAME*/, sb, sb.Capacity); // "ru", "en"
        if (len > 0)
            return sb.ToString().ToUpperInvariant();
        // запасной вариант: старшие байты
        return ((uint)hkl.ToInt64() >> 16).ToString("X4");
    }
}
