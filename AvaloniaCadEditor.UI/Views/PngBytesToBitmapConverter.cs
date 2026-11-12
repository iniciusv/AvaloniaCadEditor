using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace AvaloniaCadEditor.UI.Views;

/// <summary>
/// PNG em bytes (a miniatura da biblioteca) → <see cref="Bitmap"/>. O ViewModel guarda só os bytes, para os
/// testes não dependerem da plataforma de renderização do Avalonia.
/// </summary>
public sealed class PngBytesToBitmapConverter : IValueConverter
{
    public static PngBytesToBitmapConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } png) return null;
        try
        {
            return new Bitmap(new MemoryStream(png));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
