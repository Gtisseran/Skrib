namespace Skrib.Tests;

/// <summary>
/// Guards the find/replace rules: case-insensitive plain substring matching, wrapping
/// navigation, and full replacement. These feed the editor search bar directly.
/// </summary>
public class SearchHelperTests
{
    private const string Text = "The quick brown fox. The fox jumps over the lazy dog.";

    [Fact]
    public void CountMatches_IsCaseInsensitiveAndCountsNonOverlapping()
    {
        Assert.Equal(2, SearchHelper.CountMatches(Text, "fox"));
        Assert.Equal(2, SearchHelper.CountMatches(Text, "FOX"));
        Assert.Equal(3, SearchHelper.CountMatches(Text, "the"));
    }

    [Fact]
    public void CountMatches_HandlesEdgeCases()
    {
        Assert.Equal(0, SearchHelper.CountMatches(Text, "cat"));
        Assert.Equal(0, SearchHelper.CountMatches(Text, ""));
        Assert.Equal(0, SearchHelper.CountMatches("", "fox"));
        Assert.Equal(0, SearchHelper.CountMatches(null, "fox"));
        // Query longer than the text never matches.
        Assert.Equal(0, SearchHelper.CountMatches("abc", "abcdef"));
    }

    [Fact]
    public void FindNext_ProceedsForwardThenWraps()
    {
        // "fox" occurs at 16 and 25 in Text.
        Assert.Equal(16, SearchHelper.FindNext(Text, "fox", 0));
        Assert.Equal(25, SearchHelper.FindNext(Text, "fox", 17));
        Assert.Equal(25, SearchHelper.FindNext(Text, "fox", 23));
        // Wrapping: searching after the last match returns to the first one.
        Assert.Equal(16, SearchHelper.FindNext(Text, "fox", 26));
        // Searching from the very end wraps too.
        Assert.Equal(16, SearchHelper.FindNext(Text, "fox", Text.Length));
    }

    [Fact]
    public void FindNext_HandlesEdgeCases()
    {
        Assert.Equal(-1, SearchHelper.FindNext(Text, "cat", 0));
        Assert.Equal(-1, SearchHelper.FindNext(Text, "", 0));
        Assert.Equal(-1, SearchHelper.FindNext("", "fox", 0));
    }

    [Fact]
    public void FindPrevious_ProceedsBackwardThenWraps()
    {
        Assert.Equal(16, SearchHelper.FindPrevious(Text, "fox", 23));
        Assert.Equal(16, SearchHelper.FindPrevious(Text, "fox", 25));
        // Wrapping: before the first match, jump to the last one.
        Assert.Equal(25, SearchHelper.FindPrevious(Text, "fox", 16));
        Assert.Equal(25, SearchHelper.FindPrevious(Text, "fox", 0));
    }

    [Fact]
    public void NextIndex_ProceedsForwardThenWraps()
    {
        var starts = SearchHelper.MatchStarts(Text, "fox"); // 16 and 25.

        Assert.Equal(0, SearchHelper.NextIndex(starts, 0));
        Assert.Equal(1, SearchHelper.NextIndex(starts, 17));
        Assert.Equal(1, SearchHelper.NextIndex(starts, 23));
        // Wrapping: after the last match, back to the first entry.
        Assert.Equal(0, SearchHelper.NextIndex(starts, 26));
        Assert.Equal(0, SearchHelper.NextIndex(starts, Text.Length));
    }

    [Fact]
    public void NextIndex_HandlesEmptyList()
    {
        Assert.Equal(-1, SearchHelper.NextIndex(new List<int>(), 0));
        Assert.Equal(-1, SearchHelper.NextIndex(SearchHelper.MatchStarts(Text, "cat"), 0));
    }

    [Fact]
    public void PreviousIndex_ProceedsBackwardThenWraps()
    {
        var starts = SearchHelper.MatchStarts(Text, "fox"); // 16 and 25.

        Assert.Equal(0, SearchHelper.PreviousIndex(starts, 23));
        Assert.Equal(0, SearchHelper.PreviousIndex(starts, 25));
        // Wrapping: before the first match, jump to the last entry.
        Assert.Equal(1, SearchHelper.PreviousIndex(starts, 16));
        Assert.Equal(1, SearchHelper.PreviousIndex(starts, 0));
    }

    [Fact]
    public void PreviousIndex_HandlesEmptyList()
    {
        Assert.Equal(-1, SearchHelper.PreviousIndex(new List<int>(), 0));
        Assert.Equal(-1, SearchHelper.PreviousIndex(SearchHelper.MatchStarts(Text, "cat"), 0));
    }

    [Fact]
    public void FindNextAndPrevious_AgreeWithIndexHelpers()
    {
        // The position-based helpers delegate to the index ones: both views must agree.
        var starts = SearchHelper.MatchStarts(Text, "fox");
        Assert.Equal(starts[SearchHelper.NextIndex(starts, 17)],
            SearchHelper.FindNext(Text, "fox", 17));
        Assert.Equal(starts[SearchHelper.PreviousIndex(starts, 16)],
            SearchHelper.FindPrevious(Text, "fox", 16));
    }

    [Fact]
    public void ReplaceAll_ReplacesEveryOccurrenceCaseInsensitively()
    {
        Assert.Equal("The quick brown cat. The cat jumps over the lazy dog.",
            SearchHelper.ReplaceAll(Text, "fox", "cat"));
        // Query case does not matter; replacement is used verbatim.
        Assert.Equal("The quick brown CAT. The CAT jumps over the lazy dog.",
            SearchHelper.ReplaceAll(Text, "FOX", "CAT"));
    }

    [Fact]
    public void ReplaceAll_HandlesEdgeCases()
    {
        // No match: text unchanged.
        Assert.Equal(Text, SearchHelper.ReplaceAll(Text, "cat", "dog"));
        Assert.Equal(Text, SearchHelper.ReplaceAll(Text, "", "dog"));
        // Empty replacement deletes occurrences.
        Assert.Equal("The quick brown . The  jumps over the lazy dog.",
            SearchHelper.ReplaceAll(Text, "fox", ""));
        Assert.Equal(string.Empty, SearchHelper.ReplaceAll("", "a", "b"));
    }

    [Fact]
    public void ReplaceAll_DoesNotLoopOnReplacementContainingQuery()
    {
        // A naive regex/loop implementation would loop forever or over-replace here.
        Assert.Equal("aaaa", SearchHelper.ReplaceAll("aa", "a", "aa"));
    }

    [Fact]
    public void Matching_FoldsCaseButNotAccents()
    {
        // Case folding does not strip accents: "resume" never matches "Résumé".
        Assert.Equal(0, SearchHelper.CountMatches("Résumé", "resume"));
        Assert.Equal(0, SearchHelper.CountMatches("resume", "Résumé"));
        // Accented letters still fold across cases (é ≡ É under ordinal ignore-case).
        Assert.Equal(1, SearchHelper.CountMatches("Résumé", "résumé"));
    }
}
