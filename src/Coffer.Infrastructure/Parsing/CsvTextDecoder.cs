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
/// <item>The whole file decodes as strict UTF-8 (no invalid sequences) <b>and</b> every
/// non-ASCII character it yields is plausible for a Polish/European statement → UTF-8.</item>
/// <item>Otherwise → Windows-1250, the encoding PKO BP exports by default.</item>
/// </list>
/// Pure ASCII satisfies rule 2, and ASCII is identical in UTF-8 and Windows-1250, so
/// those files decode the same as before.
/// <para>
/// Strict validity alone is not enough. Most Windows-1250 diacritics (e.g. <c>0xB9</c> "ą",
/// <c>0xB3</c> "ł", <c>0x9C</c> "ś") are lone UTF-8 continuation bytes and are rejected,
/// but some upper-case letters are UTF-8 lead bytes (<c>0xD3</c> "Ó", <c>0xC6</c> "Ć",
/// <c>0xCA</c> "Ę", <c>0xD1</c> "Ń") and others are continuation bytes (<c>0xA3</c> "Ł",
/// <c>0xAF</c> "Ż", <c>0x8C</c> "Ś", <c>0xA5</c> "Ą"). So the Windows-1250 bytes of
/// "SPÓŁKA" or "SPÓŁDZIELNIA" are valid UTF-8 and decode to Cyrillic ("SPӣ…"). Such
/// pairs land in Cyrillic, Latin Extended-B/IPA or supplementary planes, so the
/// plausibility check (Latin-1 Supplement, Latin Extended-A, General Punctuation,
/// currency and letterlike symbols) sends them to Windows-1250.
/// </para>
/// <para>
/// Residual ambiguity: a Windows-1250 "Â", "Ă", "Ä" or "Ĺ" directly followed by a
/// continuation-byte letter still forms a plausible Latin character; these letters are
/// rare in Polish text. The trade-off in the other direction: a genuine UTF-8 file whose
/// only non-ASCII text is outside the plausible ranges (e.g. Cyrillic) is decoded as
/// Windows-1250.
/// </para>
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

        string text;
        try
        {
            text = _strictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return _windows1250;
        }

        foreach (var c in text)
        {
            if (c > '' && !IsPlausibleNonAscii(c))
            {
                return _windows1250;
            }
        }

        return Encoding.UTF8;
    }

    /// <summary>
    /// True for a non-ASCII character a statement in Polish (or another European
    /// language) plausibly contains. Surrogates (supplementary planes) are never plausible.
    /// </summary>
    private static bool IsPlausibleNonAscii(char c) => c switch
    {
        >= ' ' and <= 'ſ' => true, // Latin-1 Supplement (incl. NBSP) + Latin Extended-A
        >= ' ' and <= '⁯' => true, // General Punctuation: dashes, „” quotes, …, thin spaces
        >= '₠' and <= '⃏' => true, // Currency Symbols: €
        >= '℀' and <= '⅏' => true, // Letterlike Symbols: №, ™
        _ => false,
    };

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
