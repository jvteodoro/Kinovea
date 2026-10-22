using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using Kinovea.PortablePixels;
using SkiaSharp;

namespace Kinovea.PortableImageReader;

/// <summary>Reads single-frame BMP, PNG, and JPEG files into independently owned portable pixels.</summary>
public sealed class PortableImageReader
{
    /// <summary>
    /// Decodes a BMP, PNG, or JPEG file into RGB24 or straight-alpha RGBA32 storage.
    /// The file and native decoder resources are released before this method returns.
    /// </summary>
    /// <exception cref="NotSupportedException">The path extension or encoded image format is unsupported.</exception>
    /// <exception cref="InvalidDataException">The file is not a complete, decodable BMP or PNG.</exception>
    public PixelBuffer Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string extension = Path.GetExtension(path);
        if (!extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Portable image reader does not support the '{extension}' extension.");
        }

        using SKCodec? codec = SKCodec.Create(path);
        if (codec is null)
            throw new InvalidDataException($"Could not decode '{path}' as a BMP or PNG image.");

        if (codec.EncodedFormat is not (SKEncodedImageFormat.Bmp or SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg))
        {
            throw new NotSupportedException(
                $"Portable image reader does not support encoded format '{codec.EncodedFormat}'.");
        }

        SKImageInfo encodedInfo = codec.Info;
        if (encodedInfo.Width <= 0 || encodedInfo.Height <= 0)
            throw new InvalidDataException("Image dimensions must be positive.");

        bool hasAlpha = encodedInfo.AlphaType != SKAlphaType.Opaque;
        PixelFormat outputFormat = hasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24;
        AlphaMode outputAlphaMode = hasAlpha ? AlphaMode.Straight : AlphaMode.None;
        int outputBytesPerPixel = hasAlpha ? 4 : 3;

        int outputWidth;
        int outputHeight;
        // Windows GDI+ did not expose the generated PNG eXIf orientation 6
        // fixture as a PropertyItem. Match that measured reader result rather
        // than applying PNG metadata independently.
        LegacyQuarterTurn turn = GetLegacyQuarterTurn((ushort)codec.EncodedOrigin);
        if (turn is LegacyQuarterTurn.Clockwise90 or LegacyQuarterTurn.Clockwise270)
        {
            outputWidth = encodedInfo.Height;
            outputHeight = encodedInfo.Width;
        }
        else
        {
            outputWidth = encodedInfo.Width;
            outputHeight = encodedInfo.Height;
        }

        int rowBytes;
        int totalBytes;
        try
        {
            rowBytes = checked(outputWidth * outputBytesPerPixel);
            totalBytes = checked(rowBytes * outputHeight);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException("Image pixel storage exceeds the supported Int32 buffer size.", ex);
        }

        var decodeInfo = new SKImageInfo(
            encodedInfo.Width,
            encodedInfo.Height,
            SKColorType.Rgba8888,
            hasAlpha ? SKAlphaType.Unpremul : SKAlphaType.Opaque);

        using var decoded = new SKBitmap(decodeInfo);
        if (decoded.GetPixels() == IntPtr.Zero)
            throw new InvalidDataException("SkiaSharp could not allocate decoded pixel storage.");

        SKCodecResult decodeResult = codec.GetPixels(
            decodeInfo, decoded.GetPixels(), decoded.RowBytes, new SKCodecOptions());
        if (decodeResult != SKCodecResult.Success)
            throw new InvalidDataException($"Image decode failed with codec result '{decodeResult}'.");

        var owner = new ImageArrayOwner(totalBytes);
        try
        {
            CopyPixels(decoded, owner.Buffer, encodedInfo.Width, encodedInfo.Height,
                outputWidth, outputHeight, outputBytesPerPixel, hasAlpha, turn);

            PixelBuffer result = PixelBuffer.Create(
                outputWidth,
                outputHeight,
                outputFormat,
                outputAlphaMode,
                rowBytes,
                0,
                owner);
            owner = null!;
            return result;
        }
        finally
        {
            owner?.Dispose();
        }
    }

    private static void CopyPixels(
        SKBitmap source,
        byte[] destination,
        int sourceWidth,
        int sourceHeight,
        int destinationWidth,
        int destinationHeight,
        int outputBytesPerPixel,
        bool hasAlpha,
        LegacyQuarterTurn turn)
    {
        int sourceRowBytes = checked(sourceWidth * 4);
        var sourceRow = new byte[sourceRowBytes];
        IntPtr pixels = source.GetPixels();

        for (int sourceY = 0; sourceY < sourceHeight; sourceY++)
        {
            Marshal.Copy(IntPtr.Add(pixels, checked(sourceY * source.RowBytes)), sourceRow, 0, sourceRowBytes);
            for (int sourceX = 0; sourceX < sourceWidth; sourceX++)
            {
                int destinationX;
                int destinationY;
                switch (turn)
                {
                    case LegacyQuarterTurn.Clockwise90:
                        destinationX = sourceHeight - 1 - sourceY;
                        destinationY = sourceX;
                        break;
                    case LegacyQuarterTurn.Half:
                        destinationX = sourceWidth - 1 - sourceX;
                        destinationY = sourceHeight - 1 - sourceY;
                        break;
                    case LegacyQuarterTurn.Clockwise270:
                        destinationX = sourceY;
                        destinationY = sourceWidth - 1 - sourceX;
                        break;
                    default:
                        destinationX = sourceX;
                        destinationY = sourceY;
                        break;
                }

                if ((uint)destinationX >= (uint)destinationWidth || (uint)destinationY >= (uint)destinationHeight)
                    throw new InvalidDataException("Decoded orientation produced coordinates outside the output image.");

                int sourceOffset = checked(sourceX * 4);
                int destinationOffset = checked(destinationY * checked(destinationWidth * outputBytesPerPixel) +
                                                destinationX * outputBytesPerPixel);
                destination[destinationOffset] = sourceRow[sourceOffset];
                destination[destinationOffset + 1] = sourceRow[sourceOffset + 1];
                destination[destinationOffset + 2] = sourceRow[sourceOffset + 2];
                if (hasAlpha)
                    destination[destinationOffset + 3] = sourceRow[sourceOffset + 3];
            }
        }
    }

    private static LegacyQuarterTurn GetLegacyQuarterTurn(ushort origin) => origin switch
    {
        3 => LegacyQuarterTurn.Half,
        6 => LegacyQuarterTurn.Clockwise90,
        8 => LegacyQuarterTurn.Clockwise270,
        // Kinovea's current GetRotation maps mirrored EXIF values to Rotate0.
        _ => LegacyQuarterTurn.None
    };

    private enum LegacyQuarterTurn
    {
        None,
        Clockwise90,
        Half,
        Clockwise270
    }
}

internal sealed class ImageArrayOwner : IMemoryOwner<byte>
{
    private byte[]? buffer;

    public ImageArrayOwner(int length) => buffer = new byte[length];

    public byte[] Buffer => buffer ?? throw new ObjectDisposedException(nameof(ImageArrayOwner));

    public Memory<byte> Memory => Buffer;

    public void Dispose() => buffer = null;
}
