namespace AccessibilityAgent.Cli;

/// <summary>
/// Разбирает аргументы командной строки на опции и позиционные параметры.
/// Поддерживает длинные формы (--key, --key=value) и короткие формы (-k) с несколькими значениями.
/// </summary>
/// <remarks>
/// Имена опций сопоставляются без учета регистра. Флаги без значения трактуются как установленное значение "true".
/// </remarks>
internal sealed class ParsedArguments
{
    private ParsedArguments(Dictionary<string, List<string>> options, List<string> positionals)
    {
        Options = options;
        Positionals = positionals;
    }

    public bool IsHelpRequested => HasFlag(["help", "h"]);

    /// <summary>
    /// Словарь опций: имя опции → список значений в порядке поступления.
    /// </summary>
    private Dictionary<string, List<string>> Options { get; }

    /// <summary>
    /// Позиционные аргументы — это параметры командной строки, которые определяются порядком следования, а не именем ключа. В отличие от опций/флагов (-k, --key, --key=value), позиционные не начинаются с дефиса и интерпретируются по месту.
    /// </summary>
    public List<string> Positionals { get; }

    /// <summary>
    /// Разбирает массив аргументов командной строки.
    /// </summary>
    /// <param name="args">Аргументы, переданные в метод Main.</param>
    /// <returns>Экземпляр <see cref="ParsedArguments"/> с разобранными опциями и позиционными аргументами.</returns>
    public static ParsedArguments Parse(string[] args)
    {
        var options = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var positionals = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];

            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var remainder = token[2..];
                var separatorIndex = remainder.IndexOf('=');
                string key;
                string value;

                if (separatorIndex >= 0)
                {
                    key = remainder[..separatorIndex];
                    value = remainder[(separatorIndex + 1)..];
                }
                else if (index + 1 < args.Length && !IsOptionToken(args[index + 1]))
                {
                    key = remainder;
                    value = args[++index];
                }
                else
                {
                    key = remainder;
                    value = "true";
                }

                AddOption(options, key, value);
            }
            else if (token.Length > 1 && token[0] == '-')
            {
                var key = token[1..];
                string value;

                if (index + 1 < args.Length && !IsOptionToken(args[index + 1]))
                {
                    value = args[++index];
                }
                else
                {
                    value = "true";
                }

                AddOption(options, key, value);
            }
            else
            {
                positionals.Add(token);
            }
        }

        return new ParsedArguments(options, positionals);
    }

    /// <summary>
    /// Возвращает последнее значение для первой найденной опции из перечисленных имен.
    /// </summary>
    /// <param name="names">Набор алиасов опции (например, new[] { "output", "o" }).</param>
    /// <returns>Строковое значение или null, если опция не указана.</returns>
    public string? GetOption(string[] names)
    {
        foreach (var name in names)
        {
            if (Options.TryGetValue(name, out var values) && values.Count > 0)
            {
                return values[^1];
            }
        }

        return null;
    }

    /// <summary>
    /// Возвращает все значения для указанных имен опций в порядке их появления.
    /// </summary>
    /// <param name="names">Набор алиасов интересующих опций.</param>
    /// <returns>Последовательность значений; может быть пустой.</returns>
    public IReadOnlyList<string> GetOptionValues(string[] names)
    {
        var collected = new List<string>();

        foreach (var name in names)
        {
            if (Options.TryGetValue(name, out var values))
            {
                collected.AddRange(values);
            }
        }

        return collected;
    }

    /// <summary>
    /// Извлекает и удаляет первый позиционный аргумент.
    /// </summary>
    /// <returns>Значение аргумента или null, если позиционных аргументов нет.</returns>
    public string? ConsumePositional()
    {
        if (Positionals.Count == 0)
        {
            return null;
        }

        var value = Positionals[0];
        Positionals.RemoveAt(0);
        return value;
    }

    public bool HasFlag(string[] names)
    {
        foreach (var name in names)
        {
            if (Options.TryGetValue(name, out var values))
            {
                if (values.Count == 0)
                {
                    return true;
                }

                if (values.Any(v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Пытается получить целочисленное значение опции.
    /// </summary>
    /// <param name="names">Набор алиасов опции.</param>
    /// <param name="minValue">Минимально допустимое значение (включительно), если задано.</param>
    /// <param name="maxValue">Максимально допустимое значение (включительно), если задано.</param>
    /// <returns>Целое число или null, если опция не указана.</returns>
    /// <exception cref="ArgumentException">
    /// Будет выброшено, если значение не является целым числом или выходит за пределы [minValue; maxValue].
    /// </exception>
    public int? TryGetIntOption(string[] names, int? minValue = null, int? maxValue = null)
    {
        var raw = GetOption(names);

        if (raw == null)
        {
            return null;
        }

        if (!int.TryParse(raw, out var value))
        {
            throw new ArgumentException($"Invalid integer value '{raw}' for option '--{names[0]}'.");
        }

        if (minValue.HasValue && value < minValue.Value)
        {
            throw new ArgumentException($"Value '{value}' for option '--{names[0]}' must be >= {minValue.Value}.");
        }

        if (maxValue.HasValue && value > maxValue.Value)
        {
            throw new ArgumentException($"Value '{value}' for option '--{names[0]}' must be <= {maxValue.Value}.");
        }

        return value;
    }

    /// <summary>
    /// Возвращает требуемое целочисленное значение: сначала из опции, затем из первого позиционного аргумента.
    /// </summary>
    /// <param name="names">Набор алиасов опции.</param>
    /// <param name="minValue">Минимально допустимое значение (включительно), если задано.</param>
    /// <param name="maxValue">Максимально допустимое значение (включительно), если задано.</param>
    /// <returns>Целое число из опции или из первого позиционного аргумента.</returns>
    /// <exception cref="ArgumentException">
    /// Будет выброшено, если значение не является целым числом, нарушает границы или отсутствует как в опциях, так и среди позиционных.
    /// </exception>
    public int RequireIntOption(string[] names, int? minValue = null, int? maxValue = null)
    {
        var optionValue = TryGetIntOption(names, minValue, maxValue);
        if (optionValue.HasValue)
        {
            return optionValue.Value;
        }

        var positional = ConsumePositional();
        if (!string.IsNullOrWhiteSpace(positional))
        {
            if (!int.TryParse(positional, out var parsed))
            {
                throw new ArgumentException($"Invalid integer value '{positional}' for positional argument.");
            }

            if (minValue.HasValue && parsed < minValue.Value)
            {
                throw new ArgumentException($"Value '{parsed}' must be >= {minValue.Value}.");
            }

            if (maxValue.HasValue && parsed > maxValue.Value)
            {
                throw new ArgumentException($"Value '{parsed}' must be <= {maxValue.Value}.");
            }

            return parsed;
        }

        throw new ArgumentException($"Missing required option '--{names[0]}'.");
    }

    /// <summary>
    /// Добавляет значение к опции, создавая список при необходимости.
    /// </summary>
    /// <param name="options">Словарь опций.</param>
    /// <param name="key">Имя опции.</param>
    /// <param name="value">Значение опции.</param>
    private static void AddOption(Dictionary<string, List<string>> options, string key, string value)
    {
        if (!options.TryGetValue(key, out var values))
        {
            values = new List<string>();
            options[key] = values;
        }

        values.Add(value);
    }

    /// <summary>
    /// Определяет, является ли токен токеном опции (начинается с '-' или '--').
    /// </summary>
    /// <param name="token">Токен аргумента.</param>
    /// <returns>true, если токен является опцией; иначе false.</returns>
    private static bool IsOptionToken(string token)
    {
        return token.Length > 0 && token[0] == '-';
    }
}
