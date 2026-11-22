using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using QRCoder;

namespace TeleClean.App.Helpers;

/// <summary>
/// High-performance QR Code matrix generator and WinUI 3 SoftwareBitmap renderer.
/// Converts Telegram authentication URLs (tg://login?token=...) into scannable QR bitmaps.
/// </summary>
public static class QrCodeRenderer
{
    /// <summary>
    /// Generates a boolean 2D matrix representing QR modules.
    /// </summary>
    public static bool[,] GenerateMatrix(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);

        int count = data.ModuleMatrix.Count;
        bool[,] matrix = new bool[count, count];

        for (int y = 0; y < count; y++)
        {
            var row = data.ModuleMatrix[y];
            for (int x = 0; x < count; x++)
            {
                matrix[y, x] = row[x];
            }
        }

        return matrix;
    }

    /// <summary>
    /// Generates raw BGRA32 pixel buffer with quiet zone padding.
    /// </summary>
    public static byte[] GenerateBgraPixels(string text, out int pixelWidth, out int pixelHeight, int pixelsPerModule = 8, int quietZoneModules = 4)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (pixelsPerModule < 1) pixelsPerModule = 1;
        if (quietZoneModules < 0) quietZoneModules = 0;

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);

        int moduleCount = data.ModuleMatrix.Count;
        int totalModules = moduleCount + (quietZoneModules * 2);
        int size = totalModules * pixelsPerModule;

        pixelWidth = size;
        pixelHeight = size;

        byte[] buffer = new byte[size * size * 4]; // BGRA8
        Array.Fill<byte>(buffer, 255); // White background

        for (int r = 0; r < moduleCount; r++)
        {
            var row = data.ModuleMatrix[r];
            for (int c = 0; c < moduleCount; c++)
            {
                if (row[c]) // Dark module
                {
                    int startY = (r + quietZoneModules) * pixelsPerModule;
                    int startX = (c + quietZoneModules) * pixelsPerModule;

                    for (int y = 0; y < pixelsPerModule; y++)
                    {
                        int rowOffset = (startY + y) * size * 4;
                        for (int x = 0; x < pixelsPerModule; x++)
                        {
                            int offset = rowOffset + (startX + x) * 4;
                            buffer[offset + 0] = 0;   // Blue
                            buffer[offset + 1] = 0;   // Green
                            buffer[offset + 2] = 0;   // Red
                            buffer[offset + 3] = 255; // Alpha
                        }
                    }
                }
            }
        }

        return buffer;
    }

    /// <summary>
    /// Generates a Windows.Graphics.Imaging.SoftwareBitmap ready for display in WinUI 3 via SoftwareBitmapSource.
    /// </summary>
    public static SoftwareBitmap GenerateSoftwareBitmap(string text, int pixelsPerModule = 8, int quietZoneModules = 4)
    {
        byte[] buffer = GenerateBgraPixels(text, out int width, out int height, pixelsPerModule, quietZoneModules);

        var bitmap = new SoftwareBitmap(
            BitmapPixelFormat.Bgra8,
            width,
            height,
            BitmapAlphaMode.Premultiplied);

        bitmap.CopyFromBuffer(buffer.AsBuffer());
        return bitmap;
    }
}
