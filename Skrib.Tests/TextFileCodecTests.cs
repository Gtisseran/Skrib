using System.Text;
using Skrib;

namespace Skrib.Tests;

public class TextFileCodecTests
{
    [Fact]
    public void Decode_Utf8Bom_IsDetectedAndStripped()
    {
        var (text, encoding) = TextFileCodec.Decode(new byte[] { 0xEF, 0xBB, 0xBF, 0x61, 0x62 });
        Assert.Equal("ab", text);
        Assert.Equal(TextFileEncoding.Utf8Bom, encoding);
    }

    [Fact]
    public void Decode_Utf16LeBom_IsDetected()
    {
        var (text, encoding) = TextFileCodec.Decode(new byte[] { 0xFF, 0xFE, 0x61, 0x00 });
        Assert.Equal("a", text);
        Assert.Equal(TextFileEncoding.Utf16Le, encoding);
    }

    [Fact]
    public void Decode_Utf16BeBom_IsDetected()
    {
        var (text, encoding) = TextFileCodec.Decode(new byte[] { 0xFE, 0xFF, 0x00, 0x61 });
        Assert.Equal("a", text);
        Assert.Equal(TextFileEncoding.Utf16Be, encoding);
    }

    [Fact]
    public void Decode_PlainUtf8_IsUtf8WithoutBom()
    {
        const string text = "héllo — 文";
        var (decoded, encoding) = TextFileCodec.Decode(Encoding.UTF8.GetBytes(text));
        Assert.Equal(text, decoded);
        Assert.Equal(TextFileEncoding.Utf8NoBom, encoding);
    }

    [Fact]
    public void Decode_Empty_IsUtf8WithoutBom()
    {
        var (text, encoding) = TextFileCodec.Decode(Array.Empty<byte>());
        Assert.Equal(string.Empty, text);
        Assert.Equal(TextFileEncoding.Utf8NoBom, encoding);
    }

    [Fact]
    public void Decode_InvalidUtf8_FallsBackToAnsi()
    {
        // 0xE9 alone is invalid UTF-8 but valid ANSI; one byte decodes to one char
        // on every legacy code page, so the assertion stays machine-independent.
        var (text, encoding) = TextFileCodec.Decode(new byte[] { 0xE9 });
        Assert.Equal(TextFileEncoding.Ansi, encoding);
        Assert.Single(text);
    }

    [Fact]
    public void Encode_DecodeRoundTrip_PreservesText()
    {
        const string text = "Skrib — naïve 文字 \U0001F389\nline2";
        foreach (var encoding in new[]
                 {
                     TextFileEncoding.Utf8NoBom,
                     TextFileEncoding.Utf8Bom,
                     TextFileEncoding.Utf16Le,
                     TextFileEncoding.Utf16Be,
                 })
        {
            var (decoded, detected) = TextFileCodec.Decode(TextFileCodec.Encode(text, encoding));
            Assert.Equal(text, decoded);
            Assert.Equal(encoding, detected);
        }
    }

    [Fact]
    public void Decode_EncodeRoundTrip_PreservesAnsiBytes()
    {
        // ANSI output depends on the machine code page; the invariant is that the
        // decoded text encodes back to the exact same bytes.
        var bytes = new byte[] { 0xE9, 0x41 };
        var (text, encoding) = TextFileCodec.Decode(bytes);
        Assert.Equal(TextFileEncoding.Ansi, encoding);
        Assert.Equal(bytes, TextFileCodec.Encode(text, encoding));
    }
}
