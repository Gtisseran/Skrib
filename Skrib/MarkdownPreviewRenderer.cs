using System;
using Markdig;

namespace Skrib
{
    /// <summary>
    /// Converts Markdown source into the HTML fragment shown in the WebView2 preview.
    /// Plain C# on purpose: the unit test project compiles this file directly, so it must
    /// not depend on WinUI types (same contract as LanguageHelper / SettingsDefaults).
    /// </summary>
    public static class MarkdownPreviewRenderer
    {
        // - AdvancedExtensions: tables, task lists, strikethrough, autolinks…
        // - DisableHtml: raw HTML in a document is escaped instead of executed, so the
        //   preview can never run scripts embedded in a .md file.
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .DisableHtml()
            .Build();

        /// <summary>
        /// Renders the Markdown document to an HTML fragment (no &lt;html&gt;/&lt;body&gt; wrapper).
        /// The fragment is injected into the preview shell by a web message.
        /// </summary>
        public static string ToHtmlFragment(string? markdown)
        {
            if (string.IsNullOrEmpty(markdown))
            {
                return string.Empty;
            }
            return Markdig.Markdown.ToHtml(markdown, Pipeline);
        }

        /// <summary>
        /// The document loaded once into the preview WebView. It holds the styling, the
        /// dark/light theme switch and the message listener that replaces the rendered
        /// content. Colors are CSS variables so a theme switch only needs one web message.
        /// </summary>
        public static string BuildShellHtml()
        {
            return """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
  :root {
    --fg: rgba(0, 0, 0, 0.92);
    --fg-secondary: rgba(0, 0, 0, 0.60);
    --subtle-bg: rgba(0, 0, 0, 0.06);
    --border: rgba(0, 0, 0, 0.10);
    --accent: #0067C0;
  }
  html.dark {
    --fg: rgba(255, 255, 255, 0.92);
    --fg-secondary: rgba(255, 255, 255, 0.60);
    --subtle-bg: rgba(255, 255, 255, 0.06);
    --border: rgba(255, 255, 255, 0.10);
    --accent: #4CC2FF;
  }
  /* The window backdrop (host Border) shows through: never paint a body background. */
  html, body { background: transparent; }
  body {
    margin: 0;
    /* Matches the 16px padding the editor area keeps on every side. */
    padding: 16px;
    font-family: "Segoe UI", "Segoe UI Variable Text", sans-serif;
    font-size: 15px;
    line-height: 1.6;
    color: var(--fg);
    /* Full browser-grade selection: Ctrl+A spans the whole document and mouse
       selection crosses paragraphs, unlike the per-TextBlock selection it replaces. */
    -webkit-user-select: text;
    user-select: text;
    cursor: default;
    overflow-wrap: break-word;
  }
  h1, h2, h3, h4, h5, h6 { font-weight: 600; line-height: 1.3; }
  h1 { font-size: 26px; margin: 24px 0 10px; }
  h2 { font-size: 20px; margin: 20px 0 8px; }
  h3 { font-size: 17px; margin: 16px 0 6px; }
  h4 { font-size: 15px; margin: 12px 0 5px; }
  h5 { font-size: 13px; margin: 10px 0 4px; }
  h6 { font-size: 13px; margin: 10px 0 4px; color: var(--fg-secondary); }
  h1:first-child, h2:first-child, h3:first-child, h4:first-child { margin-top: 0; }
  p { margin: 0 0 12px; }
  ul, ol { margin: 0 0 12px; padding-left: 28px; }
  li { margin: 2px 0; }
  li > p { margin: 0; }
  li.task-list-item { list-style: none; margin-left: -20px; }
  li.task-list-item input { margin-right: 8px; }
  blockquote {
    margin: 0 0 12px;
    padding: 6px 10px;
    border-left: 2px solid var(--accent);
    background: var(--subtle-bg);
    color: var(--fg-secondary);
  }
  blockquote p:last-child { margin-bottom: 0; }
  code {
    font-family: Consolas, "Cascadia Mono", monospace;
    font-size: 13px;
    background: var(--subtle-bg);
    border-radius: 3px;
    padding: 1px 4px;
  }
  pre {
    margin: 0 0 12px;
    padding: 12px;
    background: var(--subtle-bg);
    border: 1px solid var(--border);
    border-radius: 6px;
    /* Wrap long lines instead of scrolling, like WrapCodeBlock did. */
    white-space: pre-wrap;
    overflow-wrap: break-word;
  }
  pre code {
    background: transparent;
    padding: 0;
    font-size: 13px;
    line-height: 1.5;
  }
  table {
    border-collapse: collapse;
    margin: 0 0 12px;
    max-width: 100%;
  }
  th, td {
    border: 1px solid var(--border);
    padding: 6px 8px;
    text-align: left;
  }
  th { font-weight: 600; background: var(--subtle-bg); }
  hr {
    border: 0;
    border-top: 1px solid var(--border);
    margin: 20px 0;
  }
  a { color: var(--accent); text-decoration: none; }
  a:hover { text-decoration: underline; }
  img { max-width: 760px; height: auto; }
</style>
</head>
<body>
<div id="content"></div>
<script>
  window.chrome.webview.addEventListener('message', function (e) {
    var d = e.data;
    if (!d || d.type !== 'render') return;
    document.documentElement.classList.toggle('dark', !!d.dark);
    document.documentElement.style.setProperty('--accent', d.accent || '');
    document.getElementById('content').innerHTML = d.html || '';
  });
</script>
</body>
</html>
""";
        }
    }
}
