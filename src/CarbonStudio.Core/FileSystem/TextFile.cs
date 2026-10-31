using System.Text;

namespace CarbonStudio.Core.FileSystem;

public enum LineEnding
{
    Lf,
    CrLf,
}

public enum TextEncodingKind
{
    Utf8,
    Utf16Le,
    Utf16Be,
    Utf32Le,
    Utf32Be,
    Latin1,
}

public sealed record TextFileFormat(TextEncodingKind Encoding, bool HasBom, LineEnding LineEnding)
{
    public static TextFileFormat Default { get; } =
        new(TextEncodingKind.Utf8, false, OperatingSystem.IsWindows() ? LineEnding.CrLf : LineEnding.Lf);

    public string EncodingDisplayName => Encoding switch
    {
        TextEncodingKind.Utf8 => HasBom ? "UTF-8 with BOM" : "UTF-8",
        TextEncodingKind.Utf16Le => "UTF-16 LE",
        TextEncodingKind.Utf16Be => "UTF-16 BE",
        TextEncodingKind.Utf32Le => "UTF-32 LE",
        TextEncodingKind.Utf32Be => "UTF-32 BE",
        _ => "Latin-1",
    };

    public string LineEndingDisplayName => LineEnding == LineEnding.CrLf ? "CRLF" : "LF";
}

/// <param name="Text">Content with every line break normalised to '\n'.</param>
public sealed record TextFileContent(string Text, TextFileFormat Format, bool HadMixedLineEndings);

public sealed class BinaryFileException(string path) : IOException($"'{Path.GetFileName(path)}' looks like a binary file and cannot be opened as text.");

public sealed class FileTooLargeException(string path, long size, long limit)
    : IOException($"'{Path.GetFileName(path)}' is {size / (1024 * 1024.0):0.#} MB, which exceeds the {limit / (1024 * 1024)} MB limit for the text editor.");

public sealed class UnrepresentableTextException(TextFileFormat format)
    : IOException($"The text contains characters that cannot be stored as {format.EncodingDisplayName}.");

/// <summary>Decoding and encoding of text files with BOM detection and line-ending preservation.</summary>
public static class TextFile
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async Task<TextFileContent> ReadAsync(string path, long maxBytes, CancellationToken cancellationToken = default)
    {
        var length = new FileInfo(path).Length;
        if (length > maxBytes)
        {
            throw new FileTooLargeException(path, length, maxBytes);
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        try
        {
            return Decode(bytes);
        }
        catch (BinaryFileException)
        {
            throw new BinaryFileException(path);
        }
    }

    public static TextFileContent Decode(ReadOnlySpan<byte> bytes)
    {
        var (kind, bomLength) = DetectEncoding(bytes);
        var body = bytes[bomLength..];

        string text;
        switch (kind)
        {
            case TextEncodingKind.Utf8:
                if (bomLength == 0 && body.Contains((byte)0))
                {
                    throw new BinaryFileException("file");
                }

                try
                {
                    text = StrictUtf8.GetString(body);
                }
                catch (DecoderFallbackException)
                {
                    kind = TextEncodingKind.Latin1;
                    text = Encoding.Latin1.GetString(body);
                }

                break;
            case TextEncodingKind.Utf16Le:
                text = Encoding.Unicode.GetString(body);
                break;
            case TextEncodingKind.Utf16Be:
                text = Encoding.BigEndianUnicode.GetString(body);
                break;
            case TextEncodingKind.Utf32Le:
                text = new UTF32Encoding(false, false).GetString(body);
                break;
            default:
                text = new UTF32Encoding(true, false).GetString(body);
                break;
        }

        // Latin-1 can decode anything, so reject NUL-laden data that is really binary.
        if (kind == TextEncodingKind.Latin1 && text.Contains('\0'))
        {
            throw new BinaryFileException("file");
        }

        var (normalized, ending, mixed) = NormalizeLineEndings(text);
        return new TextFileContent(normalized, new TextFileFormat(kind, bomLength > 0, ending), mixed);
    }

    /// <summary>False when saving <paramref name="text"/> in <paramref name="format"/> would lose characters.</summary>
    public static bool CanRepresent(string text, TextFileFormat format) =>
        format.Encoding != TextEncodingKind.Latin1 || !text.Any(c => c > 0xFF);

    public static byte[] Encode(string text, TextFileFormat format)
    {
        var withEndings = format.LineEnding == LineEnding.CrLf ? text.Replace("\n", "\r\n", StringComparison.Ordinal) : text;

        if (format.Encoding == TextEncodingKind.Latin1 && withEndings.Any(c => c > 0xFF))
        {
            throw new UnrepresentableTextException(format);
        }

        Encoding encoding = format.Encoding switch
        {
            TextEncodingKind.Utf8 => new UTF8Encoding(false),
            TextEncodingKind.Utf16Le => new UnicodeEncoding(false, false),
            TextEncodingKind.Utf16Be => new UnicodeEncoding(true, false),
            TextEncodingKind.Utf32Le => new UTF32Encoding(false, false),
            TextEncodingKind.Utf32Be => new UTF32Encoding(true, false),
            _ => Encoding.Latin1,
        };

        var body = encoding.GetBytes(withEndings);
        var bom = format.HasBom ? BomFor(format.Encoding) : [];
        return [.. bom, .. body];
    }

    public static async Task WriteAsync(string path, string text, TextFileFormat format, CancellationToken cancellationToken = default)
    {
        var bytes = Encode(text, format);
        await Paths.AtomicFile.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }

    private static (TextEncodingKind Kind, int BomLength) DetectEncoding(ReadOnlySpan<byte> b)
    {
        // UTF-32 LE must be checked before UTF-16 LE: their byte-order marks share a prefix.
        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0 && b[3] == 0)
        {
            return (TextEncodingKind.Utf32Le, 4);
        }

        if (b.Length >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 0xFE && b[3] == 0xFF)
        {
            return (TextEncodingKind.Utf32Be, 4);
        }

        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
        {
            return (TextEncodingKind.Utf8, 3);
        }

        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
        {
            return (TextEncodingKind.Utf16Le, 2);
        }

        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
        {
            return (TextEncodingKind.Utf16Be, 2);
        }

        return (TextEncodingKind.Utf8, 0);
    }

    private static byte[] BomFor(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf8 => [0xEF, 0xBB, 0xBF],
        TextEncodingKind.Utf16Le => [0xFF, 0xFE],
        TextEncodingKind.Utf16Be => [0xFE, 0xFF],
        TextEncodingKind.Utf32Le => [0xFF, 0xFE, 0, 0],
        TextEncodingKind.Utf32Be => [0, 0, 0xFE, 0xFF],
        _ => [],
    };

    private static (string Text, LineEnding Ending, bool Mixed) NormalizeLineEndings(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        var kinds = (crlf > 0 ? 1 : 0) + ((lf + cr) > 0 ? 1 : 0);
        var ending = crlf + lf + cr == 0
            ? TextFileFormat.Default.LineEnding
            : crlf > lf + cr ? LineEnding.CrLf : LineEnding.Lf;

        if (crlf + cr == 0)
        {
            return (text, ending, false);
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return (normalized, ending, kinds > 1 || (cr > 0 && (crlf + lf) > 0));
    }
}
