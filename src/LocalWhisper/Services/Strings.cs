using System.Globalization;

namespace LocalWhisper.Services;

/// <summary>
/// Строки интерфейса. Обычный словарь в коде, а не .resx: файл на две колонки
/// правится за секунду и виден целиком — ровно то, что нужно проекту, который
/// его владелец собирается дописывать сам.
///
/// Ключ — английский текст. Отсутствие перевода поэтому не ломает ничего:
/// показывается сам ключ, и он читаем.
///
/// До этого интерфейс LocalWhisper был смешанный — часть по-английски, часть
/// по-русски, и ни одну строку нельзя было поменять в одном месте. Теперь весь
/// видимый текст проходит здесь.
/// </summary>
public static class Strings
{
    private static readonly Dictionary<string, string> Ru = new()
    {
        // Разделы
        ["Home"] = "Главная",
        ["Modes"] = "Режимы",
        ["Vocabulary"] = "Словарь",
        ["Settings"] = "Настройки",
        ["Sound"] = "Звук",
        ["History"] = "История",

        // Боковая колонка
        ["100% local and free"] = "Полностью локально и бесплатно",
        ["No account, no cloud"] = "Без аккаунта и без облака",

        // Главная
        ["Average speed"] = "Средний темп",
        ["Words this week"] = "Слов за неделю",
        ["Apps used"] = "Приложений",
        ["Saved this week"] = "Сэкономлено",
        ["WPM"] = "сл/мин",
        ["h"] = "ч",
        ["Get started"] = "С чего начать",
        ["Start recording"] = "Начать запись",
        ["Turn your voice into text"] = "Превратить голос в текст",
        ["Customize your shortcuts"] = "Настроить сочетания",
        ["Every mode has its own hotkey"] = "У каждого режима своя клавиша",
        ["Create a mode"] = "Создать режим",
        ["Build the perfect mode"] = "Собрать режим под задачу",
        ["Add vocabulary"] = "Пополнить словарь",
        ["Teach it names and terms"] = "Научить именам и терминам",
        ["What's new"] = "Что нового",
        ["The engine moved to WPF"] = "Движок переехал на WPF",
        ["The interface, hotkeys, tray and settings are C# now; recognition stayed on faster-whisper in a headless Python worker. The models on disk are the same ones — nothing is downloaded again."] =
            "Интерфейс, горячие клавиши, трей и настройки теперь на C#; распознавание осталось на faster-whisper в безоконном питоновском воркере. Модели на диске те же — заново ничего не качается.",

        // Режимы
        ["New mode"] = "Новый режим",
        ["Set active"] = "Сделать активным",
        ["Edit"] = "Изменить",
        ["Delete"] = "Удалить",
        ["Change active mode"] = "Сменить активный режим",
        ["No modes yet"] = "Режимов пока нет",
        ["Create mode"] = "Создать режим",
        ["Edit mode"] = "Изменить режим",
        ["Mode name"] = "Название",
        ["Global hotkey"] = "Горячая клавиша",
        ["Speech language"] = "Язык речи",
        ["Clean the text up with an LLM"] = "Чистить текст языковой моделью",
        ["System prompt"] = "Системная подсказка",
        ["Instructions for the cleanup model…"] = "Указания модели, которая чистит текст…",
        ["Save"] = "Сохранить",
        ["Cancel"] = "Отмена",
        ["auto"] = "авто",

        // Словарь
        ["Add names, terms and abbreviations so the model hears them right"] =
            "Имена, термины и сокращения, чтобы модель слышала их правильно",
        ["Add word"] = "Добавить слово",
        ["New word"] = "Новое слово",
        ["Vocabulary is empty"] = "Словарь пуст",

        // Настройки
        ["Speech-to-text provider"] = "Распознавание речи",
        ["Local (faster-whisper)"] = "Локально (faster-whisper)",
        ["OpenAI API (cloud)"] = "OpenAI API (облако)",
        ["Whisper model"] = "Модель Whisper",
        ["Device"] = "Устройство",
        ["Compute type"] = "Тип вычислений",
        ["Paste method"] = "Способ вставки",
        ["clipboard"] = "через буфер",
        ["keyboard"] = "посимвольно",
        ["LLM cleanup provider"] = "Модель для чистки текста",
        ["Ollama server URL"] = "Адрес сервера Ollama",
        ["Ollama model"] = "Модель Ollama",
        ["OpenAI API key"] = "Ключ OpenAI",
        ["OpenAI base URL"] = "Адрес OpenAI",
        ["OpenAI model"] = "Модель OpenAI",
        ["Microphone"] = "Микрофон",
        ["Default input device"] = "Устройство по умолчанию",
        ["Silence threshold"] = "Порог тишины",
        ["Silence duration, seconds"] = "Длительность тишины, с",
        ["Save settings"] = "Сохранить настройки",
        ["Settings saved"] = "Настройки сохранены",
        ["The key is stored encrypted with your Windows account"] =
            "Ключ хранится зашифрованным ключом учётной записи Windows",
        ["Taken from the OPENAI_API_KEY environment variable"] =
            "Берётся из переменной окружения OPENAI_API_KEY",

        // Звук
        ["Short chimes when a dictation starts, ends and lands"] =
            "Короткие сигналы, когда диктовка началась, закончилась и легла в текст",
        ["Sound signals"] = "Звуковые сигналы",
        ["Recording started"] = "Начало записи",
        ["Recording stopped"] = "Конец записи",
        ["Text pasted"] = "Текст вставлен",
        ["Mode switched"] = "Смена режима",
        ["Play"] = "Проверить",

        // Кнопка в шапке панели Popup, когда пилюля отдала ей своё состояние.
        ["Stop"] = "Стоп",

        // История
        ["Nothing recorded yet"] = "Расшифровок пока нет",
        ["Nothing found"] = "Ничего не нашлось",
        ["Search"] = "Поиск",
        ["Copy"] = "Копировать",
        ["Copied"] = "Скопировано",
        ["Raw"] = "Как услышала модель",
        ["words"] = "сл.",

        // Трей и уведомления
        ["Settings and dashboard"] = "Настройки и статистика",
        ["Open data folder"] = "Открыть папку данных",
        ["Settings file"] = "Файл настроек",
        ["Quit"] = "Выход",
        ["Model is still loading, please wait"] = "Модель ещё грузится, подожди",
        ["Transcription failed"] = "Расшифровка не удалась",
        ["Active mode"] = "Активный режим",
        ["Fell back to CPU, it will be slower"] = "Откатился на процессор, будет медленнее",
        ["Could not register hotkeys"] = "Не занять сочетания клавиш",
        ["Pick a different combination in Modes"] = "Выбери другое сочетание в разделе «Режимы»",
        ["The engine is not running"] = "Движок не запущен",
        ["Python worker not found"] = "Питоновский воркер не найден",
        ["Loading the model"] = "Загружаю модель",
        ["Ready"] = "Готово",
    };

    private static Dictionary<string, string>? _active;

    /// <summary>
    /// Какой язык показывать. <c>null</c> в настройках — как в системе;
    /// всё, что не русский, попадает в английский, потому что ключи и есть
    /// английский текст.
    ///
    /// Смотрим на обе культуры, а не только на язык интерфейса: Windows часто
    /// стоит с английским интерфейсом при русском регионе, и приложение,
    /// которое в этом случае говорит по-английски, формально право и
    /// практически бесполезно.
    /// </summary>
    public static void Configure(string? language)
    {
        var russian = !string.IsNullOrWhiteSpace(language)
            ? language.Equals("ru", StringComparison.OrdinalIgnoreCase)
            : IsRussian(CultureInfo.CurrentUICulture) || IsRussian(CultureInfo.CurrentCulture);
        _active = russian ? Ru : null;
    }

    private static bool IsRussian(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Культура, по которой приложение пишет даты и числа. Не системная: язык
    /// интерфейса задаётся настройкой, и русское окно с английскими датами
    /// выглядит поломкой.
    /// </summary>
    public static CultureInfo Culture =>
        _active is not null ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.CurrentCulture;

    public static string L(string key) =>
        _active is not null && _active.TryGetValue(key, out var value) ? value : key;
}
