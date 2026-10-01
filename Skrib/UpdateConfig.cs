namespace Skrib;

/// <summary>
/// GitHub repository used to check for updates (releases).
/// Adjust Owner and Repo if your repository has a different name.
/// </summary>
internal static class UpdateConfig
{
    public const string GitHubOwner = "Gtisseran";
    public const string GitHubRepo = "Skrib";

    public static string ReleasesApiUrl =>
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    public static string ReleasesPageUrl =>
        $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases";

    public static string RepoUrl =>
        $"https://github.com/{GitHubOwner}/{GitHubRepo}";
}
