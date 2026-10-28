using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Kinovea.PortableDesktop.Presentation;

public readonly record struct PortableImagePoint(double X, double Y);

/// <summary>One calibrated image-space distance attached to a portable video workspace.</summary>
public sealed record PortableDistanceAnalysis(
    PortableImagePoint CalibrationStart,
    PortableImagePoint CalibrationEnd,
    double KnownDistance,
    string Unit,
    PortableImagePoint MeasureStart,
    PortableImagePoint MeasureEnd,
    double MeasuredDistance);

public static class PortableDistanceAnalysisFactory
{
    public static PortableDistanceAnalysis Create(PortableImagePoint calibrationStart, PortableImagePoint calibrationEnd, double knownDistance,
        string unit, PortableImagePoint measureStart, PortableImagePoint measureEnd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        if (!double.IsFinite(knownDistance) || knownDistance <= 0)
            throw new ArgumentOutOfRangeException(nameof(knownDistance), "Calibration distance must be finite and positive.");
        double calibrationPixels = PortableAnalysisWorkspaceStore.Distance(calibrationStart, calibrationEnd);
        if (calibrationPixels <= 0) throw new ArgumentException("Calibration endpoints must differ.");
        double value = PortableAnalysisWorkspaceStore.Distance(measureStart, measureEnd) * knownDistance / calibrationPixels;
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(measureEnd), "Measured distance is not finite.");
        return new PortableDistanceAnalysis(calibrationStart, calibrationEnd, knownDistance, unit.Trim(), measureStart,
            measureEnd, value);
    }
}

/// <summary>Versioned, portable sidecar for the first save/reopen analysis workflow.</summary>
public sealed record PortableAnalysisWorkspace(
    string FormatId,
    int SchemaVersion,
    string MediaReference,
    string MediaSha256,
    long MediaLength,
    int Width,
    int Height,
    long Timestamp,
    PortableDistanceAnalysis DistanceAnalysis);

/// <summary>Reads and writes the explicitly limited portable workspace v1 format.</summary>
public static class PortableAnalysisWorkspaceStore
{
    public const string CurrentFormatId = "kinovea.portable-analysis";
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async Task SaveAsync(string workspacePath, string mediaPath, int width, int height,
        long timestamp, PortableDistanceAnalysis analysis, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        ArgumentNullException.ThrowIfNull(analysis);
        ValidateDimensions(width, height);
        ValidateAnalysis(analysis, width, height);
        var media = new FileInfo(Path.GetFullPath(mediaPath));
        if (!media.Exists) throw new FileNotFoundException("Workspace media was not found.", media.FullName);
        string digest;
        await using (FileStream stream = media.OpenRead())
            digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();

        string fullWorkspacePath = Path.GetFullPath(workspacePath);
        string? directory = Path.GetDirectoryName(fullWorkspacePath);
        if (string.IsNullOrEmpty(directory)) throw new ArgumentException("Workspace path must have a parent directory.", nameof(workspacePath));
        Directory.CreateDirectory(directory);
        string relativeMedia = Path.GetRelativePath(directory, media.FullName);
        var workspace = new PortableAnalysisWorkspace(CurrentFormatId, CurrentSchemaVersion, relativeMedia, digest, media.Length,
            width, height, timestamp, analysis);
        string tempPath = fullWorkspacePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(stream, workspace, JsonOptions, cancellationToken);
            File.Move(tempPath, fullWorkspacePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public static async Task<(PortableAnalysisWorkspace Workspace, string MediaPath)> LoadAsync(
        string workspacePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        string fullPath = Path.GetFullPath(workspacePath);
        await using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        PortableAnalysisWorkspace workspace = await JsonSerializer.DeserializeAsync<PortableAnalysisWorkspace>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Workspace file is empty.");
        if (workspace.FormatId != CurrentFormatId)
            throw new InvalidDataException($"Unsupported workspace format '{workspace.FormatId}'.");
        if (workspace.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported workspace schema version {workspace.SchemaVersion}.");
        ValidateDimensions(workspace.Width, workspace.Height);
        if (workspace.DistanceAnalysis is null) throw new InvalidDataException("Workspace distance analysis is missing.");
        ValidateAnalysis(workspace.DistanceAnalysis, workspace.Width, workspace.Height);
        if (string.IsNullOrWhiteSpace(workspace.MediaReference) || Path.IsPathRooted(workspace.MediaReference))
            throw new InvalidDataException("Workspace media reference must be relative.");
        if (workspace.MediaSha256 is null || workspace.MediaSha256.Length != 64 || !workspace.MediaSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Workspace media SHA-256 is malformed.");

        string baseDirectory = Path.GetDirectoryName(fullPath)!;
        string mediaPath = Path.GetFullPath(Path.Combine(baseDirectory, workspace.MediaReference));
        if (!File.Exists(mediaPath)) throw new FileNotFoundException("Workspace media reference does not exist.", mediaPath);
        var media = new FileInfo(mediaPath);
        if (media.Length != workspace.MediaLength)
            throw new InvalidDataException("Workspace media length does not match the saved media.");
        await using (FileStream mediaStream = media.OpenRead())
        {
            string digest = Convert.ToHexString(await SHA256.HashDataAsync(mediaStream, cancellationToken)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(workspace.MediaSha256), Convert.FromHexString(digest)))
                throw new InvalidDataException("Workspace media SHA-256 does not match the saved media.");
        }
        return (workspace, mediaPath);
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new InvalidDataException("Workspace dimensions must be positive.");
    }

    private static void ValidateAnalysis(PortableDistanceAnalysis analysis, int width, int height)
    {
        if (!double.IsFinite(analysis.KnownDistance) || analysis.KnownDistance <= 0 || string.IsNullOrWhiteSpace(analysis.Unit))
            throw new InvalidDataException("Calibration distance and unit must be valid.");
        if (!double.IsFinite(analysis.MeasuredDistance) || analysis.MeasuredDistance < 0)
            throw new InvalidDataException("Measured distance must be finite and nonnegative.");
        ValidatePoint(analysis.CalibrationStart, width, height);
        ValidatePoint(analysis.CalibrationEnd, width, height);
        ValidatePoint(analysis.MeasureStart, width, height);
        ValidatePoint(analysis.MeasureEnd, width, height);
        double calibrationPixels = Distance(analysis.CalibrationStart, analysis.CalibrationEnd);
        if (calibrationPixels <= 0) throw new InvalidDataException("Calibration endpoints must differ.");
        double expected = Distance(analysis.MeasureStart, analysis.MeasureEnd) * analysis.KnownDistance / calibrationPixels;
        if (!double.IsFinite(expected) || Math.Abs(expected - analysis.MeasuredDistance) > Math.Max(1e-9, expected * 1e-9))
            throw new InvalidDataException("Saved measured value does not match its geometry and calibration.");
    }

    private static void ValidatePoint(PortableImagePoint point, int width, int height)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || point.X < 0 || point.Y < 0 || point.X > width || point.Y > height)
            throw new InvalidDataException("Analysis point is outside the image bounds or non-finite.");
    }

    public static double Distance(PortableImagePoint start, PortableImagePoint end)
    {
        double x = end.X - start.X;
        double y = end.Y - start.Y;
        return Math.Sqrt(x * x + y * y);
    }
}
