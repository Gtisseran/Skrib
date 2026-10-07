using Microsoft.UI.Xaml;
using System;
using Microsoft.Windows.AppLifecycle;
using System.Linq;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace Skrib
{
    // Application entry point. Handles single-instance redirection and file activation (.txt / .md).
    public partial class App : Application
    {
        private Window? _window;

        public App()
        {
            InitializeComponent();
        }

        // Invoked when the application is launched (including via .txt / .md file association).
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Register (or find) the main instance so a second launch redirects instead of opening a new window.
            var mainInstance = AppInstance.FindOrRegisterForKey("SkribMain");
            var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();

            if (!mainInstance.IsCurrent)
            {
                // Redirect this activation to the running instance, then exit.
                await mainInstance.RedirectActivationToAsync(activatedArgs);
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                return;
            }

            mainInstance.Activated += OnAppActivated;

            var window = new MainWindow();
            _window = window;
            window.Activate();

            var fileToOpen = GetFileFromArgs(activatedArgs);
            if (fileToOpen != null)
            {
                await window.OpenFileFromStorageFileAsync(fileToOpen);
            }
        }

        // Handles activations redirected from a second instance (e.g. double-click on a file while running).
        private void OnAppActivated(object? sender, AppActivationArguments args)
        {
            var file = GetFileFromArgs(args);
            if (file == null || _window is not MainWindow mainWindow)
            {
                return;
            }

            // Switch back to the UI thread before touching the window.
            _window.DispatcherQueue.TryEnqueue(async () =>
            {
                await mainWindow.OpenFileFromStorageFileAsync(file);
            });
        }

        // Extracts the first storage file from file activation args, if any.
        private static StorageFile? GetFileFromArgs(AppActivationArguments args)
        {
            if (args.Kind == ExtendedActivationKind.File
                && args.Data is IFileActivatedEventArgs fileArgs)
            {
                return fileArgs.Files.OfType<StorageFile>().FirstOrDefault();
            }
            return null;
        }
    }
}