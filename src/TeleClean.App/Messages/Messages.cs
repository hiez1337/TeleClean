using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Messaging.Messages;
using TeleClean.Core.Models;

namespace TeleClean.App.Messages;

/// <summary>
/// Fired when user authentication state changes (logged in or logged out).
/// </summary>
public sealed class AuthenticationStateChangedMessage : ValueChangedMessage<bool>
{
    public AuthenticationStateChangedMessage(bool isAuthenticated) : base(isAuthenticated)
    {
    }
}

/// <summary>
/// Fired to trigger navigation to a specific view/page.
/// </summary>
public sealed class NavigationMessage : ValueChangedMessage<string>
{
    public NavigationMessage(string targetPage) : base(targetPage)
    {
    }
}

/// <summary>
/// Fired to trigger start of bulk leave/delete operations on selected items.
/// </summary>
public sealed class StartBulkExecutionMessage : ValueChangedMessage<IReadOnlyList<DialogItem>>
{
    public StartBulkExecutionMessage(IReadOnlyList<DialogItem> items) : base(items)
    {
    }
}

/// <summary>
/// Fired when application theme is changed in Settings.
/// </summary>
public sealed class ThemeChangedMessage : ValueChangedMessage<string>
{
    public ThemeChangedMessage(string theme) : base(theme)
    {
    }
}

/// <summary>
/// Fired when window backdrop material (Mica, Mica Alt, Acrylic) is changed in Settings.
/// </summary>
public sealed class BackdropChangedMessage : ValueChangedMessage<string>
{
    public BackdropChangedMessage(string backdrop) : base(backdrop)
    {
    }
}
