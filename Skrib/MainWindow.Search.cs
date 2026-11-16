using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using Windows.System;

namespace Skrib
{
    // Search and replace half of MainWindow: the find/replace bar, the match
    // navigation and the replace actions.
    public sealed partial class MainWindow
    {
        #region Search and Replace

        // 0-based position of the current match among all matches (for the "n of m" counter).
        private int _searchMatchIndex;

        // Cached match positions, invalidated whenever the query or the document
        // changes: navigation and the counter re-use the list instead of rescanning
        // the whole document (two full scans per keystroke otherwise).
        private List<int>? _cachedMatches;

        // Shows the bar; withReplace also reveals the replace row.
        private void ShowSearch(bool withReplace)
        {
            // The document may have changed while the bar was hidden.
            _cachedMatches = null;
            SearchBar.Visibility = Visibility.Visible;
            ReplaceRow.Visibility = withReplace ? Visibility.Visible : Visibility.Collapsed;
            SearchTextBox.Focus(FocusState.Programmatic);
            SearchTextBox.SelectAll();
            UpdateSearchCount();
        }

        private void CloseSearch()
        {
            SearchBar.Visibility = Visibility.Collapsed;
            Editor.Focus(FocusState.Programmatic);
        }

        private List<int> CurrentMatches()
        {
            if (_cachedMatches is null)
            {
                _cachedMatches = SearchHelper.MatchStarts(Editor.Text, SearchTextBox.Text);
            }
            return _cachedMatches;
        }

        // Selects the match at matchIndex and refreshes the counter. With an empty
        // result list it only refreshes the counter (empty state).
        private void SelectMatchAt(List<int> starts, int matchIndex)
        {
            if (starts.Count == 0)
            {
                _searchMatchIndex = 0;
                UpdateSearchCount(0);
                return;
            }
            matchIndex = Math.Clamp(matchIndex, 0, starts.Count - 1);
            _searchMatchIndex = matchIndex;
            Editor.Select(starts[matchIndex], SearchTextBox.Text.Length);
            UpdateSearchCount(starts.Count);
        }

        private void FindNextMatch()
        {
            var starts = CurrentMatches();
            int pos = Editor.SelectionStart + Editor.SelectionLength;
            // NextIndex wraps to the first match when nothing follows the caret.
            SelectMatchAt(starts, SearchHelper.NextIndex(starts, pos));
        }

        private void FindPreviousMatch()
        {
            var starts = CurrentMatches();
            // PreviousIndex wraps to the last match when nothing precedes the caret.
            SelectMatchAt(starts, SearchHelper.PreviousIndex(starts, Editor.SelectionStart));
        }

        // totalOverride lets callers that just scanned the matches (SelectMatchAt) skip
        // the second full-text scan this refresh would otherwise do.
        private void UpdateSearchCount(int? totalOverride = null)
        {
            string query = SearchTextBox.Text;
            if (query.Length == 0)
            {
                SearchCountText.Text = string.Empty;
                return;
            }
            int total = totalOverride ?? CurrentMatches().Count;
            // An edit can remove matches after the selected one: clamp so the counter
            // never shows "3 of 2" until the next navigation recomputes the index.
            if (_searchMatchIndex >= total)
            {
                _searchMatchIndex = Math.Max(0, total - 1);
            }
            SearchCountText.Text = total == 0
                ? S("search.noMatches")
                : string.Format(S("search.matches"), _searchMatchIndex + 1, total);
        }

        private void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                if (IsShiftDown()) FindPreviousMatch(); else FindNextMatch();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                CloseSearch();
                e.Handled = true;
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // A new query restarts the search from the first match (one fresh scan).
            _cachedMatches = null;
            SelectMatchAt(CurrentMatches(), 0);
        }

        private void ReplaceTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                ReplaceCurrentMatch();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                CloseSearch();
                e.Handled = true;
            }
        }

        private void MenuSearch_Click(object sender, RoutedEventArgs e)
        {
            ShowSearch(false);
        }

        private void MenuReplace_Click(object sender, RoutedEventArgs e)
        {
            ShowSearch(true);
        }

        private void SearchNext_Click(object sender, RoutedEventArgs e)
        {
            FindNextMatch();
        }

        private void SearchPrevious_Click(object sender, RoutedEventArgs e)
        {
            FindPreviousMatch();
        }

        private void SearchClose_Click(object sender, RoutedEventArgs e)
        {
            CloseSearch();
        }

        private void ReplaceOne_Click(object sender, RoutedEventArgs e)
        {
            ReplaceCurrentMatch();
        }

        private void ReplaceCurrentMatch()
        {
            string query = SearchTextBox.Text;
            if (query.Length == 0) return;

            // If the current selection is a match, replace it before jumping to the next one.
            if (Editor.SelectionLength == query.Length &&
                string.Equals(Editor.SelectedText, query, StringComparison.OrdinalIgnoreCase))
            {
                Editor.SelectedText = ReplaceTextBox.Text;
                _isDirty = true;
            }
            FindNextMatch();
        }

        private void ReplaceAll_Click(object sender, RoutedEventArgs e)
        {
            string query = SearchTextBox.Text;
            if (query.Length == 0) return;
            string updated = SearchHelper.ReplaceAll(Editor.Text, query, ReplaceTextBox.Text);
            if (updated == Editor.Text) return;

            _suppressDirty = true;
            try { Editor.Text = updated; }
            finally { _suppressDirty = false; }
            _isDirty = true;
            QueueUiRefresh();
            UpdateSearchCount();
        }

        #endregion
    }
}
