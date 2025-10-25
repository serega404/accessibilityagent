using AccessibilityAgent.Checks;

namespace AccessibilityAgent.Agent;

/// <summary>
/// Представляет результат выполнения задания агентом.
/// Содержит информацию об идентификаторе задания, статусе успеха, результатах проверок и ошибках.
/// </summary>
internal sealed class AgentJobExecutionResult
{
    /// <summary>
    /// Идентификатор задания.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Флаг, указывающий на успешное выполнение задания.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Список результатов проверок, выполненных агентом.
    /// </summary>
    public List<CheckResult> Checks { get; init; } = new();

    /// <summary>
    /// Сообщение об ошибке, если выполнение завершилось неудачно.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Детализированная информация об ошибке.
    /// </summary>
    public string? ErrorDetails { get; init; }
}
