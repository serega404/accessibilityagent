using System.Collections.ObjectModel;

namespace AccessibilityAgent.Agent;


/// <summary>
/// Параметры конфигурации агента доступности.
/// </summary>
/// <remarks>
/// Этот класс инкапсулирует все обязательные и дополнительные параметры, необходимые для
/// работы <see cref="AgentRunner"/> и других компонентов агента: адрес сервера, токен
/// аутентификации, имя агента, параметры повторных подключений (экспоненциальная/линейная
/// стратегия может использовать минимальную/максимальную задержки), интервал отправки
/// heartbeat-сообщений и произвольные метаданные.
/// <para>
/// Значение <see cref="HeartbeatInterval"/> нормализуется: если передано отрицательное
/// время, то свойство будет равно <see cref="TimeSpan.Zero"/>.
/// </para>
/// <para>
/// Коллекция <see cref="Metadata"/> доступна только для чтения; ключи сравниваются с
/// учётом регистра (StringComparer.Ordinal).
/// </para>
/// </remarks>
internal sealed class AgentOptions
{
    public AgentOptions(
        Uri serverUri,
        string token,
        string agentName,
        TimeSpan reconnectDelay,
        TimeSpan reconnectDelayMax,
        int? maxReconnectAttempts,
        TimeSpan heartbeatInterval,
        IReadOnlyDictionary<string, string>? metadata,
        string? credentialFilePath = null,
        bool autoIssuePersonalToken = true)
    {
        if (serverUri is null)
        {
            throw new ArgumentNullException(nameof(serverUri));
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Token cannot be empty.", nameof(token));
        }

        if (string.IsNullOrWhiteSpace(agentName))
        {
            throw new ArgumentException("Agent name cannot be empty.", nameof(agentName));
        }
        if (reconnectDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(reconnectDelay), reconnectDelay, "Reconnect delay must be positive.");
        }

        if (reconnectDelayMax < reconnectDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(reconnectDelayMax), reconnectDelayMax, "Reconnect delay max must be >= reconnect delay.");
        }

        ServerUri = serverUri;
        Token = token;
        AgentName = agentName;
        ReconnectDelay = reconnectDelay;
        ReconnectDelayMax = reconnectDelayMax;
        MaxReconnectAttempts = maxReconnectAttempts;
        HeartbeatInterval = heartbeatInterval < TimeSpan.Zero ? TimeSpan.Zero : heartbeatInterval;
        Metadata = metadata is null || metadata.Count == 0
            ? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>())
            : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(metadata, StringComparer.Ordinal));

        CredentialFilePath = string.IsNullOrWhiteSpace(credentialFilePath)
            ? AgentCredentialStore.GetDefaultPath()
            : credentialFilePath!;
        AutoIssuePersonalToken = autoIssuePersonalToken;
    }

    /// <summary>
    /// URI сервера, с которым агент устанавливает соединение.
    /// </summary>
    public Uri ServerUri { get; }

    /// <summary>
    /// Токен аутентификации для доступа к серверу.
    /// </summary>
    public string Token { get; }

    /// <summary>
    /// Имя агента, используемое для идентификации на стороне сервера и в логах.
    /// </summary>
    public string AgentName { get; }

    /// <summary>
    /// Начальная задержка перед повторным подключением после обрыва связи.
    /// Значение должно быть положительным.
    /// </summary>
    public TimeSpan ReconnectDelay { get; }

    /// <summary>
    /// Максимальная задержка между попытками повторного подключения. Должна быть не меньше
    /// <see cref="ReconnectDelay"/>.
    /// </summary>
    public TimeSpan ReconnectDelayMax { get; }

    /// <summary>
    /// Максимальное количество попыток переподключения. <c>null</c> означает отсутствие ограничения.
    /// </summary>
    public int? MaxReconnectAttempts { get; }

    /// <summary>
    /// Интервал отправки heartbeat-сообщений. Если в конструктор передано отрицательное значение,
    /// свойство будет равно <see cref="TimeSpan.Zero"/>.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; }

    /// <summary>
    /// Дополнительные метаданные, связанные с агентом. Коллекция доступна только для чтения;
    /// ключи сравниваются с учётом регистра (Ordinal).
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Путь к файлу, где хранятся креды агента (персональный токен, метаданные).
    /// </summary>
    public string CredentialFilePath { get; }

    /// <summary>
    /// При первом подключении запросить у сервера персональный токен и сохранить его.
    /// </summary>
    public bool AutoIssuePersonalToken { get; }
}
