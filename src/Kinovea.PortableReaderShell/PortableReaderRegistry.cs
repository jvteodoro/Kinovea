using Kinovea.PortableFrameGenerator;

namespace Kinovea.PortableReaderShell;

/// <summary>Creates a portable frame source for one or more file extensions.</summary>
/// <remarks>
/// Register factories before opening a reader shell. Each factory must return
/// a new source instance so a successful replacement can close its previous
/// owner without invalidating the newly opened source.
/// </remarks>
public sealed class PortableReaderRegistry
{
    private readonly Dictionary<string, Func<IPortableFrameSource>> factories = new(StringComparer.OrdinalIgnoreCase);

    public PortableReaderRegistry Register(string extension, Func<IPortableFrameSource> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        ArgumentNullException.ThrowIfNull(factory);

        string normalized = NormalizeExtension(extension);
        if (!factories.TryAdd(normalized, factory))
            throw new InvalidOperationException($"A portable reader is already registered for '{normalized}'.");

        return this;
    }

    public IPortableFrameSource CreateForPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string extension = NormalizeExtension(Path.GetExtension(path));
        if (!factories.TryGetValue(extension, out Func<IPortableFrameSource>? factory))
            throw new NotSupportedException($"No portable reader is registered for '{extension}'.");

        return factory() ?? throw new InvalidOperationException($"The portable reader factory for '{extension}' returned null.");
    }

    public static PortableReaderRegistry CreateDefault() => new PortableReaderRegistry()
        .Register(".bmp", static () => new PortableImageFrameGenerator())
        .Register(".png", static () => new PortableImageFrameGenerator())
        .Register(".jpg", static () => new PortableImageFrameGenerator())
        .Register(".jpeg", static () => new PortableImageFrameGenerator());

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            throw new NotSupportedException("A file extension is required to select a portable reader.");

        return extension[0] == '.' ? extension : "." + extension;
    }
}
