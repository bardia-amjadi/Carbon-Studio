using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CarbonStudio.App.Services;

public enum SaveChoice
{
    Save,
    DontSave,
    Cancel,
}

/// <summary>Shows modal <see cref="ContentDialog"/>s one at a time (WinUI allows only one open dialog per window).</summary>
internal sealed class DialogService
{
    private readonly Func<XamlRoot?> _xamlRoot;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DialogService(Func<XamlRoot?> xamlRoot) => _xamlRoot = xamlRoot;

    public async Task ShowMessageAsync(string title, string message)
    {
        await ShowAsync(new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
        });
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var result = await ShowAsync(new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirmText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        });
        return result == ContentDialogResult.Primary;
    }

    public async Task<SaveChoice> AskSaveAsync(string message)
    {
        var result = await ShowAsync(new ContentDialog
        {
            Title = "Unsaved changes",
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Don't save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        });

        return result switch
        {
            ContentDialogResult.Primary => SaveChoice.Save,
            ContentDialogResult.Secondary => SaveChoice.DontSave,
            _ => SaveChoice.Cancel,
        };
    }

    /// <summary>Prompts for a single line of text; returns null when cancelled.</summary>
    public async Task<string?> PromptAsync(string title, string label, string initialValue = "", string confirmText = "OK")
    {
        var box = new TextBox { Text = initialValue, PlaceholderText = label, MinWidth = 320 };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = box,
            PrimaryButtonText = confirmText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        // Select the name without the extension, as Explorer does for renames.
        dialog.Opened += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            var dot = initialValue.LastIndexOf('.');
            box.Select(0, dot > 0 ? dot : initialValue.Length);
        };

        var result = await ShowAsync(dialog);
        return result == ContentDialogResult.Primary ? box.Text.Trim() : null;
    }

    public async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        await _gate.WaitAsync();
        try
        {
            dialog.XamlRoot = _xamlRoot();
            return await dialog.ShowAsync();
        }
        finally
        {
            _gate.Release();
        }
    }
}
