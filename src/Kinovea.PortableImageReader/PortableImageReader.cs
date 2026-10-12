using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using Kinovea.PortablePixels;
using SkiaSharp;

namespace Kinovea.PortableImageReader;

/// <summary>Reads single-frame BMP and PNG files into independently owned portable pixels.</summary>
public sealed class PortableImageReader
{
    /// <summary>
    /// Decodes a BMP or PNG file into RGB24 or straight-alpha RGBA32 storage.
    /// The file and native decoder resources are released before this method returns.
    /// </summary>
    /// <exception cref="NotSupportedException">The path extension or encoded image format is unsupported.</exception>
    /// <exception cref="InvalidDataException">The file is not a complete, decodable BMP or PNG.</exception>
    public PixelBuffer Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string extension = Path.GetExtension(path);
        if (!extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Portable image reader does not support the '{extension}' extension.");
        }

        ushort? pngExifOrientation = ReadPngExifOrientation(path);
        using SKCodec? codec = SKCodec.Create(path);
        if (codec is null)
            throw new InvalidDataException($"Could not decode '{path}' as a BMP or PNG image.");

        if (codec.EncodedFormat is not (SKEncodedImageFormat.Bmp or SKEncodedImageFormat.Png))
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
        LegacyQuarterTurn turn = GetLegacyQuarterTurn(
            codec.EncodedFormat == SKEncodedImageFormat.Png
                ? pngExifOrientation ?? (ushort)codec.EncodedOrigin
                : (ushort)codec.EncodedOrigin);
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

    private static ushort? ReadPngExifOrientation(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> signature = stackalloc byte[8];
        if (stream.Read(signature) != signature.Length ||
            !signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            return null;
        }

        Span<byte> chunkHeader = stackalloc byte[8];
        Span<byte> crc = stackalloc byte[4];
        while (stream.Position <= stream.Length - chunkHeader.Length)
        {
            if (stream.Read(chunkHeader) != chunkHeader.Length)
                throw new InvalidDataException("PNG chunk header is truncated.");

            uint length = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader);
            long bytesRemaining = stream.Length - stream.Position;
            if (length > int.MaxValue || length + 4L > bytesRemaining)
                throw new InvalidDataException("PNG chunk length exceeds the remaining file data.");

            bool isExif = chunkHeader[4] == (byte)'e' && chunkHeader[5] == (byte)'X' &&
                          chunkHeader[6] == (byte)'I' && chunkHeader[7] == (byte)'f';
            if (isExif)
            {
                if (length > 1024 * 1024)
                    throw new InvalidDataException("PNG eXIf chunk exceeds the 1 MiB metadata limit.");

                byte[] payload = new byte[(int)length];
                stream.ReadExactly(payload);
                stream.ReadExactly(crc);
                return ParseTiffOrientation(payload);
            }

            string chunkType = System.Text.Encoding.ASCII.GetString(chunkHeader[4..]);
            if (chunkType is "IDAT" or "IEND")
                return null;

            stream.Seek(checked((long)length + 4), SeekOrigin.Current);
        }

        return null;
    }

    private static ushort? ParseTiffOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
            throw new InvalidDataException("PNG eXIf TIFF header is truncated.");

        bool littleEndian = tiff[0] == (byte)'I' && tiff[1] == (byte)'I';
        bool bigEndian = tiff[0] == (byte)'M' && tiff[1] == (byte)'M';
        if (!littleEndian && !bigEndian)
            throw new InvalidDataException("PNG eXIf TIFF byte order is invalid.");

        if (ReadTiffUInt16(tiff, 2, littleEndian) != 42)
            throw new InvalidDataException("PNG eXIf TIFF magic is invalid.");

        uint ifdOffset = ReadTiffUInt32(tiff, 4, littleEndian);
        if (ifdOffset > tiff.Length - 2)
            throw new InvalidDataException("PNG eXIf IFD0 offset is outside the metadata chunk.");

        int ifd = (int)ifdOffset;
        ushort entryCount = ReadTiffUInt16(tiff, ifd, littleEndian);
        long entriesEnd = (long)ifd + 2 + (long)entryCount * 12;
        if (entriesEnd > tiff.Length)
            throw new InvalidDataException("PNG eXIf IFD0 entries are truncated.");

        for (int index = 0; index < entryCount; index++)
        {
            int entry = checked(ifd + 2 + index * 12);
            if (ReadTiffUInt16(tiff, entry, littleEndian) != 0x0112)
                continue;

            ushort type = ReadTiffUInt16(tiff, entry + 2, littleEndian);
            uint count = ReadTiffUInt32(tiff, entry + 4, littleEndian);
            if (type != 3 || count == 0)
                throw new InvalidDataException("PNG eXIf orientation tag has an unsupported TIFF type or count.");

            int valueOffset = count == 1 ? entry + 8 : checked((int)ReadTiffUInt32(tiff, entry + 8, littleEndian));
            if (valueOffset < 0 || valueOffset > tiff.Length - 2)
                throw new InvalidDataException("PNG eXIf orientation value is outside the metadata chunk.");

            return ReadTiffUInt16(tiff, valueOffset, littleEndian);
        }

        return null;
    }

    private static ushort ReadTiffUInt16(ReadOnlySpan<byte> tiff, int offset, bool littleEndian) => littleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(tiff.Slice(offset, 2))
        : BinaryPrimitives.ReadUInt16BigEndian(tiff.Slice(offset, 2));

    private static uint ReadTiffUInt32(ReadOnlySpan<byte> tiff, int offset, bool littleEndian) => littleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(tiff.Slice(offset, 4))
        : BinaryPrimitives.ReadUInt32BigEndian(tiff.Slice(offset, 4));

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
