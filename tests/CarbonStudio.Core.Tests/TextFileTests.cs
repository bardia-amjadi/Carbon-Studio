using System.Text;
using CarbonStudio.Core.FileSystem;

namespace CarbonStudio.Core.Tests;

public class TextFileTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Decode_PlainUtf8_NoBom()
    {
        var result = TextFile.Decode("héllo\nworld"u8);
        Assert.Equal("héllo\nworld", result.Text);
        Assert.Equal(TextEncodingKind.Utf8, result.Format.Encoding);
        Assert.False(result.Format.HasBom);
        Assert.Equal(LineEnding.Lf, result.Format.LineEnding);
    }

    [Fact]
    public void Decode_Utf8Bom_IsDetectedAndStripped()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "abc"u8];
        var result = TextFile.Decode(bytes);
        Assert.Equal("abc", result.Text);
        Assert.True(result.Format.HasBom);
    }

    [Fact]
    public void Decode_Utf16LeBom()
    {
        byte[] bytes = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("hi\r\nthere")];
        var result = TextFile.Decode(bytes);
        Assert.Equal("hi\nthere", result.Text);
        Assert.Equal(TextEncodingKind.Utf16Le, result.Format.Encoding);
        Assert.Equal(LineEnding.CrLf, result.Format.LineEnding);
    }

    [Fact]
    public void Decode_Utf32LeBom_IsNotMistakenForUtf16()
    {
        byte[] bytes = [0xFF, 0xFE, 0, 0, .. new UTF32Encoding(false, false).GetBytes("ok")];
        var result = TextFile.Decode(bytes);
        Assert.Equal("ok", result.Text);
        Assert.Equal(TextEncodingKind.Utf32Le, result.Format.Encoding);
    }

    [Fact]
    public void Decode_InvalidUtf8_FallsBackToLatin1()
    {
        byte[] bytes = [0x63, 0x61, 0x66, 0xE9]; // "café" in Latin-1
        var result = TextFile.Decode(bytes);
        Assert.Equal("café", result.Text);
        Assert.Equal(TextEncodingKind.Latin1, result.Format.Encoding);
    }

    [Fact]
    public void Decode_NulBytes_AreRejectedAsBinary()
    {
        Assert.Throws<BinaryFileException>(() => TextFile.Decode([0x4D, 0x5A, 0x00, 0x01, 0x02]));
    }

    [Fact]
    public void Decode_DetectsLineEndingsAndMixedFiles()
    {
        Assert.Equal(LineEnding.CrLf, TextFile.Decode("a\r\nb\r\nc"u8).Format.LineEnding);
        Assert.False(TextFile.Decode("a\r\nb\r\nc"u8).HadMixedLineEndings);

        var mixed = TextFile.Decode("a\r\nb\nc\n"u8);
        Assert.True(mixed.HadMixedLineEndings);
        Assert.Equal(LineEnding.Lf, mixed.Format.LineEnding);
        Assert.Equal("a\nb\nc\n", mixed.Text);
    }

    [Fact]
    public void Decode_LoneCarriageReturns_AreNormalised()
    {
        var result = TextFile.Decode("a\rb"u8);
        Assert.Equal("a\nb", result.Text);
    }

    [Theory]
    [InlineData("line1\r\nline2\r\n")]
    [InlineData("line1\nline2\n")]
    [InlineData("no newline")]
    [InlineData("")]
    public void RoundTrip_PreservesBytes_ForUtf8(string content)
    {
        var original = Encoding.UTF8.GetBytes(content);
        var decoded = TextFile.Decode(original);
        Assert.Equal(original, TextFile.Encode(decoded.Text, decoded.Format));
    }

    [Fact]
    public void RoundTrip_PreservesBom_AndUtf16()
    {
        byte[] bom8 = [0xEF, 0xBB, 0xBF, .. "x\r\ny"u8];
        var d8 = TextFile.Decode(bom8);
        Assert.Equal(bom8, TextFile.Encode(d8.Text, d8.Format));

        byte[] u16 = [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("a\nb")];
        var d16 = TextFile.Decode(u16);
        Assert.Equal(u16, TextFile.Encode(d16.Text, d16.Format));
    }

    [Fact]
    public void Encode_Latin1_RejectsUnrepresentableCharacters()
    {
        var format = new TextFileFormat(TextEncodingKind.Latin1, false, LineEnding.Lf);
        Assert.Throws<UnrepresentableTextException>(() => TextFile.Encode("snowman \u2603", format));
        Assert.Equal([0x63, 0x61, 0x66, 0xE9], TextFile.Encode("café", format));
    }

    [Fact]
    public async Task ReadAsync_EnforcesSizeLimit()
    {
        var path = _dir.WriteFile("big.txt", new string('x', 2048));
        await Assert.ThrowsAsync<FileTooLargeException>(() => TextFile.ReadAsync(path, 1024));
        var ok = await TextFile.ReadAsync(path, 4096);
        Assert.Equal(2048, ok.Text.Length);
    }

    [Fact]
    public async Task ReadAsync_Binary_ThrowsWithFileName()
    {
        var path = _dir.Combine("blob.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 0, 3]);
        var ex = await Assert.ThrowsAsync<BinaryFileException>(() => TextFile.ReadAsync(path, 1024));
        Assert.Contains("blob.bin", ex.Message);
    }

    [Fact]
    public async Task WriteAsync_ReplacesExistingFile_AndLeavesNoTempFiles()
    {
        var path = _dir.WriteFile("f.txt", "old");
        await TextFile.WriteAsync(path, "new\ntext", new TextFileFormat(TextEncodingKind.Utf8, false, LineEnding.CrLf));

        Assert.Equal("new\r\ntext", await File.ReadAllTextAsync(path));
        Assert.Equal(["f.txt"], Directory.GetFileSystemEntries(_dir.Path).Select(Path.GetFileName).ToArray());
    }
}
