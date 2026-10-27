using System.Text.Json;

namespace LocalWhisper.Services;

/// <summary>
/// Одна строка, сказанная воркером.
///
/// Разбор вынесен из <see cref="WorkerClient"/> отдельно и нарочно: это
/// единственное место, где приложение зависит от чужого формата, и проверяться
/// оно должно тестами, а не запуском микрофона. Всё, чего в строке не было,
/// оказывается пустым — воркер имеет право не присылать лишнего.
/// </summary>
public sealed record WorkerMessage(
    string Type,
    string State = "",
    string Message = "",
    string Raw = "",
    string Clean = "",
    double Duration = 0,
    string Device = "",
    string ComputeType = "",
    IReadOnlyList<string>? Items = null)
{
    public IReadOnlyList<string> Names => Items ?? [];

    /// <summary>
    /// Разбирает строку. <c>null</c> означает «это не наш протокол» — например
    /// отладочный <c>print</c>, попавший в тот же поток. Это нормальный случай,
    /// а не ошибка: ронять движок из-за лишней строки было бы дорого.
    /// </summary>
    public static WorkerMessage? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        try
        {
            var root = JsonDocument.Parse(line).RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Text(root, "type") is not { Length: > 0 } type) return null;

            return new WorkerMessage(
                type,
                Text(root, "state"),
                Text(root, "message"),
                Text(root, "raw"),
                Text(root, "clean"),
                Number(root, "duration"),
                Text(root, "device"),
                Text(root, "compute_type"),
                Strings(root, "items"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static double Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : 0;

    private static IReadOnlyList<string> Strings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return [];
        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToList();
    }
}
