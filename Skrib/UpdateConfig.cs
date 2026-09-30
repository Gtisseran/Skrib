namespace Skrib;

/// <summary>
/// Dépôt GitHub utilisé pour vérifier les mises à jour (releases).
/// Adaptez Owner et Repo si votre dépôt a un autre nom.
/// </summary>
internal static class UpdateConfig
{
    public const string GitHubOwner = "gaelt";
    public const string GitHubRepo = "Skrib";

    public static string ReleasesApiUrl =>
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    public static string ReleasesPageUrl =>
        $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases";

    public static string RepoUrl =>
        $"https://github.com/{GitHubOwner}/{GitHubRepo}";
}
