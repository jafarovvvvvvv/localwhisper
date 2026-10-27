using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace LocalWhisper.Interop;

/// <summary>
/// Всё, что приложение просит у Windows напрямую.
///
/// Пилюля живёт не по правилам обычного окна: она поверх всех, не берёт фокус,
/// не появляется в Alt-Tab и большую часть времени пропускает клики насквозь.
/// Ни одного из этих свойств WPF своими средствами не выставляет, поэтому окно
/// настраивается по хэндлу сразу после создания.
/// </summary>
internal static class Win32
{
    // MARK: - Стили окна

    public const int GWL_EXSTYLE = -20;

    /// <summary>Клики проходят насквозь.</summary>
    public const int WS_EX_TRANSPARENT = 0x00000020;
    /// <summary>Окно не всплывает в Alt-Tab и на панели задач.</summary>
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    /// <summary>Клик по окну не забирает фокус у того, с чем работает человек.</summary>
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static long GetExStyle(IntPtr hWnd) => (long)GetWindowLongPtr64(hWnd, GWL_EXSTYLE);

    public static void SetExStyle(IntPtr hWnd, long value) =>
        SetWindowLongPtr64(hWnd, GWL_EXSTYLE, (IntPtr)value);

    /// <summary>Добавляет или снимает один ex-стиль, не трогая остальные.</summary>
    public static void ToggleExStyle(IntPtr hWnd, int flag, bool on)
    {
        var style = GetExStyle(hWnd);
        // Через uint: int со старшим битом разошёлся бы знаком по всем 64
        // разрядам и снёс бы половину чужих стилей.
        var mask = (long)(uint)flag;
        var next = on ? style | mask : style & ~mask;
        if (next != style) SetExStyle(hWnd, next);
    }

    // MARK: - Окна

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);

    /// <summary>
    /// Куда поедет текст. Имя приложения вытаскивается из хвоста заголовка —
    /// «файл.txt — Блокнот» превращается в «Блокнот», — как это делала
    /// питоновская версия: в статистике интереснее программа, а не документ.
    /// </summary>
    public static string ForegroundWindowTitle()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "Desktop";

            var length = GetWindowTextLengthW(hwnd);
            if (length <= 0) return "Desktop";

            var buffer = new StringBuilder(length + 1);
            GetWindowTextW(hwnd, buffer, buffer.Capacity);
            var title = buffer.ToString().Trim();
            if (title.Length == 0) return "Desktop";

            var separator = title.LastIndexOf(" - ", StringComparison.Ordinal);
            return separator >= 0 ? title[(separator + 3)..] : title;
        }
        catch
        {
            return "Unknown App";
        }
    }

    // MARK: - Курсор и экран

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    /// <summary>
    /// Положение курсора в физических пикселях экрана.
    ///
    /// Единственный источник правды о том, где мышь, который не зависит от
    /// маршрутизации событий: пилюля почти всегда click-through, и её
    /// собственные MouseMove до неё просто не доходят.
    /// </summary>
    public static Point CursorPosition() =>
        GetCursorPos(out var p) ? new Point(p.X, p.Y) : new Point(-1, -1);

    private enum MonitorDpiType
    {
        Effective = 0,
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    /// <summary>Масштаб монитора, на котором лежит точка (1.0 при 100%, 1.5 при 150%).</summary>
    public static double DpiScaleAt(int x, int y)
    {
        var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return 1.0;
        if (GetDpiForMonitor(monitor, MonitorDpiType.Effective, out var dpiX, out _) != 0) return 1.0;
        return dpiX / 96.0;
    }

    /// <summary>Хэндл окна, или <c>IntPtr.Zero</c> пока оно не показано.</summary>
    public static IntPtr HandleOf(Window window) => new WindowInteropHelper(window).Handle;

    // MARK: - Ввод

    public const int INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_MENU = 0x12;
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_RWIN = 0x5C;

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int vKey);

    public static INPUT KeyInput(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = up ? KEYEVENTF_KEYUP : 0,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        },
    };

    /// <summary>Один символ как есть, минуя раскладку: так уезжает и кириллица, и эмодзи.</summary>
    public static INPUT CharInput(char character, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = character,
                dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0),
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        },
    };

    public static void Send(params INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    // MARK: - Звук

    /// <summary>
    /// Тот же сигнал, что давал <c>winsound.Beep</c>, — это он и есть:
    /// <c>winsound</c> ничего не изобретает, а зовёт эту функцию.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Beep(uint frequency, uint duration);
}
