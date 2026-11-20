using System.Runtime.InteropServices;

namespace KeyboardLayoutSwitcher;

// Переключение раскладки заданного окна через WM_INPUTLANGCHANGEREQUEST.
internal sealed class LayoutService
{
    // Применяет раскладку по коду языка ("RU", "EN", ...) к окну hwnd.
    // Возвращает true, если запрос отправлен; фактическую смену подтверждает
    // отложенная проверка в TrayApplicationContext — раскладку меняет само
    // целевое окно, когда обработает сообщение.
    public bool ApplyLayout(IntPtr hwnd, string layoutCode)
    {
        IntPtr hkl = FindHklForCode(layoutCode);
        if (hkl == IntPtr.Zero)
        {
            Logger.Error($"Раскладка '{layoutCode}' не установлена в системе. Доступные коды: " +
                         $"[{string.Join(", ", GetAvailableLayoutCodes())}]");
            return false;
        }

        // Сообщение должно получить окно с фокусом ввода целевого потока
        // (например, edit-поле в Блокноте); иначе — само окно верхнего уровня.
        IntPtr target = NativeMethods.GetInputTargetWindow(hwnd, out uint threadId);
        bool posted = NativeMethods.PostMessage(
            target, NativeMethods.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);

        if (!posted)
        {
            Logger.Error($"PostMessage(WM_INPUTLANGCHANGEREQUEST) окну 0x{target.ToInt64():X} не удался, " +
                         $"Win32-ошибка {Marshal.GetLastWin32Error()}");
            return false;
        }

        Logger.Info($"Запрос раскладки {layoutCode} (HKL 0x{hkl.ToInt64():X}) отправлен окну " +
                    $"0x{target.ToInt64():X} потока {threadId} (активное окно 0x{hwnd.ToInt64():X})");
        return true;
    }

    // Перебирает установленные в системе раскладки и ищет HKL с нужным языком.
    private static IntPtr FindHklForCode(string code)
    {
        foreach (IntPtr hkl in GetSystemInputLayouts())
        {
            if (string.Equals(NativeMethods.LayoutCode(hkl), code, StringComparison.OrdinalIgnoreCase))
                return hkl;
        }
        return IntPtr.Zero;
    }

    // Список всех установленных раскладок.
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

    // Доступные коды языков, установленные в системе, например ["EN", "RU"].
    public static List<string> GetAvailableLayoutCodes()
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (IntPtr hkl in GetSystemInputLayouts())
            set.Add(NativeMethods.LayoutCode(hkl));
        return set.ToList();
    }
}
