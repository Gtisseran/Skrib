using System;
using System.Collections.Generic;
using System.Text;

namespace Skrib;

/// <summary>
/// Pure text-search primitives for the editor find/replace bar: case-insensitive plain
/// substring matching, no regex. Deliberately free of any WinUI dependency so the rules
/// stay verifiable headlessly (same contract as UiStrings / RecentFilesList).
/// </summary>
public static class SearchHelper
{
    /// <summary>All match start positions, left to right, non-overlapping.</summary>
    public static List<int> MatchStarts(string? text, string? query)
    {
        var starts = new List<int>();
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return starts;

        int pos = 0;
        while ((pos = IndexOf(text, query, pos)) >= 0)
        {
            starts.Add(pos);
            pos += query.Length;
        }
        return starts;
    }

    /// <summary>Number of non-overlapping matches.</summary>
    public static int CountMatches(string? text, string? query)
    {
        return MatchStarts(text, query).Count;
    }

    /// <summary>
    /// Start of the first match at or after <paramref name="startIndex"/> (wrapping to the
    /// beginning of the text), or -1 when the query never matches.
    /// </summary>
    public static int FindNext(string? text, string? query, int startIndex)
    {
        var starts = MatchStarts(text, query);
        foreach (int start in starts)
        {
            if (start >= startIndex) return start;
        }
        return starts.Count > 0 ? starts[0] : -1;
    }

    /// <summary>
    /// Start of the last match strictly before <paramref name="startIndex"/> (wrapping to
    /// the end of the text), or -1 when the query never matches.
    /// </summary>
    public static int FindPrevious(string? text, string? query, int startIndex)
    {
        var starts = MatchStarts(text, query);
        for (int i = starts.Count - 1; i >= 0; i--)
        {
            if (starts[i] < startIndex) return starts[i];
        }
        return starts.Count > 0 ? starts[starts.Count - 1] : -1;
    }

    /// <summary>
    /// Case-insensitive replace of every occurrence; returns the text unchanged when the
    /// query is empty or matches nothing.
    /// </summary>
    public static string ReplaceAll(string? text, string? query, string? replacement)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return text ?? string.Empty;

        var starts = MatchStarts(text, query);
        if (starts.Count == 0) return text;

        var sb = new StringBuilder(text.Length);
        int copy = 0;
        var with = replacement ?? string.Empty;
        foreach (int start in starts)
        {
            sb.Append(text, copy, start - copy);
            sb.Append(with);
            copy = start + query.Length;
        }
        sb.Append(text, copy, text.Length - copy);
        return sb.ToString();
    }

    private static int IndexOf(string text, string query, int startIndex)
    {
        return text.IndexOf(query, startIndex, StringComparison.OrdinalIgnoreCase);
    }
}
