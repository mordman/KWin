using System.Runtime.InteropServices;

namespace KeyboardLayoutSwitcher;

// Переключение раскладки заданного окна через WM_INPUTLANGCHANGEREQUEST.
internal sealed class LayoutService
{
    private const int TimeoutMs = 300;

    // Применяет раскладку по коду языка ("RU", "EN", ...) к окну hwnd.
    // Возвращает true, если сообщение отправлено.
    public bool ApplyLayout(IntPtr hwnd, string layoutCode)
    {
        IntPtr hkl = FindHklForCode(layoutCode);
        if (hkl == IntPtr.Zero)
            return false; // такой раскладки в системе нет — ничего не делаем

        // WM_INPUTLANGCHANGEREQUEST — штатный способ: окно само применит раскладку,
        // как будто пользователь выбрал её в языковой панели.
        NativeMethods.SendMessageTimeout(
            hwnd,
            NativeMethods.WM_INPUTLANGCHANGEREQUEST,
            (IntPtr)NativeMethods.INPUTLANGCHANGE_FORWARD,
            hkl,
            NativeMethods.SMTO_ABORTIFHUNG,
            TimeoutMs,
            out _);

        return true;
    }

    // Перебирает установленные в системе раскладки и ищет HKL с нужным языком.
    private static IntPtr FindHklForCode(string code)
    {
        var layouts = GetSystemInputLayouts();
        foreach (IntPtr hkl in layouts)
        {
            if (string.Equals(NativeMethods.LayoutCode(hkl), code, StringComparison.OrdinalIgnoreCase))
                return hkl;
        }
        return IntPtr.Zero;
    }

    // Список всех установленных раскладок (для справки и для настроек).
    public static List<IntPtr> GetSystemInputLayouts()
    {
        var result = new List<IntPtr>();
        int count = NativeMethods.GetKeyboardLayoutList(0, IntPtr.Zero);
        if (count <= 0) return result;

        var buffer = new IntPtr[count];
        NativeMethods.GetKeyboardLayoutList(count, buffer);
        result.AddRange(buffer);
        return result;
    }

    // Доступные коды языков, установленные в системе, например ["RU", "EN"].
    public static List<string> GetAvailableLayoutCodes()
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (IntPtr hkl in GetSystemInputLayouts())
            set.Add(NativeMethods.LayoutCode(hkl));
        return set.ToList();
    }
}
