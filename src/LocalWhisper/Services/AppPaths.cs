namespace LocalWhisper.Services;

/// <summary>
/// Единственное место, знающее, где что лежит.
///
/// Папка та же, что была у питоновской версии — <c>~/.localwhisper</c>, — и это
/// не мелочь: в ней лежат три гигабайта моделей, история и настройки, которые
/// должны пережить переезд приложения на другой язык без единой правки руками.
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".localwhisper");

    public static string Config => Path.Combine(Root, "config.json");
    public static string History => Path.Combine(Root, "history.json");
    public static string Models => Path.Combine(Root, "models");
    public static string LogFile => Path.Combine(Root, "app.log");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Models);
    }

    /// <summary>
    /// Запись через временный файл: обрыв питания посреди сохранения не должен
    /// оставлять от истории половину JSON-а, которую потом не прочитать.
    /// </summary>
    public static void WriteAtomic(string path, string contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Где лежит питоновский движок.
    ///
    /// Рядом с exe он оказывается при сборке, но при запуске из
    /// <c>bin/Debug/…</c> исходник живёт на четыре папки выше — поэтому вверх
    /// от исполняемого файла тоже смотрим. Так <c>dotnet run</c> работает без
    /// предварительной раскладки файлов.
    /// </summary>
    public static string? FindWorker()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && directory is not null; i++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "engine", "worker.py");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
