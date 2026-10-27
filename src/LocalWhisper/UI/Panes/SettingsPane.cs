using System.Globalization;
using System.Windows.Controls;
using LocalWhisper.Model;
using LocalWhisper.Services;

namespace LocalWhisper.UI.Panes;

/// <summary>
/// Настройки движка: чем распознавать, на чём, чем чистить и как вставлять.
///
/// Правки копятся в черновике и применяются кнопкой, а не на лету. Причина
/// простая: смена модели или устройства перезагружает движок на несколько
/// секунд, и делать это на каждое нажатие клавиши в поле — значит превратить
/// правку адреса сервера в десять перезагрузок подряд.
/// </summary>
public sealed class SettingsPane(AppController app, Action refresh) : Pane(app, refresh)
{
    private static readonly string[] Models =
        ["tiny", "base", "small", "medium", "large-v3", "large-v3-turbo", "distil-large-v3"];

    private static readonly string[] Devices = ["cuda", "cpu"];
    private static readonly string[] ComputeTypes = ["float16", "int8_float16", "int8", "float32"];

    /// <summary>Черновик: то, что уже набрано, но ещё не сохранено.</summary>
    private sealed class Draft
    {
        public string Provider = "local";
        public string Model = "large-v3-turbo";
        public string Device = "cuda";
        public string Compute = "float16";
        public string Paste = "clipboard";
        public string Llm = "ollama";
        public string OllamaUrl = "";
        public string OllamaModel = "";
        public string OpenaiKey = "";
        public string OpenaiUrl = "";
        public string OpenaiModel = "";
        public string Threshold = "";
        public string Duration = "";
    }

    private Draft? _draft;

    public override UIElement Build()
    {
        var draft = _draft ??= FromConfig();

        var stack = new StackPanel();
        stack.Children.Add(PaneKit.Title(Strings.L("Settings")));
        if (App.ModelStatus.Length > 0) stack.Children.Add(PaneKit.Subtitle(App.ModelStatus));

        stack.Children.Add(PaneKit.FormRow(Strings.L("Speech-to-text provider"),
            PaneKit.Combo(
                [("local", Strings.L("Local (faster-whisper)")), ("openai", Strings.L("OpenAI API (cloud)"))],
                draft.Provider,
                value => { draft.Provider = value; Refresh(); })));

        if (draft.Provider == "local")
        {
            stack.Children.Add(PaneKit.FormRow(Strings.L("Whisper model"),
                PaneKit.Combo(Models, draft.Model, value => draft.Model = value)));
            stack.Children.Add(PaneKit.FormRow(Strings.L("Device"),
                PaneKit.Combo(Devices, draft.Device, value => draft.Device = value)));
            stack.Children.Add(PaneKit.FormRow(Strings.L("Compute type"),
                PaneKit.Combo(ComputeTypes, draft.Compute, value => draft.Compute = value)));
        }

        stack.Children.Add(PaneKit.FormRow(Strings.L("Paste method"),
            PaneKit.Combo(
                [("clipboard", Strings.L("clipboard")), ("keyboard", Strings.L("keyboard"))],
                draft.Paste,
                value => draft.Paste = value)));

        stack.Children.Add(PaneKit.FormRow(Strings.L("LLM cleanup provider"),
            PaneKit.Combo(
                [("ollama", "Ollama"), ("openai", "OpenAI")],
                draft.Llm,
                value => { draft.Llm = value; Refresh(); })));

        if (draft.Llm == "ollama")
        {
            stack.Children.Add(PaneKit.FormRow(Strings.L("Ollama server URL"),
                TextRow(draft.OllamaUrl, value => draft.OllamaUrl = value, "http://localhost:11434")));
            stack.Children.Add(PaneKit.FormRow(Strings.L("Ollama model"),
                TextRow(draft.OllamaModel, value => draft.OllamaModel = value, "llama3.2")));
        }

        // Ключ нужен и облачному распознаванию, и облачной чистке — показываем
        // его, если хотя бы одно из двух выбрано.
        if (draft.Llm == "openai" || draft.Provider == "openai")
        {
            stack.Children.Add(PaneKit.FormRow(Strings.L("OpenAI API key"),
                BuildKeyField(draft),
                Config.KeyComesFromEnvironment
                    ? Strings.L("Taken from the OPENAI_API_KEY environment variable")
                    : Strings.L("The key is stored encrypted with your Windows account")));
            stack.Children.Add(PaneKit.FormRow(Strings.L("OpenAI base URL"),
                TextRow(draft.OpenaiUrl, value => draft.OpenaiUrl = value, "https://api.openai.com/v1")));
            stack.Children.Add(PaneKit.FormRow(Strings.L("OpenAI model"),
                TextRow(draft.OpenaiModel, value => draft.OpenaiModel = value, "gpt-5.4-mini")));
        }

        stack.Children.Add(PaneKit.FormRow(Strings.L("Silence threshold"),
            TextRow(draft.Threshold, value => draft.Threshold = value, "0.003")));
        stack.Children.Add(PaneKit.FormRow(Strings.L("Silence duration, seconds"),
            TextRow(draft.Duration, value => draft.Duration = value, "2.0")));

        var save = PaneKit.TextButton(Strings.L("Save settings"), Commit, "IconCheck");
        save.HorizontalAlignment = HorizontalAlignment.Left;
        save.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(save);

        return PaneKit.List(stack);
    }

    private Draft FromConfig()
    {
        var config = App.Config;
        return new Draft
        {
            Provider = config.WhisperProvider,
            Model = config.WhisperModel,
            Device = config.WhisperDevice,
            Compute = config.WhisperComputeType,
            Paste = config.PasteMethod,
            Llm = config.LlmProvider.ToLowerInvariant(),
            OllamaUrl = config.OllamaUrl,
            OllamaModel = config.OllamaModel,
            OpenaiKey = config.OpenaiApiKey,
            OpenaiUrl = config.OpenaiUrl,
            OpenaiModel = config.OpenaiModel,
            Threshold = config.SilenceThreshold.ToString(CultureInfo.InvariantCulture),
            Duration = config.SilenceDuration.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static UIElement TextRow(string value, Action<string> onChange, string placeholder)
    {
        var box = PaneKit.Field();
        box.Text = value;
        box.TextChanged += (_, _) => onChange(box.Text);
        return PaneKit.FieldBox(PaneKit.WithPlaceholder(box, placeholder));
    }

    /// <summary>
    /// Ключ под звёздочками, но с кнопкой «показать»: вводя длинную строку
    /// вслепую, ошибиться легче, чем заметить это потом по отказу сервера.
    /// </summary>
    private UIElement BuildKeyField(Draft draft)
    {
        if (Config.KeyComesFromEnvironment)
        {
            var readOnlyBox = PaneKit.Field();
            readOnlyBox.Text = Mask(App.Config.OpenaiApiKey);
            readOnlyBox.IsReadOnly = true;
            readOnlyBox.Foreground = PaneKit.Brush("TextTertiary");
            return PaneKit.FieldBox(readOnlyBox);
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = PaneKit.Field();
        box.Text = draft.OpenaiKey;
        box.TextChanged += (_, _) => draft.OpenaiKey = box.Text;
        PaneKit.ApplyCover(box, draft.OpenaiKey.Length > 0);

        var field = PaneKit.WithPlaceholder(box, "sk-…");
        Grid.SetColumn(field, 0);
        grid.Children.Add(field);

        var covered = draft.OpenaiKey.Length > 0;
        Button? reveal = null;
        reveal = PaneKit.IconButton("IconSearch", () =>
        {
            covered = !covered;
            PaneKit.ApplyCover(box, covered);
            if (reveal is not null) reveal.Opacity = covered ? 1 : 0.6;
        }, 22, Strings.L("Copy"));
        Grid.SetColumn(reveal, 1);
        grid.Children.Add(reveal);

        return PaneKit.FieldBox(grid);
    }

    private static string Mask(string key) =>
        key.Length <= 8 ? new string('•', key.Length) : $"{key[..4]}{new string('•', 8)}{key[^4..]}";

    private void Commit()
    {
        if (_draft is not { } draft) return;
        var config = App.Config;

        config.WhisperProvider = draft.Provider;
        config.WhisperModel = draft.Model;
        config.WhisperDevice = draft.Device;
        config.WhisperComputeType = draft.Compute;
        config.PasteMethod = draft.Paste;
        config.LlmProvider = draft.Llm;
        config.OllamaUrl = draft.OllamaUrl.Trim();
        config.OllamaModel = draft.OllamaModel.Trim();
        config.OpenaiUrl = draft.OpenaiUrl.Trim();
        config.OpenaiModel = draft.OpenaiModel.Trim();

        // Ключ из переменной окружения не наш, чтобы его переписывать.
        if (!Config.KeyComesFromEnvironment) config.OpenaiApiKey = draft.OpenaiKey.Trim();

        // Числа читаем терпимо: неразобранное просто остаётся прежним, а не
        // обнуляет порог тишины в ноль, после чего запись не кончается никогда.
        if (double.TryParse(draft.Threshold, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold)
            && threshold is > 0 and < 1)
        {
            config.SilenceThreshold = threshold;
        }
        if (double.TryParse(draft.Duration, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)
            && duration is >= 0.3 and <= 30)
        {
            config.SilenceDuration = duration;
        }

        _draft = null;
        App.SaveConfig();
        App.Notify(Strings.L("Settings saved"));
    }
}
