using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Kinovea.PortablePixels;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace Kinovea.PortablePixels.GdiPlus;

/// <summary>Copies supported Windows GDI+ bitmap layouts into portable owned memory.</summary>
[SupportedOSPlatform("windows")]
public static class GdiPlusPixelBufferAdapter
{
    /// <summary>
    /// Converts supported 24/32-bit GDI+ storage to RGB24 or RGBA32. The source
    /// bitmap remains caller-owned. The returned buffer owns an independent
    /// managed byte array and is valid after the source bitmap is disposed.
    /// Conversion is synchronous; no GDI+ pointer escapes the LockBits scope.
    /// </summary>
    public static PixelBuffer Convert(Bitmap source)
        => ConvertWithOwnerFactory(source, static length => new ByteArrayOwner(length));

    internal static PixelBuffer ConvertWithOwnerFactory(Bitmap source, Func<int, IMemoryOwner<byte>> ownerFactory)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(ownerFactory);

        GdiPixelFormat sourceFormat = source.PixelFormat;
        (int sourceBytesPerPixel, PixelFormat destinationFormat, AlphaMode alphaMode) = sourceFormat switch
        {
            GdiPixelFormat.Format24bppRgb => (3, PixelFormat.Rgb24, AlphaMode.None),
            GdiPixelFormat.Format32bppRgb => (4, PixelFormat.Rgb24, AlphaMode.None),
            GdiPixelFormat.Format32bppArgb => (4, PixelFormat.Rgba32, AlphaMode.Straight),
            GdiPixelFormat.Format32bppPArgb => (4, PixelFormat.Rgba32, AlphaMode.Premultiplied),
            _ => throw new NotSupportedException($"GDI+ pixel format {sourceFormat} is not supported.")
        };

        int outputBytesPerPixel = destinationFormat == PixelFormat.Rgb24 ? 3 : 4;
        int sourceRowBytes = checked(source.Width * sourceBytesPerPixel);
        int outputStride = checked(source.Width * outputBytesPerPixel);
        int outputLength = checked(outputStride * source.Height);
        byte[] sourceRow = new byte[sourceRowBytes];
        IMemoryOwner<byte>? outputOwner = null;

        try
        {
            BitmapData data = source.LockBits(
                new Rectangle(0, 0, source.Width, source.Height),
                ImageLockMode.ReadOnly,
                sourceFormat);
            try
            {
                if (Math.Abs((long)data.Stride) < sourceRowBytes)
                    throw new InvalidOperationException("GDI+ returned a stride shorter than one pixel row.");

                outputOwner = ownerFactory(outputLength)
                    ?? throw new InvalidOperationException("The output owner factory returned null.");
                Memory<byte> output = outputOwner.Memory;
                if (output.Length < outputLength)
                    throw new ArgumentException("The output owner is smaller than the converted image.", nameof(ownerFactory));

                for (int y = 0; y < source.Height; y++)
                {
                    IntPtr sourceAddress = IntPtr.Add(data.Scan0, checked(y * data.Stride));
                    Marshal.Copy(sourceAddress, sourceRow, 0, sourceRowBytes);
                    Span<byte> destinationRow = output.Span.Slice(checked(y * outputStride), outputStride);

                    for (int x = 0; x < source.Width; x++)
                    {
                        int sourceOffset = x * sourceBytesPerPixel;
                        int destinationOffset = x * outputBytesPerPixel;
                        // GDI+ Format*Rgb/Argb/PArgb storage on Windows is B,G,R[,A].
                        // The fourth Format32bppRgb byte is unused and is intentionally dropped.
                        destinationRow[destinationOffset] = sourceRow[sourceOffset + 2];
                        destinationRow[destinationOffset + 1] = sourceRow[sourceOffset + 1];
                        destinationRow[destinationOffset + 2] = sourceRow[sourceOffset];
                        if (outputBytesPerPixel == 4)
                            destinationRow[destinationOffset + 3] = sourceRow[sourceOffset + 3];
                    }
                }
            }
            finally
            {
                source.UnlockBits(data);
            }

            IMemoryOwner<byte> ownerToTransfer = outputOwner
                ?? throw new InvalidOperationException("Conversion did not produce an output owner.");
            PixelBuffer converted = PixelBuffer.Create(
                source.Width,
                source.Height,
                destinationFormat,
                alphaMode,
                outputStride,
                0,
                ownerToTransfer);
            outputOwner = null;
            return converted;
        }
        finally
        {
            // PixelBuffer takes responsibility only after Create succeeds.
            outputOwner?.Dispose();
        }
    }

    private sealed class ByteArrayOwner : IMemoryOwner<byte>
    {
        private byte[]? bytes;

        public ByteArrayOwner(int length) => bytes = new byte[length];

        public Memory<byte> Memory => bytes ?? throw new ObjectDisposedException(nameof(ByteArrayOwner));

        public void Dispose() => bytes = null;
    }
}
