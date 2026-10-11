using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Kinovea.Bitmap.Characterization
{
    internal static class EvidenceWriter
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 100
        };

        public static void WriteJson(string path, object value)
        {
            string json = Serializer.Serialize(value);
            File.WriteAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
        }

        public static string Sha256File(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return ToLowerHex(sha.ComputeHash(stream));
        }

        public static string Sha256Bytes(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return ToLowerHex(sha.ComputeHash(bytes));
        }

        public static Dictionary<string, object> CaptureBitmap(Bitmap bitmap, string id, string outputDirectory)
        {
            string pixelDirectory = Path.Combine(outputDirectory, "pixels");
            Directory.CreateDirectory(pixelDirectory);

            int width = bitmap.Width;
            int height = bitmap.Height;
            byte[] rgba = new byte[checked(width * height * 4)];
            int transparentCount = 0;
            int translucentCount = 0;
            int minAlpha = 255;
            int maxAlpha = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color color = bitmap.GetPixel(x, y);
                    int offset = ((y * width) + x) * 4;
                    rgba[offset] = color.R;
                    rgba[offset + 1] = color.G;
                    rgba[offset + 2] = color.B;
                    rgba[offset + 3] = color.A;
                    if (color.A == 0) transparentCount++;
                    if (color.A > 0 && color.A < 255) translucentCount++;
                    minAlpha = Math.Min(minAlpha, color.A);
                    maxAlpha = Math.Max(maxAlpha, color.A);
                }
            }

            string rgbaName = id + ".rgba";
            string rgbaPath = Path.Combine(pixelDirectory, rgbaName);
            File.WriteAllBytes(rgbaPath, rgba);

            int bitsPerPixel = Image.GetPixelFormatSize(bitmap.PixelFormat);
            string nativeName = id + ".native.bin";
            string nativePath = Path.Combine(pixelDirectory, nativeName);
            int? stride = null;
            int? nativeRowBytes = null;
            string nativeCaptureError = null;
            try
            {
                var rectangle = new Rectangle(0, 0, width, height);
                BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, bitmap.PixelFormat);
                try
                {
                    int rowBytes = (int)(((long)width * bitsPerPixel + 7) / 8);
                    if (rowBytes < 1)
                        throw new InvalidOperationException("GDI+ reported an unknown pixel format.");
                    stride = data.Stride;
                    nativeRowBytes = rowBytes;

                    byte[] row = new byte[rowBytes];
                    using (var stream = File.Create(nativePath))
                    {
                        for (int y = 0; y < height; y++)
                        {
                            IntPtr scanLine = IntPtr.Add(data.Scan0, checked(y * data.Stride));
                            Marshal.Copy(scanLine, row, 0, row.Length);
                            stream.Write(row, 0, row.Length);
                        }
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
            catch (Exception ex)
            {
                nativeCaptureError = ex.GetType().FullName + ": " + ex.Message;
                if (File.Exists(nativePath)) File.Delete(nativePath);
            }

            string exifOrientation = null;
            try
            {
                const int orientationId = 0x112;
                foreach (int propertyId in bitmap.PropertyIdList)
                {
                    if (propertyId == orientationId)
                    {
                        PropertyItem item = bitmap.GetPropertyItem(orientationId);
                        if (item.Value != null && item.Value.Length >= 2)
                            exifOrientation = BitConverter.ToUInt16(item.Value, 0).ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
            catch (Exception ex)
            {
                exifOrientation = "capture-error:" + ex.GetType().FullName;
            }

            var result = new Dictionary<string, object>
            {
                { "width", width },
                { "height", height },
                { "pixelFormat", bitmap.PixelFormat.ToString() },
                { "bitsPerPixel", bitsPerPixel },
                { "strideBytes", stride },
                { "nativeRowBytes", nativeRowBytes },
                { "nativeStorage", "LockBits scanlines in logical top-to-bottom order; row padding omitted" },
                { "nativeChannelSemantics", "GDI+ native order and alpha association as indicated by pixelFormat" },
                { "nativeFile", nativeCaptureError == null ? "pixels/" + nativeName : null },
                { "nativeSha256", nativeCaptureError == null ? Sha256File(nativePath) : null },
                { "nativeCaptureError", nativeCaptureError },
                { "rgbaFile", "pixels/" + rgbaName },
                { "rgbaSha256", Sha256Bytes(rgba) },
                { "rgbaLayout", "top-to-bottom, left-to-right, 8-bit R,G,B,A" },
                { "dpiX", bitmap.HorizontalResolution },
                { "dpiY", bitmap.VerticalResolution },
                { "alphaMin", minAlpha },
                { "alphaMax", maxAlpha },
                { "fullyTransparentPixelCount", transparentCount },
                { "partiallyTransparentPixelCount", translucentCount },
                { "exifOrientationPropertyAfterRead", exifOrientation }
            };
            return result;
        }

        public static Dictionary<string, object> CaptureRuntimeMetadata(string outputDirectory, string readerAssemblyPath)
        {
            string gdiplusPath = Path.Combine(Environment.SystemDirectory, "gdiplus.dll");
            string gdiplusVersion = File.Exists(gdiplusPath) ? FileVersionInfo.GetVersionInfo(gdiplusPath).FileVersion : null;
            string log4netPath = typeof(log4net.ILog).Assembly.Location;
            var metadata = new Dictionary<string, object>
            {
                { "capturedUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                { "osVersion", Environment.OSVersion.VersionString },
                { "clrVersion", Environment.Version.ToString() },
                { "frameworkDescription", ".NET Framework 4.8 targeting executable; actual runtime version recorded by Environment.Version" },
                { "processBitness", IntPtr.Size * 8 },
                { "culture", CultureInfo.CurrentCulture.Name },
                { "systemDrawingAssemblyVersion", typeof(Bitmap).Assembly.GetName().Version.ToString() },
                { "gdiplusPath", gdiplusPath },
                { "gdiplusFileVersion", gdiplusVersion },
                { "log4netAssemblyVersion", typeof(log4net.ILog).Assembly.GetName().Version.ToString() },
                { "log4netAssemblyPath", log4netPath },
                { "log4netAssemblySha256", File.Exists(log4netPath) ? Sha256File(log4netPath) : null },
                { "readerType", typeof(Kinovea.Video.Bitmap.VideoReaderBitmap).FullName },
                { "readerAssemblyPath", readerAssemblyPath },
                { "readerAssemblySha256", File.Exists(readerAssemblyPath) ? Sha256File(readerAssemblyPath) : null },
                { "fixtureGeneratorVersion", 1 }
            };
            WriteJson(Path.Combine(outputDirectory, "runtime-metadata.json"), metadata);
            return metadata;
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }
}
