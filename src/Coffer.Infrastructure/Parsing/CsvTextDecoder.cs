using System.Text;

namespace Coffer.Infrastructure.Parsing;

/// <summary>
/// Decodes a CSV statement export whose encoding is not declared. Shared by
/// <see cref="FingerprintBankDetector"/>, the PKO "Historia rachunku" parser and the
/// AI fallback parser so all three read the same file the same way.
/// </summary>
/// <remarks>
/// Rules, in order:
/// <list type="number">
/// <item>A UTF-8 byte-order mark → UTF-8 (the BOM is not part of the text).</item>
/// <item>The whole file decodes as strict UTF-8 (no invalid sequences) → UTF-8.</item>
/// <item>Otherwise → Windows-1250, the encoding PKO BP exports by default.</item>
/// </list>
/// Pure ASCII satisfies rule 2, and ASCII is identical in UTF-8 and Windows-1250, so
/// those files decode the same as before. A Windows-1250 file with Polish diacritics
/// is practically never valid UTF-8: its letters (e.g. <c>0xB9</c> "ą", <c>0xB3</c> "ł",
/// <c>0x9C</c> "ś") are lone continuation bytes, which strict UTF-8 rejects.
/// </remarks>
public static class CsvTextDecoder
{
    private static readonly Encoding _strictUtf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly Encoding _windows1250;

    static CsvTextDecoder()
    {
        // Windows-1250 is not a built-in code page on modern .NET; registering the
        // provider is idempotent. Register before resolving the encoding.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _windows1250 = Encoding.GetEncoding(1250);
    }

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>The Windows-1250 encoding used when the content is not UTF-8.</summary>
    public static Encoding Windows1250 => _windows1250;

    /// <summary>
    /// Picks the encoding for <paramref name="content"/> per the rules in the type remarks.
    /// Returns <see cref="Encoding.UTF8"/> for both UTF-8 cases.
    /// </summary>
    public static Encoding DetectEncoding(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(Utf8Bom))
        {
            return Encoding.UTF8;
        }

        try
        {
            _strictUtf8.GetCharCount(content);
            return Encoding.UTF8;
        }
        catch (DecoderFallbackException)
        {
            return _windows1250;
        }
    }

    /// <summary>Decodes <paramref name="content"/> to text, dropping a leading UTF-8 BOM.</summary>
    public static string Decode(ReadOnlySpan<byte> content)
    {
        var encoding = DetectEncoding(content);
        if (content.StartsWith(Utf8Bom))
        {
            content = content[Utf8Bom.Length..];
        }

        return encoding.GetString(content);
    }

    /// <summary>
    /// Reads the whole <paramref name="content"/> stream from position 0 and decodes it.
    /// The stream is left open and rewound to position 0.
    /// </summary>
    public static string ReadAllText(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        content.Position = 0;
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        content.Position = 0;

        return Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }
}
