using System;
using System.Collections.Generic;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Skrib
{
    /// <summary>
    /// Turns the flattened preview model into RichTextBlock content. Because the preview
    /// is native XAML, the window backdrop shows through (transparency setting), themes
    /// update through theme resources, and selection spans the whole document.
    /// </summary>
    public static class MarkdownPreviewBuilder
    {
        // Matches the sizes the previous MarkdownTextBlock preview used.
        private static readonly double[] HeadingSizes = { 26, 20, 17, 15, 13, 13 };
        private static readonly double[] HeadingTop = { 24, 20, 16, 12, 10, 10 };
        private static readonly double[] HeadingBottom = { 10, 8, 6, 5, 4, 4 };

        private static readonly FontFamily CodeFont = new("Consolas");

        /// <param name="lightTheme">True in light theme: picks the secondary/accent shades.</param>
        public static void Fill(RichTextBlock target, IReadOnlyList<object> blocks, bool lightTheme)
        {
            target.Blocks.Clear();
            bool first = true;
            AppendBlocks(target.Blocks, blocks, lightTheme, indent: 0, ref first);
        }

        private static void AppendBlocks(BlockCollection blocks, IReadOnlyList<object> content, bool light, int indent, ref bool first)
        {
            foreach (var item in content)
            {
                if (item is MarkdownBlock block)
                {
                    AppendBlock(blocks, block, light, indent, ref first);
                }
            }
        }

        private static void AppendBlock(BlockCollection blocks, MarkdownBlock block, bool light, int indent, ref bool first)
        {
            switch (block.Kind)
            {
                case MarkdownBlockKind.Heading:
                {
                    var p = NewParagraph();
                    AddRuns(p.Inlines, block.Runs, light, bold: true, headingSize: HeadingSizes[block.Level - 1]);
                    p.Margin = new Thickness(indent, first ? 0 : HeadingTop[block.Level - 1], indent, HeadingBottom[block.Level - 1]);
                    blocks.Add(p);
                    break;
                }

                case MarkdownBlockKind.Paragraph:
                {
                    var p = NewParagraph();
                    AddRuns(p.Inlines, block.Runs, light);
                    p.Margin = new Thickness(indent, 0, indent, 12);
                    blocks.Add(p);
                    break;
                }

                case MarkdownBlockKind.CodeBlock:
                {
                    var p = NewParagraph();
                    AddCodeLines(p, block.Text, light);
                    p.Margin = new Thickness(indent + 8, first ? 0 : 4, indent + 8, 12);
                    blocks.Add(p);
                    break;
                }

                case MarkdownBlockKind.Quote:
                {
                    AppendQuote(blocks, block, light, indent, ref first);
                    break;
                }

                case MarkdownBlockKind.List:
                {
                    AppendList(blocks, block.List!, light, indent, ref first);
                    break;
                }

                case MarkdownBlockKind.Rule:
                {
                    var p = NewParagraph();
                    p.Inlines.Add(new Run
                    {
                        Text = "──────────────────────────────",
                        Foreground = Brush(light, 0x20, 0x00, 0x00, 0x00, 0x20, 0xFF, 0xFF, 0xFF)
                    });
                    p.Margin = new Thickness(indent, first ? 0 : 8, indent, 16);
                    blocks.Add(p);
                    break;
                }

                case MarkdownBlockKind.Table:
                {
                    AppendTable(blocks, block.Rows, light, indent, ref first);
                    break;
                }
            }
            first = false;
        }

        private static void AppendQuote(BlockCollection blocks, MarkdownBlock block, bool light, int indent, ref bool first)
        {
            var quoteBrush = Brush(light, 0x99, 0x00, 0x00, 0x00, 0x99, 0xFF, 0xFF, 0xFF);
            AppendQuoteBlocks(blocks, block.Blocks, light, indent, quoteBrush, ref first);
        }

        private static void AppendQuoteBlocks(BlockCollection blocks, IReadOnlyList<object> content, bool light, int indent, Brush quoteBrush, ref bool first)
        {
            foreach (var item in content)
            {
                if (item is not MarkdownBlock block)
                {
                    continue;
                }
                switch (block.Kind)
                {
                    case MarkdownBlockKind.Paragraph or MarkdownBlockKind.Heading:
                    {
                        var p = NewParagraph();
                        AddRuns(p.Inlines, block.Runs, light, bold: block.Kind == MarkdownBlockKind.Heading);
                        p.Foreground = quoteBrush;
                        p.Margin = new Thickness(indent + 12, first ? 0 : 4, indent, 4);
                        blocks.Add(p);
                        break;
                    }
                    case MarkdownBlockKind.CodeBlock:
                    {
                        var p = NewParagraph();
                        AddCodeLines(p, block.Text, light);
                        p.Foreground = quoteBrush;
                        p.Margin = new Thickness(indent + 20, first ? 0 : 4, indent, 4);
                        blocks.Add(p);
                        break;
                    }
                    case MarkdownBlockKind.Quote:
                        AppendQuoteBlocks(blocks, block.Blocks, light, indent + 12, quoteBrush, ref first);
                        break;
                }
                first = false;
            }
        }

        private static void AppendList(BlockCollection blocks, MarkdownList list, bool light, int indent, ref bool first)
        {
            int index = list.StartIndex;
            foreach (var item in list.Items)
            {
                string marker = list.Ordered ? $"{index++}.  " : "•  ";
                bool itemFirst = true;
                foreach (var inner in item.Blocks)
                {
                    if (inner is not MarkdownBlock block)
                    {
                        continue;
                    }
                    switch (block.Kind)
                    {
                        case MarkdownBlockKind.Paragraph or MarkdownBlockKind.Heading:
                        {
                            var p = NewParagraph();
                            if (itemFirst)
                            {
                                p.Inlines.Add(new Run { Text = marker, Foreground = Brush(light, 0x99, 0x00, 0x00, 0x00, 0x99, 0xFF, 0xFF, 0xFF) });
                            }
                            AddRuns(p.Inlines, block.Runs, light, bold: block.Kind == MarkdownBlockKind.Heading);
                            p.Margin = new Thickness(indent + 16, first ? 0 : (itemFirst ? 4 : 0), indent, 4);
                            blocks.Add(p);
                            break;
                        }
                        case MarkdownBlockKind.List:
                            AppendList(blocks, block.List!, light, indent + 16, ref first);
                            break;
                        case MarkdownBlockKind.CodeBlock:
                        {
                            var p = NewParagraph();
                            AddCodeLines(p, block.Text, light);
                            p.Margin = new Thickness(indent + 24, first ? 0 : 4, indent, 4);
                            blocks.Add(p);
                            break;
                        }
                    }
                    itemFirst = false;
                    first = false;
                }
            }
        }

        private static void AppendTable(BlockCollection blocks, IReadOnlyList<MarkdownTableRow> rows, bool light, int indent, ref bool first)
        {
            foreach (var row in rows)
            {
                var p = NewParagraph();
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    if (c > 0)
                    {
                        p.Inlines.Add(new Run
                        {
                            Text = "  |  ",
                            Foreground = Brush(light, 0x66, 0x00, 0x00, 0x00, 0x66, 0xFF, 0xFF, 0xFF)
                        });
                    }
                    AddRuns(p.Inlines, row.Cells[c], light, bold: row.IsHeader);
                }
                p.Margin = new Thickness(indent, first ? 0 : (row.IsHeader ? 4 : 0), indent, row.IsHeader ? 4 : 6);
                blocks.Add(p);
                first = false;
            }
        }

        private static Paragraph NewParagraph()
        {
            return new Paragraph();
        }

        private static void AddRuns(InlineCollection inlines, IReadOnlyList<MarkdownRun> runs, bool light,
            bool bold = false, double? headingSize = null)
        {
            foreach (var run in runs)
            {
                if (run.Text == "\n")
                {
                    inlines.Add(new LineBreak());
                    continue;
                }
                bool runBold = bold || run.Bold;
                if (run.LinkUrl is not null)
                {
                    var link = new Hyperlink
                    {
                        UnderlineStyle = UnderlineStyle.None,
                        Foreground = Brush(light, 0xFF, 0x00, 0x67, 0xC0, 0xFF, 0x4C, 0xC2, 0xFF)
                    };
                    var inner = new Run { Text = run.Text };
                    ApplyTextStyle(inner, run, light, runBold, headingSize);
                    link.Inlines.Add(inner);
                    string url = run.LinkUrl;
                    link.Click += (_, _) => OpenLink(url);
                    inlines.Add(link);
                    continue;
                }
                var text = new Run { Text = run.Text };
                ApplyTextStyle(text, run, light, runBold, headingSize);
                inlines.Add(text);
            }
        }

        private static void ApplyTextStyle(Run run, MarkdownRun style, bool light, bool bold, double? headingSize)
        {
            if (bold)
            {
                run.FontWeight = FontWeights.SemiBold;
            }
            if (style.Italic || bold)
            {
                run.FontStyle = style.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
            }
            if (style.Strikethrough)
            {
                run.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
            }
            if (style.Code)
            {
                run.FontFamily = CodeFont;
                run.FontSize = headingSize.HasValue ? headingSize.Value * 0.85 : 13;
            }
            else if (headingSize.HasValue)
            {
                run.FontSize = headingSize.Value;
            }
        }

        private static void AddCodeLines(Paragraph p, string text, bool light)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var run = new Run
                {
                    Text = lines[i],
                    FontFamily = CodeFont,
                    FontSize = 13,
                    Foreground = Brush(light, 0xD9, 0x00, 0x00, 0x00, 0xD9, 0xFF, 0xFF, 0xFF)
                };
                p.Inlines.Add(run);
                if (i < lines.Length - 1)
                {
                    p.Inlines.Add(new LineBreak());
                }
            }
            p.LineHeight = 20;
            p.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        }

        private static Brush Brush(bool light, byte alphaLight, byte rLight, byte gLight, byte bLight,
            byte alphaDark, byte rDark, byte gDark, byte bDark)
        {
            var color = light
                ? Color.FromArgb(alphaLight, rLight, gLight, bLight)
                : Color.FromArgb(alphaDark, rDark, gDark, bDark);
            return new SolidColorBrush(color);
        }

        private static async void OpenLink(string url)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(url)
                    && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    && (uri.Scheme == "http" || uri.Scheme == "https" || uri.Scheme == "mailto"))
                {
                    await Windows.System.Launcher.LaunchUriAsync(uri);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write($"Error opening link from preview: {ex.Message}");
            }
        }
    }
}
