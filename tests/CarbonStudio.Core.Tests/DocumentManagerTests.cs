using CarbonStudio.Core.Documents;
using CarbonStudio.Core.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarbonStudio.Core.Tests;

public class DocumentManagerTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly DocumentManager _docs;

    public DocumentManagerTests()
    {
        // SettingsService exposes defaults before LoadAsync, which is all these tests need.
        _docs = new DocumentManager(Factory.Settings(Factory.Paths(_dir)), NullLogger<DocumentManager>.Instance);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Open_LoadsContent_ActivatesAndDeduplicates()
    {
        var path = _dir.WriteFile("a.txt", "hello");

        var first = await _docs.OpenAsync(path);
        var second = await _docs.OpenAsync(path);

        Assert.Same(first, second);
        Assert.Single(_docs.Documents);
        Assert.Same(first, _docs.Active);
        Assert.Equal("hello", first.Text);
        Assert.False(first.IsDirty);
    }

    [Fact]
    public async Task Edit_MarksDirty_AndSaveClearsIt_PreservingFormat()
    {
        var path = _dir.Combine("crlf.txt");
        await File.WriteAllBytesAsync(path, "a\r\nb"u8.ToArray());
        var doc = await _docs.OpenAsync(path);

        _docs.UpdateText(doc, "a\nb\nc");
        Assert.True(doc.IsDirty);
        Assert.True(_docs.HasUnsavedChanges);

        await _docs.SaveAsync(doc);
        Assert.False(doc.IsDirty);
        Assert.Equal("a\r\nb\r\nc", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task UpdateText_WithIdenticalText_DoesNotDirty()
    {
        var doc = await _docs.OpenAsync(_dir.WriteFile("a.txt", "same"));
        var events = 0;
        _docs.DocumentChanged += (_, _) => events++;

        _docs.UpdateText(doc, "same");

        Assert.False(doc.IsDirty);
        Assert.Equal(0, events);
    }

    [Fact]
    public async Task Close_ActivatesNeighbour_AndClearsActiveWhenLast()
    {
        var a = await _docs.OpenAsync(_dir.WriteFile("a.txt", "a"));
        var b = await _docs.OpenAsync(_dir.WriteFile("b.txt", "b"));
        var c = await _docs.OpenAsync(_dir.WriteFile("c.txt", "c"));

        _docs.Active = b;
        _docs.Close(b);
        Assert.Same(c, _docs.Active);

        _docs.Close(c);
        Assert.Same(a, _docs.Active);

        _docs.Close(a);
        Assert.Null(_docs.Active);
        Assert.Empty(_docs.Documents);
    }

    [Fact]
    public async Task Untitled_RequiresSaveAs_ThenBecomesNamedFile()
    {
        var doc = _docs.CreateUntitled();
        Assert.True(doc.IsUntitled);
        Assert.Equal("Untitled-1", doc.DisplayName);
        Assert.Equal("Untitled-2", _docs.CreateUntitled().DisplayName);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _docs.SaveAsync(doc));

        _docs.UpdateText(doc, "line1\nline2");
        var target = _dir.Combine("saved.txt");
        await _docs.SaveAsAsync(doc, target);

        Assert.False(doc.IsUntitled);
        Assert.Equal("saved.txt", doc.DisplayName);
        Assert.False(doc.IsDirty);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task SaveAs_ToPathOpenInAnotherTab_IsRejected()
    {
        var a = await _docs.OpenAsync(_dir.WriteFile("a.txt", "a"));
        await _docs.OpenAsync(_dir.WriteFile("b.txt", "b"));

        await Assert.ThrowsAsync<IOException>(() => _docs.SaveAsAsync(a, _dir.Combine("b.txt")));
    }

    [Fact]
    public async Task SaveAll_SavesDirtyNamedDocuments_AndReportsFailures()
    {
        var a = await _docs.OpenAsync(_dir.WriteFile("a.txt", "a"));
        var b = await _docs.OpenAsync(_dir.WriteFile("b.txt", "b"));
        _docs.UpdateText(a, "A!");
        _docs.UpdateText(b, "B!");

        // Make b unwritable by turning its path into a directory.
        File.Delete(b.Path!);
        Directory.CreateDirectory(b.Path!);

        var failures = await _docs.SaveAllAsync();

        Assert.Single(failures);
        Assert.Same(b, failures[0].Document);
        Assert.False(a.IsDirty);
        Assert.True(b.IsDirty);
        Assert.Equal("A!", await File.ReadAllTextAsync(a.Path!));
    }

    [Fact]
    public async Task NotifyPathRenamed_UpdatesFileAndFolderMoves()
    {
        var file = await _docs.OpenAsync(_dir.WriteFile("src/inner/x.txt", "x"));
        var other = await _docs.OpenAsync(_dir.WriteFile("keep.txt", "k"));

        _docs.NotifyPathRenamed(_dir.Combine("src"), _dir.Combine("source"));

        Assert.Equal(_dir.Combine("source", "inner", "x.txt"), file.Path);
        Assert.Equal("x.txt", file.DisplayName);
        Assert.Equal(_dir.Combine("keep.txt"), other.Path);

        _docs.NotifyPathRenamed(_dir.Combine("keep.txt"), _dir.Combine("kept.txt"));
        Assert.Equal("kept.txt", other.DisplayName);
    }

    [Fact]
    public async Task Open_Binary_Throws_AndOpensNothing()
    {
        var path = _dir.Combine("x.bin");
        await File.WriteAllBytesAsync(path, [0, 1, 2, 3]);

        await Assert.ThrowsAsync<BinaryFileException>(() => _docs.OpenAsync(path));
        Assert.Empty(_docs.Documents);
    }

    [Fact]
    public void Active_CannotBeSetToUnknownDocument()
    {
        var other = new DocumentManager(Factory.Settings(Factory.Paths(_dir)), NullLogger<DocumentManager>.Instance).CreateUntitled();
        Assert.Throws<ArgumentException>(() => _docs.Active = other);
    }
}

public class DocumentFormatTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private (DocumentManager Docs, CarbonStudio.Core.Settings.SettingsService Settings) Create()
    {
        var settings = Factory.Settings(Factory.Paths(_dir));
        return (new DocumentManager(settings, NullLogger<DocumentManager>.Instance), settings);
    }

    [Fact]
    public async Task SetLineEnding_MarksDirty_AndSaveUsesTheNewEnding()
    {
        var (docs, _) = Create();
        var path = _dir.Combine("f.txt");
        await File.WriteAllBytesAsync(path, "a\nb"u8.ToArray());
        var doc = await docs.OpenAsync(path);

        docs.SetLineEnding(doc, LineEnding.CrLf);
        Assert.True(doc.IsDirty);
        Assert.Equal(LineEnding.CrLf, doc.Format.LineEnding);

        await docs.SaveAsync(doc);
        Assert.Equal("a\r\nb", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task SetLineEnding_ToSameValue_IsANoOp()
    {
        var (docs, _) = Create();
        var path = _dir.Combine("f.txt");
        await File.WriteAllBytesAsync(path, "a\nb"u8.ToArray());
        var doc = await docs.OpenAsync(path);
        var events = 0;
        docs.DocumentChanged += (_, _) => events++;

        docs.SetLineEnding(doc, LineEnding.Lf);

        Assert.False(doc.IsDirty);
        Assert.Equal(0, events);
    }

    [Fact]
    public async Task SetLineEnding_ClearsMixedLineEndingWarning()
    {
        var (docs, _) = Create();
        var path = _dir.Combine("mixed.txt");
        await File.WriteAllBytesAsync(path, "a\r\nb\nc"u8.ToArray());
        var doc = await docs.OpenAsync(path);
        Assert.True(doc.HadMixedLineEndings);

        docs.SetLineEnding(doc, LineEnding.Lf);
        Assert.False(doc.HadMixedLineEndings);
    }

    [Fact]
    public async Task SetEncoding_Latin1_RejectsTextThatWouldBeCorrupted()
    {
        var (docs, _) = Create();
        var doc = await docs.OpenAsync(_dir.WriteFile("u.txt", "snowman \u2603"));

        Assert.Throws<UnrepresentableTextException>(() => docs.SetEncoding(doc, TextEncodingKind.Latin1));
        Assert.Equal(TextEncodingKind.Utf8, doc.Format.Encoding);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public async Task SetEncoding_AppliesBomRules_AndRoundTripsOnSave()
    {
        var (docs, _) = Create();
        var path = _dir.WriteFile("e.txt", "héllo");
        var doc = await docs.OpenAsync(path);

        docs.SetEncoding(doc, TextEncodingKind.Utf16Le, withBom: false);
        Assert.True(doc.Format.HasBom, "UTF-16 always carries a BOM");
        Assert.True(doc.IsDirty);
        await docs.SaveAsync(doc);

        var reopened = TextFile.Decode(await File.ReadAllBytesAsync(path));
        Assert.Equal("héllo", reopened.Text);
        Assert.Equal(TextEncodingKind.Utf16Le, reopened.Format.Encoding);

        docs.SetEncoding(doc, TextEncodingKind.Utf8, withBom: true);
        Assert.True(doc.Format.HasBom);
        docs.SetEncoding(doc, TextEncodingKind.Latin1, withBom: true);
        Assert.False(doc.Format.HasBom, "Latin-1 never has a BOM");
    }

    [Fact]
    public async Task FormatChange_RaisesFormatChangedAndDirtyEvents()
    {
        var (docs, _) = Create();
        var doc = await docs.OpenAsync(_dir.WriteFile("f.txt", "x"));
        var kinds = new List<DocumentChangeKind>();
        docs.DocumentChanged += (_, e) => kinds.Add(e.Kind);

        docs.SetEncoding(doc, TextEncodingKind.Utf8, withBom: true);

        Assert.Equal([DocumentChangeKind.FormatChanged, DocumentChangeKind.DirtyChanged], kinds.ToArray());
    }

    [Theory]
    [InlineData(CarbonStudio.Core.Settings.NewFileLineEnding.Lf, LineEnding.Lf)]
    [InlineData(CarbonStudio.Core.Settings.NewFileLineEnding.CrLf, LineEnding.CrLf)]
    public async Task CreateUntitled_UsesTheConfiguredLineEnding(CarbonStudio.Core.Settings.NewFileLineEnding setting, LineEnding expected)
    {
        var (docs, settings) = Create();
        await settings.UpdateUserAsync(s => s with { Editor = s.Editor with { NewFileLineEnding = setting } });

        Assert.Equal(expected, docs.CreateUntitled().Format.LineEnding);
    }

    [Fact]
    public async Task Language_FollowsTheFileName_AndRenames()
    {
        var (docs, _) = Create();
        var doc = await docs.OpenAsync(_dir.WriteFile("a.json", "{}"));
        Assert.Equal("json", doc.Language.Id);

        docs.NotifyPathRenamed(doc.Path!, _dir.Combine("a.cs"));
        Assert.Equal("csharp", doc.Language.Id);
    }

    [Fact]
    public void CanRepresent_OnlyRestrictsLatin1()
    {
        Assert.True(TextFile.CanRepresent("\u2603 😀", new TextFileFormat(TextEncodingKind.Utf8, false, LineEnding.Lf)));
        Assert.True(TextFile.CanRepresent("café", new TextFileFormat(TextEncodingKind.Latin1, false, LineEnding.Lf)));
        Assert.False(TextFile.CanRepresent("\u2603", new TextFileFormat(TextEncodingKind.Latin1, false, LineEnding.Lf)));
    }
}
