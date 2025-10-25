using System.Text.Json.Nodes;

namespace AccessibilityAgent.Agent;


/// <summary>
/// Представляет запрос на выполнение работы агентом.
/// Содержит идентификатор, тип задачи, полезную нагрузку и метаданные.
/// </summary>
internal sealed class AgentJobRequest
{
    /// <summary>
    /// Уникальный идентификатор запроса.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Тип задачи, которую должен выполнить агент.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Полезная нагрузка запроса, содержащая параметры задачи.
    /// Может быть <c>null</c>.
    /// </summary>
    public JsonNode? Payload { get; init; }

    /// <summary>
    /// Дополнительные метаданные запроса.
    /// Может быть <c>null</c>.
    /// </summary>
    public JsonNode? Metadata { get; init; }
}
