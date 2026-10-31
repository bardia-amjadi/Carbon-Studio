using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CarbonStudio.App.Services;

/// <summary>File and folder pickers initialised with the owning window handle (required in desktop WinUI 3 apps).</summary>
internal sealed class PickerService
{
    private readonly nint _windowHandle;

    public PickerService(nint windowHandle) => _windowHandle = windowHandle;

    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _windowHandle);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public async Task<string?> PickFileAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickSavePathAsync(string suggestedName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName),
        };

        var extension = Path.GetExtension(suggestedName);
        picker.FileTypeChoices.Add(
            string.IsNullOrEmpty(extension) ? "Text file" : $"{extension.TrimStart('.').ToUpperInvariant()} file",
            [string.IsNullOrEmpty(extension) ? ".txt" : extension]);
        picker.FileTypeChoices.Add("All files", ["."]);

        InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
