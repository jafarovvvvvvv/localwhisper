using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using LocalWhisper.Services;

namespace LocalWhisper.Interop;

/// <summary>
/// Разобранное сочетание клавиш. Запись та же, что лежит в конфиге и что
/// понимает Popup: <c>ctrl+alt+space</c>.
/// </summary>
public readonly record struct HotkeyCombination(uint Modifiers, uint Key)
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    /// <summary>Не повторять, пока клавишу держат.</summary>
    public const uint MOD_NOREPEAT = 0x4000;

    public bool IsValid => Key != 0;

    public static bool TryParse(string? combination, out HotkeyCombination result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(combination)) return false;

        uint modifiers = 0;
        uint key = 0;

        foreach (var raw in combination.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim().ToLowerInvariant();
            switch (part)
            {
                case "ctrl" or "control": modifiers |= MOD_CONTROL; continue;
                case "alt": modifiers |= MOD_ALT; continue;
                case "shift": modifiers |= MOD_SHIFT; continue;
                case "win" or "windows" or "cmd" or "super" or "meta": modifiers |= MOD_WIN; continue;
            }

            var parsed = part switch
            {
                "space" => 0x20u,
                "enter" or "return" => 0x0Du,
                "tab" => 0x09u,
                "esc" or "escape" => 0x1Bu,
                "backspace" => 0x08u,
                "delete" or "del" => 0x2Eu,
                "insert" or "ins" => 0x2Du,
                "home" => 0x24u,
                "end" => 0x23u,
                "pageup" => 0x21u,
                "pagedown" => 0x22u,
                "up" => 0x26u,
                "down" => 0x28u,
                "left" => 0x25u,
                "right" => 0x27u,
                "`" or "tilde" or "backquote" => 0xC0u,
                { Length: 1 } single when char.IsAsciiLetterOrDigit(single[0]) =>
                    char.ToUpperInvariant(single[0]),
                _ when part.StartsWith('f') && int.TryParse(part[1..], out var n) && n is >= 1 and <= 24 =>
                    (uint)(0x6F + n),
                _ => 0u,
            };

            // Последняя названная клавиша и есть клавиша сочетания: «ctrl+a+b»
            // — запись бессмысленная, и спорить с ней незачем.
            if (parsed != 0) key = parsed;
        }

        if (key == 0) return false;
        result = new HotkeyCombination(modifiers, key);
        return true;
    }

    /// <summary>
    /// Сочетание, разложенное на клавиши для показа отдельными чипами.
    /// Нераспознанное возвращается как есть: показать «ctrl+щ» честнее, чем
    /// промолчать.
    /// </summary>
    public static IReadOnlyList<string> Parts(string? combination)
    {
        if (string.IsNullOrWhiteSpace(combination)) return [];
        return combination
            .Split('+', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim().ToLowerInvariant() switch
            {
                "ctrl" or "control" => "Ctrl",
                "alt" => "Alt",
                "shift" => "Shift",
                "win" or "windows" or "cmd" or "super" or "meta" => "Win",
                "space" => "Space",
                "enter" or "return" => "Enter",
                "tab" => "Tab",
                "esc" or "escape" => "Esc",
                var other => other.Length == 1
                    ? other.ToUpperInvariant()
                    : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(other),
            })
            .ToList();
    }
}

/// <summary>
/// Глобальные клавиши приложения.
///
/// Сначала честная регистрация через <c>RegisterHotKey</c>: система сама
/// доставляет <c>WM_HOTKEY</c>, ничего не перехватывая. Если сочетание уже
/// занято другим приложением, регистрация не проходит — и тогда включается
/// низкоуровневый перехват клавиатуры, который стоит раньше в очереди и умеет
/// забрать комбинацию себе. Так было и в питоновской версии, и это единственный
/// способ отобрать, скажем, <c>ctrl+space</c> у переключателя раскладки.
///
/// **Окно, на котором зарегистрированы клавиши, нельзя пересоздавать.**
/// <c>WM_HOTKEY</c> приходит именно на этот HWND, и новое окно означает, что все
/// сочетания молча перестали работать. Поэтому клавиши висят на окне пилюли,
/// которое живёт от старта до выхода и прячется прозрачностью, а не закрытием.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly IntPtr _hwnd;
    private readonly HwndSource? _source;
    private readonly Dictionary<int, Action> _actions = [];
    private readonly List<int> _registered = [];
    private readonly KeyboardHook _hook = new();
    private int _nextId = 1;

    public HotkeyManager(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
    }

    /// <summary>
    /// Перевешивает все клавиши разом. Возвращает те сочетания, которые не
    /// удалось занять ни одним из способов, — их показывают человеку, потому
    /// что молча не работающая клавиша выглядит как поломка приложения.
    /// </summary>
    public IReadOnlyList<string> Apply(IEnumerable<(string Combination, Action Action)> bindings)
    {
        Clear();

        var failed = new List<string>();
        foreach (var (combination, action) in bindings)
        {
            if (string.IsNullOrWhiteSpace(combination)) continue;
            if (!HotkeyCombination.TryParse(combination, out var parsed))
            {
                Log.Warn("hotkey", $"не разобрать сочетание: {combination}");
                failed.Add(combination);
                continue;
            }

            var id = _nextId++;
            if (RegisterHotKey(_hwnd, id, parsed.Modifiers | HotkeyCombination.MOD_NOREPEAT, parsed.Key))
            {
                _registered.Add(id);
                _actions[id] = action;
                Log.Info("hotkey", $"занято системой: {combination}");
                continue;
            }

            if (_hook.Add(parsed, action))
            {
                Log.Info("hotkey", $"занято перехватом: {combination}");
                continue;
            }

            Log.Warn("hotkey", $"не занять вовсе: {combination}");
            failed.Add(combination);
        }

        return failed;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return IntPtr.Zero;
        if (!_actions.TryGetValue(wParam.ToInt32(), out var action)) return IntPtr.Zero;
        handled = true;
        action();
        return IntPtr.Zero;
    }

    private void Clear()
    {
        foreach (var id in _registered) UnregisterHotKey(_hwnd, id);
        _registered.Clear();
        _actions.Clear();
        _hook.Clear();
        _nextId = 1;
    }

    public void Dispose()
    {
        Clear();
        _hook.Dispose();
        _source?.RemoveHook(WndProc);
    }
}

/// <summary>
/// Низкоуровневый перехват клавиатуры — запасной путь для сочетаний, которые
/// система отдавать не хочет.
///
/// Пока список пуст, перехватчик не установлен вовсе: он стоит на пути каждого
/// нажатия во всей системе, и держать его «на всякий случай» — плохая плата за
/// то, чем чаще всего не пользуются.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int HC_ACTION = 0;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private readonly List<(HotkeyCombination Combination, Action Action)> _bindings = [];
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly HashSet<uint> _swallowedKeys = [];

    // Делегат обязан пережить установку хука: собранный сборщиком мусора, он
    // роняет процесс при первом же нажатии клавиши.
    private HookProc? _proc;
    private IntPtr _handle;

    public bool Add(HotkeyCombination combination, Action action)
    {
        if (!combination.IsValid) return false;
        _bindings.Add((combination, action));
        return Install();
    }

    public void Clear()
    {
        _bindings.Clear();
        _swallowedKeys.Clear();
        Uninstall();
    }

    private bool Install()
    {
        if (_handle != IntPtr.Zero) return true;
        _proc = Callback;
        // Для WH_KEYBOARD_LL модуль не нужен, и поток — этот: обратный вызов
        // придёт туда, где крутится очередь сообщений, то есть в UI.
        _handle = SetWindowsHookExW(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
        if (_handle == IntPtr.Zero)
        {
            Log.Warn("hotkey", $"перехват клавиатуры не установился: {Marshal.GetLastWin32Error()}");
            _proc = null;
            return false;
        }
        return true;
    }

    private void Uninstall()
    {
        if (_handle == IntPtr.Zero) return;
        UnhookWindowsHookEx(_handle);
        _handle = IntPtr.Zero;
        _proc = null;
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode != HC_ACTION) return CallNextHookEx(_handle, nCode, wParam, lParam);

        var message = wParam.ToInt32();
        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

        if (message is WM_KEYUP or WM_SYSKEYUP)
        {
            // Отпускание съеденной клавиши съедаем тоже: приложение, увидевшее
            // отпускание без нажатия, ведёт себя странно ровно в тех местах,
            // где это труднее всего заметить.
            if (_swallowedKeys.Remove(data.vkCode)) return 1;
            return CallNextHookEx(_handle, nCode, wParam, lParam);
        }

        if (message is not (WM_KEYDOWN or WM_SYSKEYDOWN))
            return CallNextHookEx(_handle, nCode, wParam, lParam);

        foreach (var (combination, action) in _bindings)
        {
            if (combination.Key != data.vkCode) continue;
            if (CurrentModifiers() != combination.Modifiers) continue;

            // Зажатая клавиша повторяется автоматически. Одно нажатие — одно
            // срабатывание, иначе удержание запускало бы и останавливало запись
            // десяток раз подряд. У системной регистрации за это отвечает
            // MOD_NOREPEAT; здесь — этот набор.
            if (!_swallowedKeys.Add(data.vkCode)) return 1;
            // Обратный вызов стоит на пути всего ввода в системе: пока он
            // работает, клавиатура ждёт. Поэтому здесь только «поставить в
            // очередь», а делает всё уже обычный оборот диспетчера.
            _dispatcher.BeginInvoke(action);
            return 1;
        }

        return CallNextHookEx(_handle, nCode, wParam, lParam);
    }

    /// <summary>
    /// Какие модификаторы зажаты прямо сейчас. Сравнение потом идёт на точное
    /// совпадение: иначе <c>ctrl+shift+space</c> заодно срабатывал бы как
    /// <c>ctrl+space</c>, и два режима запускались бы одной клавишей.
    /// </summary>
    private static uint CurrentModifiers()
    {
        uint modifiers = 0;
        if (Down(Win32.VK_CONTROL)) modifiers |= HotkeyCombination.MOD_CONTROL;
        if (Down(Win32.VK_MENU)) modifiers |= HotkeyCombination.MOD_ALT;
        if (Down(Win32.VK_SHIFT)) modifiers |= HotkeyCombination.MOD_SHIFT;
        if (Down(Win32.VK_LWIN) || Down(Win32.VK_RWIN)) modifiers |= HotkeyCombination.MOD_WIN;
        return modifiers;
    }

    private static bool Down(int key) => (Win32.GetAsyncKeyState(key) & 0x8000) != 0;

    public void Dispose() => Clear();
}
