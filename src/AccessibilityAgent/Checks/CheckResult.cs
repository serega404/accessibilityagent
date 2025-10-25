namespace AccessibilityAgent.Checks;

/// <summary>
/// Результат выполнения одной проверки доступности.
/// </summary>
/// <remarks>
/// Содержит идентификатор проверки, статус, сообщение, опциональную длительность и дополнительные данные.
/// </remarks>
internal sealed class CheckResult
{
    /// <summary>
    /// Идентификатор или имя проверки.
    /// </summary>
    public required string Check { get; init; }

    /// <summary>
    /// Признак успешного прохождения проверки.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Человекочитаемое описание результата выполнения проверки.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Длительность выполнения проверки в миллисекундах.
    /// </summary>
    /// <remarks>
    /// Может быть null, если длительность не измерялась.
    /// </remarks>
    public double? DurationMs { get; init; }

    /// <summary>
    /// Дополнительные диагностические данные в формате ключ-значение.
    /// </summary>
    /// <remarks>
    /// Может быть null при отсутствии дополнительных данных.
    /// </remarks>
    public Dictionary<string, string>? Data { get; init; }
}
