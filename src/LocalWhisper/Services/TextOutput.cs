using LocalWhisper.Interop;

namespace LocalWhisper.Services;

/// <summary>
/// Как расшифровка попадает туда, где стоял курсор.
///
/// Способа два, и первый — через буфер обмена — стоит по умолчанию не случайно:
/// он кладёт весь текст одним движением, не зависит от раскладки и не ломается
/// на кириллице. Посимвольный набор остаётся для полей, которые вставку из
/// буфера не принимают вовсе.
/// </summary>
public static class TextOutput
{
    /// <summary>
    /// Сколько ждать между «положил в буфер» и «нажал Ctrl+V».
    ///
    /// Буферу нужно время осесть: приложение-получатель узнаёт о новом
    /// содержимом уведомлением, и Ctrl+V, посланный в ту же миллисекунду,
    /// успевает вставить прошлое. Сто двадцать миллисекунд — то же число, что
    /// стояло в питоновской версии, и оно проверено на этой машине.
    /// </summary>
    private static readonly TimeSpan ClipboardSettle = TimeSpan.FromMilliseconds(120);

    public static async Task PasteAsync(string text, string method)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (!string.Equals(method, "clipboard", StringComparison.OrdinalIgnoreCase))
        {
            TypeText(text);
            return;
        }

        if (!TrySetClipboard(text))
        {
            // Буфер занят другим приложением — набираем текст руками, лишь бы
            // диктовка не пропала.
            Log.Warn("paste", "буфер обмена недоступен, набираю посимвольно");
            TypeText(text);
            return;
        }

        await Task.Delay(ClipboardSettle).ConfigureAwait(true);
        SendPaste();
        // Текст намеренно остаётся в буфере: если курсор оказался не там,
        // человек нажмёт Ctrl+V ещё раз, а не будет диктовать заново.
    }

    /// <summary>
    /// Положить в буфер, переживая чужую блокировку.
    ///
    /// Буфер обмена в Windows — общий ресурс с одним владельцем за раз, и любое
    /// приложение, открывшее его в этот момент, отдаёт нам отказ. Пять попыток
    /// через полсотни миллисекунд закрывают почти все такие случаи.
    /// </summary>
    private static bool TrySetClipboard(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                // Copy: true — текст переживает выход приложения, как переживал
                // при pyperclip.
                Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (Exception e)
            {
                if (attempt == 4) Log.Warn("paste", $"буфер не отдаётся: {e.Message}");
                Thread.Sleep(50);
            }
        }
        return false;
    }

    private static void SendPaste()
    {
        // Чужие модификаторы отпускаем: если человек всё ещё держит Alt от
        // сочетания, которым остановил запись, Ctrl+V превратился бы в
        // Ctrl+Alt+V — а это в половине программ совсем другая команда.
        var release = new List<Win32.INPUT>();
        foreach (var vk in new ushort[] { Win32.VK_MENU, Win32.VK_SHIFT, Win32.VK_LWIN, Win32.VK_RWIN })
        {
            if ((Win32.GetAsyncKeyState(vk) & 0x8000) != 0) release.Add(Win32.KeyInput(vk, up: true));
        }
        if (release.Count > 0) Win32.Send([.. release]);

        Win32.Send(
            Win32.KeyInput(Win32.VK_CONTROL, up: false),
            Win32.KeyInput(0x56, up: false),   // V
            Win32.KeyInput(0x56, up: true),
            Win32.KeyInput(Win32.VK_CONTROL, up: true));
    }

    /// <summary>
    /// Посимвольный набор. Идёт как Unicode, минуя раскладку: иначе русский
    /// текст в английской раскладке превратился бы в набор согласных.
    /// </summary>
    private static void TypeText(string text)
    {
        foreach (var character in text)
        {
            if (character == '\n')
            {
                Win32.Send(Win32.KeyInput(0x0D, up: false), Win32.KeyInput(0x0D, up: true));
                continue;
            }
            if (character == '\r') continue;

            Win32.Send(Win32.CharInput(character, up: false), Win32.CharInput(character, up: true));
            Thread.Sleep(5);
        }
    }
}
