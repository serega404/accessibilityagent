using System.Text.Json.Serialization;

namespace AccessibilityAgent.Agent;

[JsonSerializable(typeof(AgentCredentials))]
internal partial class AgentJsonContext : JsonSerializerContext
{
}
