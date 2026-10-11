using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Kinovea.Bitmap.Characterization
{
    internal sealed class FixtureSpec
    {
        public string Id;
        public string FileName;
        public string Format;
        public int Width;
        public int Height;
        public int? ExifOrientation;
        public int? ExpectedQuarterTurns;
        public bool Transparent;
        public string Sha256;
        public string Path;
    }

    internal static class FixtureFactory
    {
        private const int JpegQuality = 93;

        public static List<FixtureSpec> Generate(string directory)
        {
            Directory.CreateDirectory(directory);
            var fixtures = new List<FixtureSpec>();

            AddBitmapFixture(fixtures, directory, "bmp-rgb-7x5", "bmp-rgb-7x5.bmp", "BMP", 7, 5, false, null);
            AddBitmapFixture(fixtures, directory, "png-alpha-13x9", "png-alpha-13x9.png", "PNG", 13, 9, true, null);
            AddJpegFixture(fixtures, directory, "jpeg-rgb-11x7", "jpeg-rgb-11x7.jpg", null);

            byte[] jpeg = EncodeJpeg(11, 7);
            for (ushort orientation = 1; orientation <= 9; orientation++)
            {
                string id = "jpeg-exif-orientation-" + orientation.ToString("00");
                string fileName = id + ".jpg";
                string path = System.IO.Path.Combine(directory, fileName);
                byte[] oriented = InsertExifOrientation(jpeg, orientation);
                File.WriteAllBytes(path, oriented);
                fixtures.Add(CreateSpec(id, fileName, "JPEG", 11, 7, orientation, ExpectedRotation(orientation), false, path));
            }

            string corruptPath = System.IO.Path.Combine(directory, "corrupt-image.png");
            File.WriteAllBytes(corruptPath, new byte[] { 0x4e, 0x4f, 0x54, 0x2d, 0x41, 0x2d, 0x50, 0x4e, 0x47 });
            fixtures.Add(CreateSpec("corrupt-png", "corrupt-image.png", "INVALID", 0, 0, null, null, false, corruptPath));

            string unsupportedPath = System.IO.Path.Combine(directory, "unsupported.gif");
            File.WriteAllBytes(unsupportedPath, new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 });
            fixtures.Add(CreateSpec("unsupported-gif", "unsupported.gif", "UNSUPPORTED", 0, 0, null, null, false, unsupportedPath));

            return fixtures;
        }

        public static Dictionary<string, object> ToManifestEntry(FixtureSpec fixture)
        {
            return new Dictionary<string, object>
            {
                { "id", fixture.Id },
                { "fileName", fixture.FileName },
                { "format", fixture.Format },
                { "width", fixture.Width },
                { "height", fixture.Height },
                { "exifOrientation", fixture.ExifOrientation },
                { "expectedQuarterTurns", fixture.ExpectedQuarterTurns },
                { "transparent", fixture.Transparent },
                { "generator", "FixtureFactory v1; deterministic pixel formula; Windows GDI+ encoders" },
                { "jpegQuality", fixture.Format == "JPEG" ? (object)JpegQuality : null },
                { "sha256", fixture.Sha256 }
            };
        }

        public static Color PatternPixel(int x, int y, bool transparent)
        {
            int alpha = 255;
            if (transparent)
            {
                if (x == 0 && y == 0) alpha = 0;
                else if (x == 1 && y == 0) alpha = 64;
                else if (x == 2 && y == 0) alpha = 128;
            }

            return Color.FromArgb(
                alpha,
                (x * 37 + y * 11 + 17) & 255,
                (x * 17 + y * 53 + 29) & 255,
                (x * 91 + y * 7 + 43) & 255);
        }

        private static void AddBitmapFixture(List<FixtureSpec> fixtures, string directory, string id, string fileName, string format, int width, int height, bool transparent, int? orientation)
        {
            string path = System.IO.Path.Combine(directory, fileName);
            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                        bitmap.SetPixel(x, y, PatternPixel(x, y, transparent));
                }

                ImageFormat imageFormat = format == "PNG" ? ImageFormat.Png : ImageFormat.Bmp;
                bitmap.Save(path, imageFormat);
            }

            fixtures.Add(CreateSpec(id, fileName, format, width, height, orientation, ExpectedRotation(orientation), transparent, path));
        }

        private static void AddJpegFixture(List<FixtureSpec> fixtures, string directory, string id, string fileName, int? orientation)
        {
            string path = System.IO.Path.Combine(directory, fileName);
            File.WriteAllBytes(path, EncodeJpeg(11, 7));
            fixtures.Add(CreateSpec(id, fileName, "JPEG", 11, 7, orientation, ExpectedRotation(orientation), false, path));
        }

        private static FixtureSpec CreateSpec(string id, string fileName, string format, int width, int height, int? orientation, int? quarterTurns, bool transparent, string path)
        {
            return new FixtureSpec
            {
                Id = id,
                FileName = fileName,
                Format = format,
                Width = width,
                Height = height,
                ExifOrientation = orientation,
                ExpectedQuarterTurns = quarterTurns,
                Transparent = transparent,
                Path = path,
                Sha256 = EvidenceWriter.Sha256File(path)
            };
        }

        private static int? ExpectedRotation(int? orientation)
        {
            if (!orientation.HasValue) return 0;
            switch (orientation.Value)
            {
                case 3: return 2;
                case 6: return 1;
                case 8: return 3;
                // Flip and rotate+flip EXIF cases map to Rotate0 in current source.
                default: return 0;
            }
        }

        private static byte[] EncodeJpeg(int width, int height)
        {
            using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                        bitmap.SetPixel(x, y, PatternPixel(x, y, false));
                }

                ImageCodecInfo jpegEncoder = null;
                foreach (ImageCodecInfo encoder in ImageCodecInfo.GetImageEncoders())
                {
                    if (encoder.FormatID == ImageFormat.Jpeg.Guid)
                    {
                        jpegEncoder = encoder;
                        break;
                    }
                }
                if (jpegEncoder == null) throw new InvalidOperationException("GDI+ JPEG encoder was not found.");

                using (var stream = new MemoryStream())
                using (var parameters = new EncoderParameters(1))
                {
                    parameters.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
                    bitmap.Save(stream, jpegEncoder, parameters);
                    return stream.ToArray();
                }
            }
        }

        private static byte[] InsertExifOrientation(byte[] jpeg, ushort orientation)
        {
            if (jpeg.Length < 2 || jpeg[0] != 0xff || jpeg[1] != 0xd8)
                throw new InvalidDataException("The GDI+ JPEG encoder did not produce a JPEG SOI marker.");

            // APP1 payload: Exif\0\0 followed by a little-endian TIFF IFD0 with tag 0x0112.
            byte[] payload = new byte[]
            {
                0x45, 0x78, 0x69, 0x66, 0x00, 0x00,
                0x49, 0x49, 0x2a, 0x00, 0x08, 0x00, 0x00, 0x00,
                0x01, 0x00,
                0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,
                (byte)(orientation & 0xff), (byte)(orientation >> 8), 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00
            };

            int segmentLength = payload.Length + 2;
            using (var output = new MemoryStream(jpeg.Length + payload.Length + 4))
            {
                output.Write(jpeg, 0, 2);
                output.WriteByte(0xff);
                output.WriteByte(0xe1);
                output.WriteByte((byte)(segmentLength >> 8));
                output.WriteByte((byte)(segmentLength & 0xff));
                output.Write(payload, 0, payload.Length);
                output.Write(jpeg, 2, jpeg.Length - 2);
                return output.ToArray();
            }
        }
    }
}
