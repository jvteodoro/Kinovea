namespace Kinovea.PortablePixels;

/// <summary>Produces deterministic RGB24 test pixels without image libraries.</summary>
public static class SyntheticPixelProducer
{
    /// <summary>
    /// Creates top-down RGB24 pixels. Each channel is a deterministic function
    /// of x and y; optional row padding is initialized to 0xCD and excluded from
    /// logical row spans.
    /// </summary>
    public static PixelBuffer CreateRgb24(int width, int height, int paddingBytes = 0)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (paddingBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(paddingBytes));

        int rowBytes = checked(width * 3);
        int stride = checked(rowBytes + paddingBytes);
        int length = checked(stride * height);
        var owner = new ArrayMemoryOwner(length);
        Memory<byte> memory = owner.Memory;

        for (int y = 0; y < height; y++)
        {
            Span<byte> row = memory.Span.Slice(y * stride, rowBytes);
            for (int x = 0; x < width; x++)
            {
                int pixel = x * 3;
                row[pixel] = Red(x, y);
                row[pixel + 1] = Green(x, y);
                row[pixel + 2] = Blue(x, y);
            }

            memory.Span.Slice(y * stride + rowBytes, paddingBytes).Fill(0xCD);
        }

        return PixelBuffer.Create(width, height, PixelFormat.Rgb24, AlphaMode.None, stride, 0, owner);
    }

    public static byte Red(int x, int y) => unchecked((byte)(17 + 31 * x + 7 * y));
    public static byte Green(int x, int y) => unchecked((byte)(29 + 11 * x + 37 * y));
    public static byte Blue(int x, int y) => unchecked((byte)(43 + 19 * x + 13 * y));
}
