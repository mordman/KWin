using System.Runtime.InteropServices;
using System.Text;

namespace KeyboardLayoutSwitcher;

// Низкоуровневая работа с окнами, раскладками и процессами.
// Все объявления проверены по документации Win32:
// user32.dll — окна, хуки, раскладки; kernel32.dll — процессы и локали.
internal static class NativeMethods
{
    // ---------- Делегаты ----------
    internal delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    // ---------- Константы ----------
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const int OBJID_WINDOW = 0;
    public const int CHILDID_SELF = 0;

    public const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;

    // GetLocaleInfo: LOCALE_SISO639LANGNAME — двухбуквенный код ISO 639-1 ("ru", "en")
    public const uint LOCALE_SISO639LANGNAME = 0x0059;
    // GetLocaleInfo: LOCALE_SLANGUAGE — полное имя языка ("Russian (Russia)")
    public const uint LOCALE_SLANGUAGE = 0x0002;

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // ---------- Структуры ----------
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;     // окно, владеющее фокусом ввода
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    // ---------- user32.dll ----------
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);

    [DllImport("user32.dll")]
    public static extern int GetKeyboardLayoutList(int nBuff, IntPtr lpList);

    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO pgui);

    // Асинхронная отправка сообщения — штатный способ запросить смену раскладки
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    // ---------- kernel32.dll ----------
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetLocaleInfo(uint Locale, uint LCType, StringBuilder lpLCData, int cchData);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags,
        StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

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

    // Возвращает имя класса окна (помимает диагностику в журнале).
    public static string GetWindowClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
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

    // Текущая раскладка потока, владеющего окном.
    public static IntPtr GetWindowLayout(IntPtr hwnd)
    {
        uint threadId = GetWindowThreadProcessId(hwnd, out _);
        return threadId == 0 ? IntPtr.Zero : GetKeyboardLayout(threadId);
    }

    // Короткий код языка раскладки, например "RU" или "EN".
    public static string LayoutCode(IntPtr hkl)
    {
        if (hkl == IntPtr.Zero) return "??";
        // Младшее слово HKL — идентификатор языка (LANGID)
        uint langId = (uint)hkl.ToInt64() & 0xFFFF;
        var sb = new StringBuilder(16);
        int len = GetLocaleInfo(langId, LOCALE_SISO639LANGNAME, sb, sb.Capacity); // "ru", "en", ...
        if (len > 0)
            return sb.ToString().ToUpperInvariant();
        // запасной вариант — старшее слово HKL (идентификатор самой раскладки)
        return ((uint)hkl.ToInt64() >> 16).ToString("X4");
    }

    // Полное имя языка раскладки, например "Russian (Russia)" — для журнала.
    public static string LanguageName(IntPtr hkl)
    {
        if (hkl == IntPtr.Zero) return "";
        uint langId = (uint)hkl.ToInt64() & 0xFFFF;
        var sb = new StringBuilder(128);
        int len = GetLocaleInfo(langId, LOCALE_SLANGUAGE, sb, sb.Capacity);
        return len > 0 ? sb.ToString() : "";
    }

    // Окно, которому нужно отправить WM_INPUTLANGCHANGEREQUEST:
    // окно с фокусом ввода в потоке активного окна, иначе само активное окно.
    public static IntPtr GetInputTargetWindow(IntPtr hwnd, out uint threadId)
    {
        threadId = GetWindowThreadProcessId(hwnd, out _);
        if (threadId != 0)
        {
            var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (GetGUIThreadInfo(threadId, ref info) && info.hwndFocus != IntPtr.Zero)
                return info.hwndFocus;
        }
        return hwnd;
    }
}
