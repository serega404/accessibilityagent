namespace AccessibilityAgent.Checks;

/// <summary>
/// Вспомогательные методы для формирования словаря метаданных из пар ключ‑значение с фильтрацией пустых данных.
/// </summary>
internal static class MetadataBuilder
{
    /// <summary>
    /// Создаёт словарь из переданных пар, игнорируя записи с пустым ключом или значением.
    /// </summary>
    /// <param name="entries">Пары ключ‑значение. Значение может быть <c>null</c> и будет проигнорировано.</param>
    /// <returns>
    /// Словарь с непустыми значениями или <c>null</c>, если после фильтрации не осталось записей.
    /// </returns>
    public static Dictionary<string, string>? FromPairs(params (string Key, string? Value)[] entries)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in entries)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            dictionary[key] = value!;
        }

        return dictionary.Count == 0 ? null : dictionary;
    }
}
