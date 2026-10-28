using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Kinovea.PortableDesktop.Presentation;

/// <summary>A portable, lossless-in-content view of a legacy Kinovea Video Analysis XML document.</summary>
/// <remarks>
/// The document preserves sections this application does not understand. It does not execute the legacy
/// MetadataSerializer's timestamp remapping, coordinate scaling, XSL conversion, or drawing initialization.
/// </remarks>
public sealed class PortableKvaDocument
{
    public const string RootName = "KinoveaVideoAnalysis";
    private readonly XDocument document;

    private PortableKvaDocument(XDocument document, string sourcePath)
    {
        this.document = document;
        SourcePath = sourcePath;
        FormatVersion = Required("FormatVersion").Value.Trim();
        ValidateVersion(FormatVersion);
    }

    public string SourcePath { get; }
    public string FormatVersion { get; }
    public string? OriginalFilename => ElementValue("OriginalFilename");
    public string? FullPath => ElementValue("FullPath");
    public long? ByteLength => ParseLong(ElementValue("ByteLength"));
    public (int Width, int Height)? ImageSize => ParseSize(ElementValue("ImageSize"));
    public long? FirstTimestamp => ParseLong(ElementValue("FirstTimeStamp"));
    public long? SelectionStart => ParseLong(ElementValue("SelectionStart"));
    public long? SelectionEnd => ParseLong(ElementValue("SelectionEnd"));
    public long? TimeOrigin => ParseLong(ElementValue("TimeOrigin"));
    public double? AverageTimestampsPerFrame => ParseDouble(ElementValue("AverageTimeStampsPerFrame"));

    /// <summary>Named top-level sections and element counts, including sections not interpreted by this port.</summary>
    public IReadOnlyDictionary<string, int> SectionCounts => document.Root!.Elements()
        .GroupBy(e => e.Name.LocalName, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
    public int KeyframeCount => document.Root!.Elements().Where(e => e.Name.LocalName == "Keyframes").SelectMany(e => e.Elements()).Count(e => e.Name.LocalName == "Keyframe");
    public int TrackCount => document.Root!.Elements().Where(e => e.Name.LocalName == "Tracks").SelectMany(e => e.Elements()).Count();
    public int ChronoCount => document.Root!.Elements().Where(e => e.Name.LocalName == "Chronos").SelectMany(e => e.Elements()).Count();
    public int RecognizedDrawingCount => document.Root!.Descendants().Count(e => DrawingElementNames.Contains(e.Name.LocalName));
    public bool HasCalibration => document.Root!.Elements().Any(e => e.Name.LocalName is "Calibration" or "CalibrationHelp");

    private static readonly HashSet<string> DrawingElementNames = new(StringComparer.Ordinal)
    {
        "Angle", "Arrow", "ArrowCurve", "ArrowPolyline", "Circle", "Clock", "Counter", "CrossMark", "Curve",
        "DistortionGrid", "Grid", "Label", "Line", "Pencil", "Plane", "Polyline", "Rectangle", "SVG",
        "Spotlight", "TestGrid", "Text", "TimePolyline", "TimeSegment", "Chrono", "ChronoMulti", "Track"
    };

    public static PortableKvaDocument Load(string path, long maximumCharacters = 64L * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        string fullPath = Path.GetFullPath(path);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = false,
            IgnoreComments = false,
            MaxCharactersInDocument = maximumCharacters,
            MaxCharactersFromEntities = 0,
            CloseInput = true
        };
        using XmlReader reader = XmlReader.Create(fullPath, settings);
        XDocument parsed = XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        if (parsed.Root?.Name.LocalName != RootName)
            throw new InvalidDataException($"Expected XML root '{RootName}'.");
        if (parsed.Root.Elements().Count(e => e.Name.LocalName == "FormatVersion") != 1)
            throw new InvalidDataException("KVA must contain exactly one FormatVersion element.");
        return new PortableKvaDocument(parsed, fullPath);
    }

    /// <summary>Writes XML content without removing unknown Kinovea sections; formatting may be normalized.</summary>
    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("KVA output must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);
        string temp = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await document.SaveAsync(output, SaveOptions.None, cancellationToken).ConfigureAwait(false);
            File.Move(temp, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>Returns the original metadata XML for structural comparison and explicit section-level adapters.</summary>
    public XDocument ToXDocument() => new(document);

    /// <summary>Creates a document with its media reference updated while retaining every other XML node.</summary>
    public PortableKvaDocument RelinkMedia(string mediaPath, long mediaLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        if (mediaLength < 0) throw new ArgumentOutOfRangeException(nameof(mediaLength));
        XDocument relinked = new(document);
        XElement root = relinked.Root!;
        SetElement(root, "FullPath", Path.GetFullPath(mediaPath));
        SetElement(root, "OriginalFilename", Path.GetFileNameWithoutExtension(mediaPath));
        SetElement(root, "ByteLength", mediaLength.ToString(CultureInfo.InvariantCulture));
        return new PortableKvaDocument(relinked, SourcePath);
    }

    /// <summary>Resolves FullPath or a relative media reference without guessing when no path was stored.</summary>
    public string? ResolveMediaPath(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath)) return Path.GetFullPath(overridePath);
        string? stored = FullPath;
        if (string.IsNullOrWhiteSpace(stored)) return null;
        if (Path.IsPathRooted(stored)) return Path.GetFullPath(stored);
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath)!, stored));
    }

    private XElement Required(string name) => document.Root!.Elements().Single(e => e.Name.LocalName == name);
    private string? ElementValue(string name) => document.Root!.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
    private static void SetElement(XElement root, string name, string value)
    {
        XElement? existing = root.Elements().FirstOrDefault(e => e.Name.LocalName == name);
        if (existing is null) root.Add(new XElement(name, value));
        else existing.Value = value;
    }

    private static void ValidateVersion(string version)
    {
        if (!double.TryParse(version, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value) ||
            !double.IsFinite(value) || value < 1.3 || value > 2.1)
            throw new InvalidDataException($"Unsupported Kinovea KVA format version '{version}'. Supported inventory range is 1.3 through 2.1.");
    }

    private static long? ParseLong(string? value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : null;
    private static double? ParseDouble(string? value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && double.IsFinite(parsed) ? parsed : null;

    private static (int Width, int Height)? ParseSize(string? value)
    {
        if (value is null) return null;
        string[] parts = value.Split(';');
        return parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) && width > 0 &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height) && height > 0
            ? (width, height) : null;
    }
}
