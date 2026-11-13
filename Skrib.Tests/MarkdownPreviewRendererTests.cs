using Skrib;

namespace Skrib.Tests;

/// <summary>
/// Covers the HTML conversion behind the Markdown preview WebView: standard Markdown,
/// the advanced extensions (tables, task lists, strikethrough) and the security rule
/// that raw HTML embedded in a document must never reach the preview.
/// </summary>
public class MarkdownPreviewRendererTests
{
    [Fact]
    public void EmptySource_RendersEmptyFragment()
    {
        Assert.Equal(string.Empty, MarkdownPreviewRenderer.ToHtmlFragment(string.Empty));
        Assert.Equal(string.Empty, MarkdownPreviewRenderer.ToHtmlFragment(null));
    }

    [Fact]
    public void Headings_AndParagraphs_AreRendered()
    {
        var html = MarkdownPreviewRenderer.ToHtmlFragment("# Title\n\nHello world.");
        Assert.Contains("<h1", html);
        Assert.Contains("Title", html);
        Assert.Contains("<p>Hello world.</p>", html);
    }

    [Fact]
    public void Lists_AndNestedBlocks_AreRendered()
    {
        var html = MarkdownPreviewRenderer.ToHtmlFragment("- one\n- two\n");
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>one</li>", html);
        Assert.Contains("<li>two</li>", html);
    }

    [Fact]
    public void PipeTables_AreRendered()
    {
        var html = MarkdownPreviewRenderer.ToHtmlFragment("| A | B |\n|---|---|\n| 1 | 2 |");
        Assert.Contains("<table>", html);
        Assert.Contains("<td>1</td>", html);
    }

    [Fact]
    public void TaskLists_AreRendered()
    {
        var html = MarkdownPreviewRenderer.ToHtmlFragment("- [x] done\n- [ ] todo\n");
        Assert.Contains("task-list-item", html);
        Assert.Contains("checkbox", html);
    }

    [Fact]
    public void InlineFormatting_IsRendered()
    {
        var html = MarkdownPreviewRenderer.ToHtmlFragment("a **bold** and `code` and ~~struck~~");
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<code>code</code>", html);
        Assert.Contains("<del>struck</del>", html);
    }

    [Fact]
    public void RawHtml_IsEscaped_NotExecuted()
    {
        var html = MarkdownPreviewRenderer.ToHtmlFragment("<script>alert(1)</script>\n\n<div onclick=\"x()\">t</div>");
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<div", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Links_AreRenderedAsAnchors()
    {
        var html = MarkdownPreviewRenderer.ToHtmlFragment("[GitHub](https://github.com)");
        Assert.Contains("<a href=\"https://github.com\"", html);
        Assert.Contains("GitHub", html);
    }

    [Fact]
    public void ShellHtml_ContainsMessageListener_AndNoBackground()
    {
        var shell = MarkdownPreviewRenderer.BuildShellHtml();
        // The message listener is what receives each rendered document.
        Assert.Contains("addEventListener('message'", shell);
        Assert.Contains("getElementById('content')", shell);
        // The window backdrop must show through the preview.
        Assert.Contains("background: transparent", shell);
        // Selection must stay enabled over the whole document.
        Assert.Contains("user-select: text", shell);
    }

    [Fact]
    public void ShellHtml_DefinesDarkAndLightVariables()
    {
        var shell = MarkdownPreviewRenderer.BuildShellHtml();
        Assert.Contains("html.dark", shell);
        Assert.Contains("--accent", shell);
    }
}
