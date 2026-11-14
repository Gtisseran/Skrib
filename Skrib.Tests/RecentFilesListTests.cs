namespace Skrib.Tests;

/// <summary>
/// Guards the recent-files list rules: deduplication by path, newest-first order,
/// cap enforcement with eviction feedback, and corruption-tolerant parsing.
/// </summary>
public class RecentFilesListTests
{
    [Fact]
    public void Push_AddsNewEntryAtTheTop()
    {
        var entries = new List<RecentFilesList.Entry>
        {
            new("t2", @"C:\a\old.md"),
        };

        var result = RecentFilesList.Push(entries, "t1", @"C:\a\new.md", out var evicted);

        Assert.Equal(2, result.Count);
        Assert.Equal(@"C:\a\new.md", result[0].Path);
        Assert.Equal("t1", result[0].Token);
        Assert.Equal(@"C:\a\old.md", result[1].Path);
        Assert.Empty(evicted);
    }

    [Fact]
    public void Push_DeduplicatesByPathAndMovesToTheTop()
    {
        var entries = new List<RecentFilesList.Entry>
        {
            new("t1", @"C:\a\doc.md"),
            new("t2", @"C:\b\other.md"),
        };

        // Same file, new token (each pick grants a fresh token): the old entry must go.
        var result = RecentFilesList.Push(entries, "t3", @"C:\a\doc.md", out var evicted);

        Assert.Equal(2, result.Count);
        Assert.Equal(@"C:\a\doc.md", result[0].Path);
        Assert.Equal("t3", result[0].Token);
        Assert.Equal(@"C:\b\other.md", result[1].Path);
        Assert.Empty(evicted);
    }

    [Fact]
    public void Push_CapsTheListAndReturnsEvictedEntries()
    {
        var entries = new List<RecentFilesList.Entry>();
        for (int i = 0; i < RecentFilesList.MaxEntries; i++)
        {
            entries.Add(new($"t{i}", @$"C:\a\file{i}.md"));
        }

        var result = RecentFilesList.Push(entries, "new", @"C:\a\newest.md", out var evicted);

        Assert.Equal(RecentFilesList.MaxEntries, result.Count);
        Assert.Equal(@"C:\a\newest.md", result[0].Path);
        var tail = entries[^1];
        Assert.Single(evicted);
        Assert.Equal(tail.Token, evicted[0].Token);
    }

    [Fact]
    public void Push_IgnoresAnEmptyPath()
    {
        var entries = new List<RecentFilesList.Entry> { new("t1", @"C:\a\doc.md") };

        var result = RecentFilesList.Push(entries, "t2", string.Empty, out var evicted);

        Assert.Single(result);
        Assert.Equal("t1", result[0].Token);
        Assert.Empty(evicted);
    }

    [Fact]
    public void Parse_HandlesCorruptJson()
    {
        Assert.Empty(RecentFilesList.Parse("not json at all"));
        Assert.Empty(RecentFilesList.Parse("[{broken"));
    }

    [Fact]
    public void Parse_HandlesEmptyInputAndNull()
    {
        Assert.Empty(RecentFilesList.Parse(null));
        Assert.Empty(RecentFilesList.Parse(string.Empty));
    }

    [Fact]
    public void Parse_ToJson_RoundTrips()
    {
        var entries = new List<RecentFilesList.Entry>
        {
            new("t1", @"C:\a\doc.md"),
            new("t2", @"C:\b\other.md"),
        };

        var roundTripped = RecentFilesList.Parse(RecentFilesList.ToJson(entries));

        Assert.Equal(2, roundTripped.Count);
        Assert.Equal("t1", roundTripped[0].Token);
        Assert.Equal(@"C:\a\doc.md", roundTripped[0].Path);
        Assert.Equal("t2", roundTripped[1].Token);
        Assert.Equal(@"C:\b\other.md", roundTripped[1].Path);
    }

    [Fact]
    public void RemoveByToken_RemovesOnlyTheMatchingEntry()
    {
        var entries = new List<RecentFilesList.Entry>
        {
            new("t1", @"C:\a\doc.md"),
            new("t2", @"C:\b\other.md"),
        };

        var result = RecentFilesList.RemoveByToken(entries, "t1");

        var tail = entries[^1];
        Assert.Single(result);
        Assert.Equal(tail.Token, result[0].Token);
    }
}
