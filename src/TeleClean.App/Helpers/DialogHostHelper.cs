using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace TeleClean.App.Helpers;

/// <summary>
/// Prevents concurrent ContentDialog.ShowAsync calls, which throw COMException in WinUI 3.
/// </summary>
public static class DialogHostHelper
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<ContentDialogResult> ShowSafeAsync(ContentDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        if (dialog.XamlRoot == null)
        {
            return ContentDialogResult.None;
        }

        if (!await Gate.WaitAsync(0))
        {
            return ContentDialogResult.None;
        }

        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            Gate.Release();
        }
    }
}
