using System.Numerics;

namespace Kinovea.PortableAnalysis;

/// <summary>Portable drawing serialization payload; platform clipboard integration remains an adapter.</summary>
public sealed record DrawingClipboardPayload
{
    public string SerializedDrawing { get; }
    public Vector2 Position { get; }
    public string Name { get; }

    public DrawingClipboardPayload(string serializedDrawing, Vector2 position, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serializedDrawing);
        ArgumentNullException.ThrowIfNull(name);
        SerializedDrawing = serializedDrawing;
        Position = position;
        Name = name;
    }
}
