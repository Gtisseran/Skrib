using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.AccessCache;

namespace Skrib
{
    // Recent-files half of MainWindow: the FutureAccessList-backed "Recent files"
    // submenu (list maintenance, token freeing and menu rebuild).
    public sealed partial class MainWindow
    {
        #region Recent Files

        // Files opened in earlier sessions, reachable after relaunch. The FutureAccessList
        // token is what grants access to a file picked before; the path is display-only.
        private void AddToRecentFiles(StorageFile file)
        {
            try
            {
                var entries = LoadRecentFiles();
                string token = StorageApplicationPermissions.FutureAccessList.Add(file);
                entries = RecentFilesList.Push(entries, token, file.Path, out var evicted);
                FreeRecentFileTokens(evicted);
                ApplicationData.Current.LocalSettings.Values["RecentFiles"] = RecentFilesList.ToJson(entries);
                UpdateRecentFilesMenu(entries);
            }
            catch (Exception ex)
            {
                Debug.Write($"Error updating recent files: {ex.Message}");
            }
        }

        private void RemoveRecentFile(string token)
        {
            try
            {
                var entries = RecentFilesList.RemoveByToken(LoadRecentFiles(), token);
                try { StorageApplicationPermissions.FutureAccessList.Remove(token); }
                catch (Exception ex) { Debug.Write($"Error freeing recent file token: {ex.Message}"); }
                ApplicationData.Current.LocalSettings.Values["RecentFiles"] = RecentFilesList.ToJson(entries);
                UpdateRecentFilesMenu(entries);
            }
            catch (Exception ex)
            {
                Debug.Write($"Error removing recent file: {ex.Message}");
            }
        }

        private void ClearRecentFiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var entries = LoadRecentFiles();
                FreeRecentFileTokens(entries);
                ApplicationData.Current.LocalSettings.Values["RecentFiles"] = RecentFilesList.ToJson(new List<RecentFilesList.Entry>());
                UpdateRecentFilesMenu(new List<RecentFilesList.Entry>());
            }
            catch (Exception ex)
            {
                Debug.Write($"Error clearing recent files: {ex.Message}");
            }
        }

        private static List<RecentFilesList.Entry> LoadRecentFiles()
        {
            try
            {
                var entries = RecentFilesList.Parse(ApplicationData.Current.LocalSettings.Values["RecentFiles"] as string);
                // A list saved by an older build can exceed the current cap: trim the
                // tail and free the evicted FutureAccessList tokens.
                if (entries.Count > RecentFilesList.MaxEntries)
                {
                    int surplus = entries.Count - RecentFilesList.MaxEntries;
                    var evicted = entries.GetRange(RecentFilesList.MaxEntries, surplus);
                    entries.RemoveRange(RecentFilesList.MaxEntries, surplus);
                    FreeRecentFileTokens(evicted);
                }
                return entries;
            }
            catch (Exception ex)
            {
                Debug.Write($"Error loading recent files: {ex.Message}");
                return new List<RecentFilesList.Entry>();
            }
        }

        private static void FreeRecentFileTokens(List<RecentFilesList.Entry> entries)
        {
            foreach (var entry in entries)
            {
                try { StorageApplicationPermissions.FutureAccessList.Remove(entry.Token); }
                catch (Exception ex) { Debug.Write($"Error freeing recent file token: {ex.Message}"); }
            }
        }

        // Rebuilds the "Recent files" submenu (hidden while the list is empty).
        private void UpdateRecentFilesMenu()
        {
            UpdateRecentFilesMenu(LoadRecentFiles());
        }

        private void UpdateRecentFilesMenu(List<RecentFilesList.Entry> entries)
        {
            MenuRecent.Items.Clear();
            foreach (var entry in entries)
            {
                var item = new MenuFlyoutItem { Text = Path.GetFileName(entry.Path), Tag = entry.Token };
                ToolTipService.SetToolTip(item, entry.Path);
                item.Click += RecentFile_Click;
                MenuRecent.Items.Add(item);
            }
            if (entries.Count > 0)
            {
                MenuRecent.Items.Add(new MenuFlyoutSeparator());
                var clear = new MenuFlyoutItem { Text = S("recent.clear") };
                clear.Click += ClearRecentFiles_Click;
                MenuRecent.Items.Add(clear);
            }
            MenuRecent.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void RecentFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not MenuFlyoutItem item || item.Tag is not string token) return;
                StorageFile? file;
                try
                {
                    file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync(token);
                }
                catch (Exception ex)
                {
                    // A moved or deleted file invalidates its entry: drop it from the menu.
                    Debug.Write($"Error opening recent file: {ex.Message}");
                    RemoveRecentFile(token);
                    return;
                }

                // The save prompt belongs to OpenFileFromStorageFileAsync: asking here
                // too would show the dialog twice after "Don't save" (the document
                // stays dirty).
                await OpenFileFromStorageFileAsync(file);
            }
            catch (Exception ex)
            {
                // async void: an unobserved failure (e.g. a busy dialog) would crash the app.
                Debug.Write($"Error opening recent file: {ex.Message}");
            }
        }

        #endregion
    }
}
