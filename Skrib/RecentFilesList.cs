using System.Collections.Generic;
using System.Text.Json;

namespace Skrib;

/// <summary>
/// Pure list manipulation for the recent-files menu. Each entry pairs a FutureAccessList
/// token (what lets a packaged app re-open a file picked in an earlier session) with the
/// display path. Deliberately free of any WinUI dependency so the rules stay verifiable
/// headlessly (same contract as LanguageHelper / SettingsDefaults / UiStrings).
/// </summary>
public static class RecentFilesList
{
    /// <summary>One recent file: FutureAccessList token + path shown in the menu.</summary>
    public sealed record Entry(string Token, string Path);

    /// <summary>Maximum number of entries kept in the menu.</summary>
    public const int MaxEntries = 5;

    /// <summary>
    /// Parses the persisted JSON list; corrupt or missing input yields an empty list
    /// instead of throwing, so a bad setting value can never crash the startup path.
    /// </summary>
    public static List<Entry> Parse(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new List<Entry>();
        try
        {
            return JsonSerializer.Deserialize<List<Entry>>(json) ?? new List<Entry>();
        }
        catch (JsonException)
        {
            return new List<Entry>();
        }
    }

    /// <summary>
    /// Moves (or adds) the entry to the top, deduplicated by path, capped at
    /// <see cref="MaxEntries"/>. Entries evicted from the tail, and the replaced
    /// duplicate's stale token, are returned so the caller can free their
    /// FutureAccessList tokens.
    /// </summary>
    public static List<Entry> Push(List<Entry> entries, string token, string path, out List<Entry> evicted)
    {
        evicted = new List<Entry>();
        if (string.IsNullOrEmpty(path)) return entries;

        var result = new List<Entry>();
        foreach (var entry in entries)
        {
            if (entry.Path == path)
            {
                // The replaced duplicate would otherwise leak its FutureAccessList
                // token: it is no longer reachable from the list, so free it too.
                if (entry.Token != token) evicted.Add(entry);
            }
            else
            {
                result.Add(entry);
            }
        }
        result.Insert(0, new Entry(token, path));

        while (result.Count > MaxEntries)
        {
            var last = result[result.Count - 1];
            result.RemoveAt(result.Count - 1);
            evicted.Add(last);
        }
        return result;
    }

    /// <summary>Removes the entry matching the token, if present (stale or cleared files).</summary>
    public static List<Entry> RemoveByToken(List<Entry> entries, string token)
    {
        var result = new List<Entry>();
        foreach (var entry in entries)
        {
            if (entry.Token != token)
            {
                result.Add(entry);
            }
        }
        return result;
    }

    /// <summary>Serializes back to JSON for LocalSettings.</summary>
    public static string ToJson(List<Entry> entries)
    {
        return JsonSerializer.Serialize(entries);
    }
}
