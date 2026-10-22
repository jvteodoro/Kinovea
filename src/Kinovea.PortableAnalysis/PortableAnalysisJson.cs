using System.Text.Json;

namespace Kinovea.PortableAnalysis;

/// <summary>JSON settings for contracts that contain System.Numerics vector fields.</summary>
public static class PortableAnalysisJson
{
    public static JsonSerializerOptions Options { get; } = new() { IncludeFields = true };
}
