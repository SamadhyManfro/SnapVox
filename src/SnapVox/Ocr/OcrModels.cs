using Avalonia;

namespace SnapVox.Ocr;

/// <summary>A recognized word and its box, in pixels of the selection image.</summary>
public sealed record OcrWord(string Text, Rect Box);

public sealed record OcrLine(IReadOnlyList<OcrWord> Words)
{
    public string Text => string.Join(' ', Words.Select(w => w.Text));
}

/// <summary>Final result: lines/words with positions, the full text and the detected language.</summary>
public sealed record OcrOutput(IReadOnlyList<OcrLine> Lines, string? LanguageCode, string? OcrLanguageTag)
{
    public IEnumerable<OcrWord> Words => Lines.SelectMany(l => l.Words);
    public int WordCount => Lines.Sum(l => l.Words.Count);
    public string Text => string.Join(Environment.NewLine, Lines.Select(l => l.Text));
    public bool IsEmpty => WordCount == 0;
}
