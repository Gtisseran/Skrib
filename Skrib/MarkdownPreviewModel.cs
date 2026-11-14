using System;
using System.Collections.Generic;
using System.Text;
using Markdig;

namespace Skrib
{
    // Flattened Markdown content for the native preview. Plain C# on purpose: the unit
    // test project compiles this file directly, so it must not depend on WinUI types
    // (same contract as LanguageHelper / SettingsDefaults).

    /// <summary>A styled run of text (no nesting: styles are flattened while parsing).</summary>
    public sealed record MarkdownRun(
        string Text,
        bool Bold = false,
        bool Italic = false,
        bool Strikethrough = false,
        bool Code = false,
        string? LinkUrl = null);

    /// <summary>One list entry; its blocks may nest further lists.</summary>
    public sealed record MarkdownListItem(IReadOnlyList<object> Blocks);

    /// <summary>A list, ordered (1. 2. …) or bulleted; <see cref="Indent"/> is the nesting depth.</summary>
    public sealed record MarkdownList(
        bool Ordered,
        int StartIndex,
        int Indent,
        IReadOnlyList<MarkdownListItem> Items);

    /// <summary>A table row; each cell holds the runs of one column.</summary>
    public sealed record MarkdownTableRow(bool IsHeader, IReadOnlyList<IReadOnlyList<MarkdownRun>> Cells);

    /// <summary>The supported block kinds of the preview model.</summary>
    public enum MarkdownBlockKind { Heading, Paragraph, CodeBlock, Quote, List, Rule, Table }

    /// <summary>A single block of the preview. Which payload fields are set depends on Kind.</summary>
    public sealed class MarkdownBlock
    {
        public MarkdownBlockKind Kind { get; init; }
        public int Level { get; init; }                                        // Heading: 1..6
        public IReadOnlyList<MarkdownRun> Runs { get; init; } = Array.Empty<MarkdownRun>();    // Heading/Paragraph
        public string Text { get; init; } = string.Empty;                      // CodeBlock
        public IReadOnlyList<object> Blocks { get; init; } = Array.Empty<object>();            // Quote
        public MarkdownList? List { get; init; }                               // List
        public IReadOnlyList<MarkdownTableRow> Rows { get; init; } = Array.Empty<MarkdownTableRow>(); // Table
    }

    /// <summary>
    /// Parses a Markdown document into flat preview blocks with Markdig. Raw HTML is never
    /// interpreted: tags are dropped and every other node becomes literal text runs, so
    /// nothing inside a document can execute in the preview.
    /// </summary>
    public static class MarkdownPreviewModel
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()   // tables, task lists, strikethrough, autolinks…
            .Build();

        public static IReadOnlyList<object> Parse(string? markdown)
        {
            var blocks = new List<object>();
            if (string.IsNullOrEmpty(markdown))
            {
                return blocks;
            }
            var document = Markdig.Markdown.Parse(markdown, Pipeline);
            CollectBlocks(document, blocks, listIndent: 0);
            return blocks;
        }

        private static void CollectBlocks(Markdig.Syntax.ContainerBlock container, List<object> target, int listIndent)
        {
            foreach (var block in container)
            {
                switch (block)
                {
                    case Markdig.Syntax.HeadingBlock heading:
                        target.Add(new MarkdownBlock
                        {
                            Kind = MarkdownBlockKind.Heading,
                            Level = Math.Clamp(heading.Level, 1, 6),
                            Runs = CollectRuns(heading.Inline)
                        });
                        break;

                    case Markdig.Syntax.ParagraphBlock paragraph:
                        target.Add(new MarkdownBlock { Kind = MarkdownBlockKind.Paragraph, Runs = CollectRuns(paragraph.Inline) });
                        break;

                    // Covers fenced and indented code alike (FencedCodeBlock derives from it).
                    case Markdig.Syntax.CodeBlock code:
                        target.Add(new MarkdownBlock { Kind = MarkdownBlockKind.CodeBlock, Text = LinesToText(code.Lines) });
                        break;

                    case Markdig.Syntax.QuoteBlock quote:
                        var quoted = new List<object>();
                        CollectBlocks(quote, quoted, listIndent);
                        target.Add(new MarkdownBlock { Kind = MarkdownBlockKind.Quote, Blocks = quoted });
                        break;

                    case Markdig.Syntax.ListBlock list:
                        var items = new List<MarkdownListItem>();
                        foreach (var item in list)
                        {
                            var itemBlocks = new List<object>();
                            if (item is Markdig.Syntax.ListItemBlock listItem)
                            {
                                CollectBlocks(listItem, itemBlocks, listIndent + 1);
                            }
                            items.Add(new MarkdownListItem(itemBlocks));
                        }
                        int startIndex = 1;
                        if (list.IsOrdered && int.TryParse(list.DefaultOrderedStart, out int parsed))
                        {
                            startIndex = Math.Max(1, parsed);
                        }
                        target.Add(new MarkdownBlock
                        {
                            Kind = MarkdownBlockKind.List,
                            List = new MarkdownList(list.IsOrdered, startIndex, listIndent, items)
                        });
                        break;

                    case Markdig.Syntax.ThematicBreakBlock:
                        target.Add(new MarkdownBlock { Kind = MarkdownBlockKind.Rule });
                        break;

                    case Markdig.Extensions.Tables.Table table:
                        var rows = new List<MarkdownTableRow>();
                        foreach (var row in table)
                        {
                            if (row is not Markdig.Extensions.Tables.TableRow tableRow)
                            {
                                continue;
                            }
                            var cells = new List<IReadOnlyList<MarkdownRun>>();
                            foreach (var cell in tableRow)
                            {
                                if (cell is Markdig.Extensions.Tables.TableCell tableCell)
                                {
                                    cells.Add(CollectCellRuns(tableCell));
                                }
                            }
                            rows.Add(new MarkdownTableRow(tableRow.IsHeader, cells));
                        }
                        target.Add(new MarkdownBlock { Kind = MarkdownBlockKind.Table, Rows = rows });
                        break;

                    case Markdig.Syntax.HtmlBlock:
                        // Raw HTML is skipped entirely: tags are never rendered or interpreted.
                        break;

                    default:
                        break;
                }
            }
        }

        private static IReadOnlyList<MarkdownRun> CollectCellRuns(Markdig.Extensions.Tables.TableCell cell)
        {
            var runs = new List<MarkdownRun>();
            foreach (var inner in cell)
            {
                if (inner is Markdig.Syntax.ParagraphBlock paragraph)
                {
                    runs.AddRange(CollectRuns(paragraph.Inline));
                }
            }
            return runs;
        }

        private static string LinesToText(Markdig.Helpers.StringLineGroup lines)
        {
            var text = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                {
                    text.Append('\n');
                }
                text.Append(lines.Lines[i].ToString());
            }
            return text.ToString();
        }

        private static List<MarkdownRun> CollectRuns(Markdig.Syntax.Inlines.ContainerInline? container)
        {
            var runs = new List<MarkdownRun>();
            if (container == null)
            {
                return runs;
            }
            foreach (var inline in container)
            {
                CollectInline(inline, runs, bold: false, italic: false, strike: false, code: false, link: null);
            }
            return runs;
        }

        private static void CollectInline(Markdig.Syntax.Inlines.Inline inline, List<MarkdownRun> runs,
            bool bold, bool italic, bool strike, bool code, string? link)
        {
            switch (inline)
            {
                case Markdig.Syntax.Inlines.LiteralInline literal:
                    AddRun(literal.Content.ToString(), runs, bold, italic, strike, code, link);
                    break;

                case Markdig.Syntax.Inlines.CodeInline codeInline:
                    AddRun(codeInline.Content, runs, bold: false, italic: false, strike: false, code: true, link);
                    break;

                case Markdig.Syntax.Inlines.EmphasisInline emphasis:
                    bool emStrike = strike || emphasis.DelimiterChar == '~';
                    bool emBold = bold || (!emStrike && emphasis.DelimiterCount >= 2);
                    bool emItalic = italic || (!emStrike && emphasis.DelimiterCount == 1);
                    foreach (var child in emphasis)
                    {
                        CollectInline(child, runs, emBold, emItalic, emStrike, code, link);
                    }
                    break;

                case Markdig.Syntax.Inlines.LinkInline imageLink when imageLink.IsImage:
                {
                    // Images render as their alt text: a notepad preview keeps things light.
                    // The alt text is the inline content, not the reference label.
                    var altText = new StringBuilder();
                    foreach (var child in imageLink)
                    {
                        var childRuns = new List<MarkdownRun>();
                        CollectInline(child, childRuns, bold: false, italic: false, strike: false, code: false, link: null);
                        foreach (var childRun in childRuns)
                        {
                            altText.Append(childRun.Text);
                        }
                    }
                    AddRun(altText.Length == 0 ? "[image]" : altText.ToString(), runs, bold, italic, strike, false, null);
                    break;
                }

                case Markdig.Syntax.Inlines.LinkInline textLink:
                    string url = textLink.Url ?? string.Empty;
                    foreach (var child in textLink)
                    {
                        CollectInline(child, runs, bold, italic, strike, code, url);
                    }
                    break;

                case Markdig.Syntax.Inlines.AutolinkInline autolink:
                    AddRun(autolink.Url, runs, bold, italic, strike, code, autolink.Url);
                    break;

                case Markdig.Syntax.Inlines.LineBreakInline:
                    AddRun("\n", runs, bold, italic, strike, code, link);
                    break;

                case Markdig.Syntax.Inlines.HtmlEntityInline entity:
                    AddRun(entity.Transcoded.ToString(), runs, bold, italic, strike, code, link);
                    break;

                case Markdig.Syntax.Inlines.HtmlInline:
                    // Raw HTML tags are dropped, never rendered or interpreted.
                    break;

                default:
                    if (inline is Markdig.Syntax.Inlines.ContainerInline nested)
                    {
                        foreach (var child in nested)
                        {
                            CollectInline(child, runs, bold, italic, strike, code, link);
                        }
                    }
                    break;
            }
        }

        private static void AddRun(string text, List<MarkdownRun> runs, bool bold, bool italic, bool strike, bool code, string? link)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            runs.Add(new MarkdownRun(text, bold, italic, strike, code, link));
        }
    }
}
