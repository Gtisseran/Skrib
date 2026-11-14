using Skrib;

namespace Skrib.Tests;

/// <summary>
/// Covers the Markdown parsing layer behind the native preview: the flattening of inline
/// styles, headings, lists, code blocks, links, and the guarantee that raw HTML from a
/// document is never carried over into the model.
/// </summary>
public class MarkdownPreviewModelTests
{
    private static List<MarkdownRun> ParagraphRuns(object block)
    {
        var paragraph = Assert.IsType<MarkdownBlock>(block);
        Assert.Equal(MarkdownBlockKind.Paragraph, paragraph.Kind);
        return new List<MarkdownRun>(paragraph.Runs);
    }

    private static string Joined(IEnumerable<MarkdownRun> runs) => string.Concat(runs.Select(r => r.Text));

    [Fact]
    public void EmptySource_YieldsNoBlocks()
    {
        Assert.Empty(MarkdownPreviewModel.Parse(string.Empty));
        Assert.Empty(MarkdownPreviewModel.Parse(null));
    }

    [Fact]
    public void Headings_CarryTheirLevel()
    {
        var blocks = MarkdownPreviewModel.Parse("# Title\n\nBody text.\n");
        Assert.Equal(2, blocks.Count);
        var heading = Assert.IsType<MarkdownBlock>(blocks[0]);
        Assert.Equal(MarkdownBlockKind.Heading, heading.Kind);
        Assert.Equal(1, heading.Level);
        Assert.Equal("Title", Joined(heading.Runs));
    }

    [Fact]
    public void Paragraph_FlattensBoldItalicAndCode()
    {
        var runs = ParagraphRuns(MarkdownPreviewModel.Parse("a **bold** and *italic* and `code`")[0]);
        Assert.Equal("a bold and italic and code", Joined(runs));
        Assert.True(runs[1].Bold);
        Assert.True(runs[3].Italic);
        Assert.True(runs[5].Code);
        Assert.False(runs[0].Bold);
    }

    [Fact]
    public void Strikethrough_IsFlagged()
    {
        var runs = ParagraphRuns(MarkdownPreviewModel.Parse("~~gone~~")[0]);
        Assert.True(runs[0].Strikethrough);
        Assert.Equal("gone", runs[0].Text);
    }

    [Fact]
    public void Links_KeepTheirUrl()
    {
        var runs = ParagraphRuns(MarkdownPreviewModel.Parse("[GitHub](https://github.com)")[0]);
        var run = Assert.Single(runs);
        Assert.Equal("GitHub", run.Text);
        Assert.Equal("https://github.com", run.LinkUrl);
    }

    [Fact]
    public void Autolinks_AreLinked()
    {
        var runs = ParagraphRuns(MarkdownPreviewModel.Parse("See <https://example.com>")[0]);
        Assert.Equal(2, runs.Count);
        Assert.Equal("See ", runs[0].Text);
        Assert.Null(runs[0].LinkUrl);
        Assert.Equal("https://example.com", runs[1].Text);
        Assert.Equal("https://example.com", runs[1].LinkUrl);
    }

    [Fact]
    public void CodeBlocks_KeepTheirText()
    {
        var blocks = MarkdownPreviewModel.Parse("```bash\ngit status\n```");
        var code = Assert.IsType<MarkdownBlock>(Assert.Single(blocks));
        Assert.Equal(MarkdownBlockKind.CodeBlock, code.Kind);
        Assert.Equal("git status", code.Text);
    }

    [Fact]
    public void BulletLists_BecomeIndentedItems()
    {
        var blocks = MarkdownPreviewModel.Parse("- one\n- two\n");
        var list = Assert.IsType<MarkdownBlock>(Assert.Single(blocks));
        Assert.Equal(MarkdownBlockKind.List, list.Kind);
        var model = Assert.IsType<MarkdownList>(list.List);
        Assert.False(model.Ordered);
        Assert.Equal(2, model.Items.Count);
        Assert.IsType<MarkdownBlock>(Assert.Single(model.Items[0].Blocks));
    }

    [Fact]
    public void OrderedLists_StartAtOne()
    {
        var blocks = MarkdownPreviewModel.Parse("1. first\n2. second\n");
        var list = Assert.IsType<MarkdownBlock>(Assert.Single(blocks)).List;
        Assert.NotNull(list);
        Assert.True(list!.Ordered);
        Assert.Equal(1, list.StartIndex);
        Assert.Equal(2, list.Items.Count);
    }

    [Fact]
    public void Quotes_NestTheirBlocks()
    {
        var blocks = MarkdownPreviewModel.Parse("> quoted text\n");
        var quote = Assert.IsType<MarkdownBlock>(Assert.Single(blocks));
        Assert.Equal(MarkdownBlockKind.Quote, quote.Kind);
        Assert.IsType<MarkdownBlock>(Assert.Single(quote.Blocks));
    }

    [Fact]
    public void Rules_AreRecognized()
    {
        var blocks = MarkdownPreviewModel.Parse("before\n\n---\n\nafter\n");
        Assert.Equal(3, blocks.Count);
        Assert.Equal(MarkdownBlockKind.Rule, ((MarkdownBlock)blocks[1]).Kind);
    }

    [Fact]
    public void RawHtml_TagsAreDropped_TextBetweenIsInert()
    {
        var runs = ParagraphRuns(MarkdownPreviewModel.Parse("safe <script>alert(1)</script> text")[0]);
        // Tags never reach the model; the text between them is kept as inert literal text.
        Assert.Equal("safe alert(1) text", Joined(runs));
        Assert.DoesNotContain(runs, r => r.Text.Contains('<'));
    }

    [Fact]
    public void PipeTables_BecomeRows()
    {
        var blocks = MarkdownPreviewModel.Parse("| A | B |\n|---|---|\n| 1 | 2 |");
        var table = Assert.IsType<MarkdownBlock>(Assert.Single(blocks));
        Assert.Equal(MarkdownBlockKind.Table, table.Kind);
        Assert.Equal(2, table.Rows.Count);
        Assert.True(table.Rows[0].IsHeader);
        Assert.False(table.Rows[1].IsHeader);
        Assert.Equal("1", table.Rows[1].Cells[0][0].Text);
    }

    [Fact]
    public void Images_FallBackToTheirAltText()
    {
        var runs = ParagraphRuns(MarkdownPreviewModel.Parse("![logo](img.png)")[0]);
        Assert.Equal("logo", runs[0].Text);
        Assert.Null(runs[0].LinkUrl);
    }
}
