namespace CarbonStudio.Core.Documents;

/// <summary>1-based line and column of a caret.</summary>
public readonly record struct TextPosition(int Line, int Column)
{
    /// <summary>Computes the position of <paramref name="offset"/> in text whose line breaks are '\n'.</summary>
    public static TextPosition FromOffset(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        var clamped = Math.Clamp(offset, 0, text.Length);

        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < clamped; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return new TextPosition(line, clamped - lineStart + 1);
    }

    public override string ToString() => $"Ln {Line}, Col {Column}";
}
