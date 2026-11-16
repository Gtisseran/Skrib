using System;
using System.Globalization;
using System.Text;

namespace Skrib;

/// <summary>Encodings the editor can open and write back unchanged.</summary>
public enum TextFileEncoding { Utf8NoBom, Utf8Bom, Utf16Le, Utf16Be, Ansi }

/// <summary>
/// Byte-level text file codec: detects the encoding at open time (BOM, then strict
/// UTF-8 validation, then the legacy ANSI code page) and re-encodes with the same one
/// at save time, so an ANSI or UTF-16 file is no longer silently rewritten as UTF-8.
/// The BOMs are written explicitly: the GetBytes preamble emission of shared Encoding
/// instances is only guaranteed on their first call.
/// Pure C# on purpose: the unit test project compiles this file directly (same
/// contract as SearchHelper / RecentFilesList).
/// </summary>
public static class TextFileCodec
{
    // Strict decode: any invalid byte proves the file is not UTF-8 (legacy ANSI).
    private static readonly UTF8Encoding Utf8Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UTF8Encoding Utf8Body = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UnicodeEncoding Utf16LeBody = new(bigEndian: false, byteOrderMark: false);
    private static readonly UnicodeEncoding Utf16BeBody = new(bigEndian: true, byteOrderMark: false);
    private static readonly byte[] Utf8BomBytes = { 0xEF, 0xBB, 0xBF };
    private static readonly byte[] Utf16LeBomBytes = { 0xFF, 0xFE };
    private static readonly byte[] Utf16BeBomBytes = { 0xFE, 0xFF };
    private static readonly Encoding Ansi = ResolveAnsi();

    public static (string Text, TextFileEncoding Encoding) Decode(byte[] bytes)
    {
        if (StartsWith(bytes, Utf8BomBytes))
        {
            return (Utf8Body.GetString(bytes, Utf8BomBytes.Length, bytes.Length - Utf8BomBytes.Length), TextFileEncoding.Utf8Bom);
        }
        if (StartsWith(bytes, Utf16LeBomBytes))
        {
            return (Utf16LeBody.GetString(bytes, 2, bytes.Length - 2), TextFileEncoding.Utf16Le);
        }
        if (StartsWith(bytes, Utf16BeBomBytes))
        {
            return (Utf16BeBody.GetString(bytes, 2, bytes.Length - 2), TextFileEncoding.Utf16Be);
        }
        if (bytes.Length == 0)
        {
            return (string.Empty, TextFileEncoding.Utf8NoBom);
        }
        try
        {
            return (Utf8Strict.GetString(bytes), TextFileEncoding.Utf8NoBom);
        }
        catch (DecoderFallbackException)
        {
            return (Ansi.GetString(bytes), TextFileEncoding.Ansi);
        }
    }

    public static byte[] Encode(string text, TextFileEncoding encoding)
    {
        return encoding switch
        {
            TextFileEncoding.Utf8Bom => Concat(Utf8BomBytes, Utf8Body.GetBytes(text)),
            TextFileEncoding.Utf16Le => Concat(Utf16LeBomBytes, Utf16LeBody.GetBytes(text)),
            TextFileEncoding.Utf16Be => Concat(Utf16BeBomBytes, Utf16BeBody.GetBytes(text)),
            TextFileEncoding.Ansi => Ansi.GetBytes(text),
            _ => Utf8Body.GetBytes(text),
        };
    }

    private static bool StartsWith(byte[] bytes, byte[] prefix)
    {
        if (bytes.Length < prefix.Length) return false;
        for (int i = 0; i < prefix.Length; i++)
        {
            if (bytes[i] != prefix[i]) return false;
        }
        return true;
    }

    private static byte[] Concat(byte[] head, byte[] body)
    {
        if (head.Length == 0) return body;
        var result = new byte[head.Length + body.Length];
        Buffer.BlockCopy(head, 0, result, 0, head.Length);
        Buffer.BlockCopy(body, 0, result, head.Length, body.Length);
        return result;
    }

    // The ANSI fallback is the legacy system code page (GetACP); the code pages
    // provider must be registered before .NET can resolve it.
    private static Encoding ResolveAnsi()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(0);
        }
        catch (Exception)
        {
        }
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
        catch (Exception)
        {
            // A machine without the legacy code pages must never fail to open or save.
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
    }
}
