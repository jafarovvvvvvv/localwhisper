using LocalWhisper.Interop;

namespace LocalWhisper.Tests;

/// <summary>
/// Разбор сочетаний клавиш.
///
/// Проверяется тестами, а не руками, ровно по одной причине: сочетание, которое
/// не разобралось, не сообщает о себе ничем. Клавиша просто не работает, и
/// человек ищет причину в микрофоне, в модели, в чём угодно, кроме одной буквы
/// в файле настроек.
/// </summary>
public class HotkeyTests
{
    private const uint Ctrl = HotkeyCombination.MOD_CONTROL;
    private const uint Alt = HotkeyCombination.MOD_ALT;
    private const uint Shift = HotkeyCombination.MOD_SHIFT;
    private const uint Win = HotkeyCombination.MOD_WIN;

    [Fact]
    public void Parses_the_default_dictation_combination()
    {
        Assert.True(HotkeyCombination.TryParse("ctrl+alt+space", out var hotkey));
        Assert.Equal(Ctrl | Alt, hotkey.Modifiers);
        Assert.Equal(0x20u, hotkey.Key);
    }

    [Fact]
    public void Parses_the_mode_cycler()
    {
        Assert.True(HotkeyCombination.TryParse("ctrl+shift+m", out var hotkey));
        Assert.Equal(Ctrl | Shift, hotkey.Modifiers);
        Assert.Equal((uint)'M', hotkey.Key);
    }

    [Theory]
    [InlineData("control+space", HotkeyCombination.MOD_CONTROL, 0x20u)]
    [InlineData("win+`", HotkeyCombination.MOD_WIN, 0xC0u)]
    [InlineData("alt+f4", HotkeyCombination.MOD_ALT, 0x73u)]
    [InlineData("ctrl+f12", HotkeyCombination.MOD_CONTROL, 0x7Bu)]
    [InlineData("shift+enter", HotkeyCombination.MOD_SHIFT, 0x0Du)]
    [InlineData("ctrl+escape", HotkeyCombination.MOD_CONTROL, 0x1Bu)]
    [InlineData("ctrl+9", HotkeyCombination.MOD_CONTROL, (uint)'9')]
    public void Parses_the_records_people_actually_write(string text, uint modifiers, uint key)
    {
        Assert.True(HotkeyCombination.TryParse(text, out var hotkey));
        Assert.Equal(modifiers, hotkey.Modifiers);
        Assert.Equal(key, hotkey.Key);
    }

    [Fact]
    public void Is_forgiving_about_spaces_and_capitals()
    {
        Assert.True(HotkeyCombination.TryParse("  Ctrl + Alt +  Space ", out var hotkey));
        Assert.Equal(Ctrl | Alt, hotkey.Modifiers);
        Assert.Equal(0x20u, hotkey.Key);
    }

    [Fact]
    public void Accepts_every_spelling_of_the_windows_key()
    {
        foreach (var spelling in new[] { "win+k", "windows+k", "cmd+k", "super+k", "meta+k" })
        {
            Assert.True(HotkeyCombination.TryParse(spelling, out var hotkey), spelling);
            Assert.Equal(Win, hotkey.Modifiers);
        }
    }

    [Fact]
    public void A_bare_key_without_modifiers_is_still_a_hotkey()
    {
        // RegisterHotKey такое принимает, и питоновская версия принимала тоже.
        Assert.True(HotkeyCombination.TryParse("f9", out var hotkey));
        Assert.Equal(0u, hotkey.Modifiers);
        Assert.Equal(0x78u, hotkey.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("ctrl")]
    [InlineData("ctrl+alt")]
    [InlineData("ctrl+unknown")]
    [InlineData("+++")]
    public void Refuses_what_it_cannot_turn_into_a_key(string? text)
    {
        Assert.False(HotkeyCombination.TryParse(text, out var hotkey));
        Assert.False(hotkey.IsValid);
    }

    [Fact]
    public void Shows_the_combination_as_separate_keycaps()
    {
        Assert.Equal(["Ctrl", "Alt", "Space"], HotkeyCombination.Parts("ctrl+alt+space"));
        Assert.Equal(["Ctrl", "Shift", "M"], HotkeyCombination.Parts("ctrl+shift+m"));
        Assert.Empty(HotkeyCombination.Parts(""));
    }

    [Fact]
    public void Shows_an_unknown_key_rather_than_swallowing_it()
    {
        // Показать «Ctrl + Щщ» честнее, чем промолчать: так видно, что именно
        // записано в настройках.
        Assert.Equal(["Ctrl", "Щщ"], HotkeyCombination.Parts("ctrl+щщ"));
    }
}
