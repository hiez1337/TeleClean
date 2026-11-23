using System;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Windows.UI;
using WinRT;

namespace TeleClean.App.Helpers;

/// <summary>
/// Manages native Windows 11 Mica, Mica Alt, tuned Desktop Acrylic, and Solid window backdrops
/// directly via ISystemBackdropControllerWithTargets without WinUI 1.5 SystemBackdrop subclass issues.
/// </summary>
public sealed class WindowBackdropManager : IDisposable
{
    private readonly Window _window;
    private readonly FrameworkElement _themeRoot;
    private readonly SystemBackdropConfiguration _configuration = new() { IsInputActive = true };

    private MicaController? _micaController;
    private DesktopAcrylicController? _acrylicController;
    private string _currentMode = "Mica";
    private bool _disposed;

    public WindowBackdropManager(Window window, FrameworkElement themeRoot)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _themeRoot = themeRoot ?? throw new ArgumentNullException(nameof(themeRoot));
        UpdateTheme();
    }

    /// <summary>
    /// Applies the requested backdrop ("Mica", "MicaAlt", "Acrylic", or "Solid").
    /// Returns true if a translucent system backdrop is active, or false if the solid fallback layer should be shown.
    /// </summary>
    public bool Apply(string? mode)
    {
        if (_disposed) return false;

        _currentMode = string.IsNullOrWhiteSpace(mode) ? "Mica" : mode;
        DisposeControllers();
        UpdateThemeCore();

        if (string.Equals(_currentMode, "Solid", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(_currentMode, "Acrylic", StringComparison.OrdinalIgnoreCase)
            && DesktopAcrylicController.IsSupported())
        {
            _acrylicController = new DesktopAcrylicController
            {
                Kind = DesktopAcrylicKind.Thin
            };
            ApplyAcrylicTuning();
            _acrylicController.AddSystemBackdropTarget(_window.As<ICompositionSupportsSystemBackdrop>());
            _acrylicController.SetSystemBackdropConfiguration(_configuration);
            return true;
        }

        if (MicaController.IsSupported())
        {
            var kind = string.Equals(_currentMode, "MicaAlt", StringComparison.OrdinalIgnoreCase)
                ? MicaKind.BaseAlt
                : MicaKind.Base;

            _micaController = new MicaController { Kind = kind };
            _micaController.AddSystemBackdropTarget(_window.As<ICompositionSupportsSystemBackdrop>());
            _micaController.SetSystemBackdropConfiguration(_configuration);
            return true;
        }

        if (DesktopAcrylicController.IsSupported())
        {
            _acrylicController = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Base };
            ApplyAcrylicTuning();
            _acrylicController.AddSystemBackdropTarget(_window.As<ICompositionSupportsSystemBackdrop>());
            _acrylicController.SetSystemBackdropConfiguration(_configuration);
            return true;
        }

        return false;
    }

    public void UpdateTheme()
    {
        if (_disposed) return;
        UpdateThemeCore();
        ApplyAcrylicTuning();
    }

    public void UpdateActivation(bool isInputActive)
    {
        if (_disposed) return;
        _configuration.IsInputActive = isInputActive;
    }

    private void UpdateThemeCore()
    {
        _configuration.Theme = _themeRoot.ActualTheme switch
        {
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            ElementTheme.Light => SystemBackdropTheme.Light,
            _ => SystemBackdropTheme.Default
        };
    }

    private void ApplyAcrylicTuning()
    {
        if (_acrylicController == null) return;

        if (_themeRoot.ActualTheme == ElementTheme.Light)
        {
            _acrylicController.TintColor = Color.FromArgb(255, 244, 248, 253);
            _acrylicController.FallbackColor = Color.FromArgb(255, 242, 245, 249);
            _acrylicController.TintOpacity = 0.34f;
            _acrylicController.LuminosityOpacity = 0.66f;
        }
        else
        {
            _acrylicController.TintColor = Color.FromArgb(255, 20, 23, 29);
            _acrylicController.FallbackColor = Color.FromArgb(255, 25, 27, 31);
            _acrylicController.TintOpacity = 0.20f;
            _acrylicController.LuminosityOpacity = 0.54f;
        }
    }

    private void DisposeControllers()
    {
        if (_micaController != null)
        {
            _micaController.RemoveAllSystemBackdropTargets();
            _micaController.Dispose();
            _micaController = null;
        }

        if (_acrylicController != null)
        {
            _acrylicController.RemoveAllSystemBackdropTargets();
            _acrylicController.Dispose();
            _acrylicController = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeControllers();
    }
}
