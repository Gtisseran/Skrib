using System.Diagnostics;
using System.Text;

namespace Skrib.Tests;

/// <summary>
/// Watches the Markdown preview for performance regressions: the render runs synchronously
/// on the UI thread, so an accidental O(n²) in MarkdownPreviewModel would freeze the app
/// on large documents. A generous threshold keeps the test stable on any machine while
/// still catching orders-of-magnitude regressions.
/// </summary>
public class MarkdownPreviewPerformanceTests
{
    /// <summary>A mixed document: every supported block type, repeated. ~10 lines per unit.</summary>
    private static string BuildLargeDocument(int units)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < units; i++)
        {
            sb.AppendLine($"# Heading {i}");
            sb.AppendLine("Some **bold**, *italic*, ~~struck~~ and `inline code` text with a [link](https://example.com).");
            sb.AppendLine();
            sb.AppendLine("- item one\n- item two\n- item three");
            sb.AppendLine();
            sb.AppendLine("| Col A | Col B |");
            sb.AppendLine("|-------|-------|");
            sb.AppendLine("| a | b |");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine("code line");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("> a quote line");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    [Fact]
    public void ParsingALargeDocument_StaysFast()
    {
        // ~180k lines / a few MB: far beyond note-sized, well within what Markdig absorbs.
        var markdown = BuildLargeDocument(20_000);

        var clock = Stopwatch.StartNew();
        var blocks = MarkdownPreviewModel.Parse(markdown);
        clock.Stop();

        Assert.True(blocks.Count > 0, "Le parse n'a produit aucun bloc.");
        Assert.True(clock.ElapsedMilliseconds < 5_000,
            $"Parse trop lent : {clock.ElapsedMilliseconds} ms — une régression de performance s'est probablement glissée dans MarkdownPreviewModel.");
    }

    [Fact]
    public void ParsingANoteSizedDocument_IsInstant()
    {
        var markdown = BuildLargeDocument(20); // ~200 lines: typical note ceiling.

        var clock = Stopwatch.StartNew();
        MarkdownPreviewModel.Parse(markdown);
        clock.Stop();

        Assert.True(clock.ElapsedMilliseconds < 100,
            $"Parse d'une note trop lent : {clock.ElapsedMilliseconds} ms.");
    }
}
