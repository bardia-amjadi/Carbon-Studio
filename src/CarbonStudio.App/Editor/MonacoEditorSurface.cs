using CarbonStudio.Core.Documents;
using CarbonStudio.Core.Editor;
using CarbonStudio.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.UI;

namespace CarbonStudio.App.Editor;

/// <summary>
/// Monaco editor hosted in a WebView2 control.
///
/// Security boundary: the page is served only from a virtual host that maps to the bundled Assets\editor folder; every other
/// navigation, new window and download is cancelled; host objects and dev tools are off; permissions are denied except
/// clipboard access for our own origin; and every message from the page is validated by <see cref="EditorProtocol"/>.
/// </summary>
internal sealed class MonacoEditorSurface : IEditorSurface
{
    private const string HostName = "appassets.carbonstudio.test";
    private static readonly string Origin = $"https://{HostName}/";
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(3);

    private readonly WebView2 _web = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly string _editorFolder;
    private readonly string _userDataFolder;
    private readonly ShortcutMap _shortcuts;
    private readonly ILogger _logger;
    private readonly Queue<string> _pending = new();
    private readonly Dictionary<Guid, int> _lastRevision = [];
    private readonly Dictionary<int, TaskCompletionSource> _flushWaiters = [];
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _isReady;
    private int _nextRequestId;

    public MonacoEditorSurface(string editorFolder, string userDataFolder, ShortcutMap shortcuts, ILogger logger)
    {
        _editorFolder = editorFolder;
        _userDataFolder = userDataFolder;
        _shortcuts = shortcuts;
        _logger = logger;
    }

    public string Name => "Monaco";

    public FrameworkElement View => _web;

    public event EventHandler<EditorTextEventArgs>? TextChanged;

    public event EventHandler<EditorCaretEventArgs>? CaretChanged;

    public event EventHandler<string>? ShortcutPressed;

    /// <summary>Raised when the web view's process crashes or exits; the owner decides how to recover.</summary>
    public event EventHandler<string>? Failed;

    /// <summary>The installed WebView2 runtime version, or null when the runtime is not installed.</summary>
    public static string? RuntimeVersion
    {
        get
        {
            try
            {
                return CoreWebView2Environment.GetAvailableBrowserVersionString();
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException)
            {
                return null;
            }
        }
    }

    /// <summary>Starts WebView2, loads the editor page and waits for it to report ready. The control must already be in the visual tree.</summary>
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_userDataFolder);

        // The default user-data folder sits next to the executable, which is read-only under Program Files.
        var environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: _userDataFolder);
        await _web.EnsureCoreWebView2Async(environment);

        var core = _web.CoreWebView2;
        var settings = core.Settings;
        settings.AreHostObjectsAllowed = false;
        settings.AreDefaultContextMenusEnabled = false; // Monaco provides its own context menu
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsBuiltInErrorPageEnabled = false;
#if DEBUG
        settings.AreDevToolsEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
#endif

        core.SetVirtualHostNameToFolderMapping(HostName, _editorFolder, CoreWebView2HostResourceAccessKind.Deny);

        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith(Origin, StringComparison.Ordinal))
            {
                e.Cancel = true;
                _logger.LogWarning("Blocked editor navigation to {Uri}", e.Uri);
            }
        };
        core.FrameNavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith(Origin, StringComparison.Ordinal))
            {
                e.Cancel = true;
            }
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) =>
        {
            var ours = e.Uri.StartsWith(Origin, StringComparison.Ordinal);
            e.State = ours && e.PermissionKind == CoreWebView2PermissionKind.ClipboardRead
                ? CoreWebView2PermissionState.Allow
                : CoreWebView2PermissionState.Deny;
        };
        core.ProcessFailed += (_, e) =>
        {
            _logger.LogError("Editor web process failed: {Kind}", e.ProcessFailedKind);
            Failed?.Invoke(this, e.ProcessFailedKind.ToString());
        };
        core.WebMessageReceived += OnWebMessageReceived;

        core.Navigate(Origin + "index.html");

        var finished = await Task.WhenAny(_ready.Task, Task.Delay(ReadyTimeout));
        if (finished != _ready.Task)
        {
            throw new TimeoutException($"The editor page did not report ready within {ReadyTimeout.TotalSeconds:0} seconds.");
        }
    }

    public bool Supports(EditorAction action) => true;

    public void Open(DocumentSession document) =>
        Send(EditorProtocol.Open(document.Id, document.Text, document.Language.Id));

    public void Close(DocumentSession document)
    {
        _lastRevision.Remove(document.Id);
        Send(EditorProtocol.Close(document.Id));
    }

    public void Activate(DocumentSession? document, bool focus) =>
        Send(document is null ? EditorProtocol.Deactivate() : EditorProtocol.Activate(document.Id, focus));

    public void UpdateLanguage(DocumentSession document) =>
        Send(EditorProtocol.SetLanguage(document.Id, document.Language.Id));

    public void ApplySettings(AppSettings settings, bool dark)
    {
        Send(EditorProtocol.Options(settings.Editor));
        Send(EditorProtocol.Theme(dark, settings.Appearance.AccentColor));
        _web.DefaultBackgroundColor = dark ? Color.FromArgb(255, 0x10, 0x16, 0x1D) : Color.FromArgb(255, 0xFF, 0xFF, 0xFF);
    }

    public async Task<bool> FlushAsync()
    {
        if (!_isReady)
        {
            return true; // nothing has been sent to the page yet, so nothing can be pending in it
        }

        var requestId = Interlocked.Increment(ref _nextRequestId);
        var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _flushWaiters[requestId] = waiter;
        Send(EditorProtocol.Flush(requestId));

        var finished = await Task.WhenAny(waiter.Task, Task.Delay(FlushTimeout));
        _flushWaiters.Remove(requestId);
        if (finished != waiter.Task)
        {
            _logger.LogError("The editor did not answer a flush request within {Seconds}s", FlushTimeout.TotalSeconds);
            return false;
        }

        return true;
    }

    public void Execute(EditorAction action) => Send(EditorProtocol.Exec(action));

    public void Focus()
    {
        _web.Focus(FocusState.Programmatic);
        Send(EditorProtocol.Focus());
    }

    private void Send(string json)
    {
        if (!_isReady)
        {
            _pending.Enqueue(json);
            return;
        }

        _web.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        // Only our own page may talk to us.
        if (!args.Source.StartsWith(Origin, StringComparison.Ordinal))
        {
            _logger.LogWarning("Ignored a message from unexpected source {Source}", args.Source);
            return;
        }

        if (!EditorProtocol.TryParse(args.WebMessageAsJson, out var message, out var error))
        {
            _logger.LogWarning("Ignored an invalid editor message: {Error}", error);
            return;
        }

        switch (message)
        {
            case EditorReady:
                _isReady = true;
                _web.CoreWebView2.PostWebMessageAsJson(EditorProtocol.Shortcuts(_shortcuts.Chords));
                while (_pending.Count > 0)
                {
                    _web.CoreWebView2.PostWebMessageAsJson(_pending.Dequeue());
                }

                _ready.TrySetResult();
                break;
            case EditorTextChanged text:
                if (!_lastRevision.TryGetValue(text.Id, out var last) || text.Rev > last)
                {
                    _lastRevision[text.Id] = text.Rev;
                    TextChanged?.Invoke(this, new EditorTextEventArgs(text.Id, text.Text));
                }

                break;
            case EditorCaretMoved caret:
                CaretChanged?.Invoke(this, new EditorCaretEventArgs(caret.Id, caret.Line, caret.Column));
                break;
            case EditorShortcut shortcut:
                ShortcutPressed?.Invoke(this, shortcut.Chord);
                break;
            case EditorFlushed flushed:
                if (_flushWaiters.TryGetValue(flushed.RequestId, out var waiter))
                {
                    waiter.TrySetResult();
                }

                break;
            case EditorError failure:
                _logger.LogWarning("Editor reported an error: {Message}", failure.Message);
                break;
        }
    }
}
