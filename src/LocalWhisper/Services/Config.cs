using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LocalWhisper.Services;

/// <summary>Один режим диктовки: своя клавиша, свой язык, своя чистка текста.</summary>
public sealed class Mode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Hotkey { get; set; } = "";
    public string Language { get; set; } = "auto";
    public bool SuperMode { get; set; }
    public string SystemPrompt { get; set; } = "";

    public Mode Clone() => new()
    {
        Id = Id,
        Name = Name,
        Hotkey = Hotkey,
        Language = Language,
        SuperMode = SuperMode,
        SystemPrompt = SystemPrompt,
    };
}

/// <summary>
/// Настройки в <c>~/.localwhisper/config.json</c> — тот же файл, что писала
/// питоновская версия, и с теми же ключами.
///
/// Ключи здесь в <c>snake_case</c>, а не в camelCase, как у Popup. Это не
/// небрежность: файл уже лежит на диске у живых пользователей, и camelCase
/// молча перестал бы видеть их настройки — приложение запустилось бы «как в
/// первый раз», с чужой моделью и без режимов. Поэтому политика именования
/// своя, а всё остальное — послабления при чтении — как у Popup: файл правят
/// руками, и лишняя запятая не должна стоить всех настроек.
/// </summary>
public sealed class Config
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Пустые необязательные поля в файл не едут. Иначе config.json
        // обрастает строчками вида "pill_x": null, которые человеку, открывшему
        // файл, приходится сначала мысленно отбросить.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        // Иначе плюс в «ctrl+alt+space» уезжает в +, а кириллица — в
        // АБ. Формально верно, читать невозможно.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // MARK: - Распознавание

    public string WhisperProvider { get; set; } = "local";
    public string WhisperModel { get; set; } = "large-v3-turbo";
    public string WhisperDevice { get; set; } = "cuda";
    public string WhisperComputeType { get; set; } = "float16";

    // MARK: - Чистка текста

    public string LlmProvider { get; set; } = "ollama";
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "llama3.2";

    /// <summary>
    /// Ключ OpenAI. На диске это поле всегда пустое: в файл уезжает
    /// <see cref="OpenaiApiKeyEnc"/> под DPAPI. В памяти здесь лежит настоящий
    /// ключ — тот, что нашёлся в переменной окружения, в шифрованном блобе или
    /// (у старого файла) прямо здесь открытым текстом.
    /// </summary>
    public string OpenaiApiKey { get; set; } = "";

    public string? OpenaiApiKeyEnc { get; set; }

    public string OpenaiUrl { get; set; } = "https://api.openai.com/v1";
    public string OpenaiModel { get; set; } = "gpt-5.4-mini";

    // MARK: - Звук и запись

    public int SampleRate { get; set; } = 16000;
    public double SilenceThreshold { get; set; } = 0.003;
    public double SilenceDuration { get; set; } = 2.0;
    public string PasteMethod { get; set; } = "clipboard";
    public string ActiveModeId { get; set; } = "default";
    public string ActiveMicName { get; set; } = "";
    public List<string> Vocabulary { get; set; } = [];
    public bool SoundEnabled { get; set; } = true;

    public List<Mode> Modes { get; set; } = DefaultModes();

    // MARK: - Своё, чего не было в питоновской версии

    /// <summary>Где человек оставил пилюлю. Пусто — правый нижний угол.</summary>
    public double? PillX { get; set; }
    public double? PillY { get; set; }

    /// <summary><c>ru</c>, <c>en</c> или пусто — как в системе.</summary>
    public string? UiLanguage { get; set; }

    /// <summary>Чем запускать воркер. Пусто — <c>python</c> из PATH.</summary>
    public string? PythonExe { get; set; }

    /// <summary>
    /// Всё, чего мы не знаем. Файл правят руками и правят его же старые версии
    /// приложения — незнакомый ключ должен пережить сохранение, а не исчезнуть.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = [];

    public static List<Mode> DefaultModes() =>
    [
        new()
        {
            Id = "default",
            Name = "Voice to Text",
            Hotkey = "ctrl+alt+space",
            SuperMode = false,
            Language = "auto",
            SystemPrompt = "",
        },
        new()
        {
            Id = "super_mode",
            Name = "Super Mode",
            Hotkey = "ctrl+shift+space",
            SuperMode = true,
            Language = "auto",
            SystemPrompt = "You are a text cleanup assistant. Fix punctuation, capitalization, " +
                           "grammar. Remove filler words. Keep original meaning and language. " +
                           "Return ONLY cleaned text.",
        },
    ];

    // MARK: - Чтение

    public static Config Load()
    {
        AppPaths.EnsureCreated();
        var config = FromJson(File.Exists(AppPaths.Config) ? ReadFile() : "{}");
        config.ResolveSecrets();
        return config;
    }

    private static string ReadFile()
    {
        try
        {
            return File.ReadAllText(AppPaths.Config);
        }
        catch (Exception e)
        {
            // Своё приложение, вернувшееся к настройкам по умолчанию, полезнее
            // диалога с ошибкой при старте — но сказать об этом в журнал надо,
            // иначе сломанная запятая выглядит как «настройки не применяются».
            Log.Error("config", "config.json не читается", e);
            return "{}";
        }
    }

    /// <summary>
    /// Разобрать настройки из строки. Открыто наружу ради тестов: формат файла
    /// — то, что человек правит руками, и его послабления должны проверяться
    /// сами, а не выясняться потерянной правкой.
    /// </summary>
    public static Config FromJson(string json)
    {
        Config config;
        try
        {
            config = JsonSerializer.Deserialize<Config>(json, JsonOptions) ?? new Config();
        }
        catch (JsonException e)
        {
            Log.Warn("config", $"config.json не разобрать, беру значения по умолчанию: {e.Message}");
            return new Config();
        }

        // Списка режимов нет вовсе — это первый запуск, а не решение жить без
        // режимов: свойство остаётся со значением по умолчанию. Явный пустой
        // список — уже решение, и его мы уважаем, как уважала питоновская
        // версия.
        config.Modes ??= DefaultModes();
        config.Vocabulary ??= [];
        foreach (var mode in config.Modes)
        {
            mode.Id ??= "";
            mode.Name ??= "";
            mode.Hotkey ??= "";
            mode.Language ??= "auto";
            mode.SystemPrompt ??= "";
        }

        return config;
    }

    /// <summary>
    /// Достаёт настоящий ключ OpenAI: переменная окружения важнее файла, а
    /// шифрованный блоб — открытого текста, оставшегося от старых версий.
    /// Открытый текст при первом же сохранении переедет под DPAPI.
    /// </summary>
    public void ResolveSecrets()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            OpenaiApiKey = fromEnvironment;
            return;
        }

        if (!string.IsNullOrEmpty(OpenaiApiKeyEnc))
        {
            OpenaiApiKey = Secrets.Unprotect(OpenaiApiKeyEnc) ?? "";
        }
        // Иначе остаётся то, что лежало в файле открытым текстом.
    }

    /// <summary>Задан ли ключ снаружи — тогда поле в окне только показывает это.</summary>
    public static bool KeyComesFromEnvironment =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

    // MARK: - Запись

    /// <summary>Как настройки лягут в файл: ключ зашифрован, открытого текста нет.</summary>
    public string ToJson()
    {
        var node = JsonSerializer.SerializeToNode(this, JsonOptions)!.AsObject();

        if (KeyComesFromEnvironment)
        {
            // Ключ пришёл снаружи — он не наш, чтобы его хранить.
        }
        else if (!string.IsNullOrEmpty(OpenaiApiKey))
        {
            if (Secrets.Protect(OpenaiApiKey) is { } encrypted)
                node["openai_api_key_enc"] = encrypted;
        }
        else
        {
            // Ключ стёрли в окне — стираем и блоб. Иначе «удалить ключ» было бы
            // невозможно: приложение молча поднимало бы старый при следующем
            // запуске.
            node.Remove("openai_api_key_enc");
        }

        node["openai_api_key"] = "";
        return node.ToJsonString(JsonOptions);
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureCreated();
            AppPaths.WriteAtomic(AppPaths.Config, ToJson());
            Log.Info("config", "настройки сохранены");
        }
        catch (Exception e)
        {
            Log.Error("config", "не сохранить настройки", e);
        }
    }

    /// <summary>
    /// То, что уезжает воркеру: настоящий ключ вместо шифрованного блоба и
    /// ничего лишнего. Едет по stdin собственного дочернего процесса — тем же
    /// путём, каким ключ и раньше попадал в питоновскую память.
    /// </summary>
    public string ToWorkerJson()
    {
        var node = JsonSerializer.SerializeToNode(this, JsonOptions)!.AsObject();
        node.Remove("openai_api_key_enc");
        node["openai_api_key"] = OpenaiApiKey;
        return node.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// Активный режим — тот, что запускает кнопка на пилюле.
    ///
    /// В файл не пишется: это не настройка, а взгляд на неё. Без этой пометки
    /// System.Text.Json послушно сохранял бы целую копию режима под ключом
    /// active_mode, и файл начинал бы противоречить сам себе.
    /// </summary>
    [JsonIgnore]
    public Mode? ActiveMode =>
        Modes.FirstOrDefault(m => m.Id == ActiveModeId) ?? Modes.FirstOrDefault();
}
