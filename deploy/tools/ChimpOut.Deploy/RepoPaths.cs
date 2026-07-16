namespace ChimpOut.Deploy;

/// <summary>Resolves workspace-root-relative paths (repos + terraform dirs).</summary>
public sealed class RepoPaths
{
    public string WorkspaceRoot { get; }
    public string MetaRepo => Path.Combine(WorkspaceRoot, "monkify-meta");
    public string BackendRepo => Path.Combine(WorkspaceRoot, "monkify-backend");
    public string FrontRepo => Path.Combine(WorkspaceRoot, "monkify-front");
    public string LpRepo => Path.Combine(WorkspaceRoot, "monkify-lp");
    public string TerraformPersistent => Path.Combine(MetaRepo, "deploy", "terraform", "persistent");
    public string TerraformWorkload => Path.Combine(MetaRepo, "deploy", "terraform", "workload");

    private RepoPaths(string workspaceRoot) => WorkspaceRoot = workspaceRoot;

    /// <summary>Explicit override, else search upward for the folder holding monkify-meta + monkify-backend.</summary>
    public static RepoPaths Resolve(string? overrideRoot)
    {
        if (!string.IsNullOrWhiteSpace(overrideRoot))
            return new RepoPaths(Path.GetFullPath(overrideRoot));

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "monkify-meta")) &&
                Directory.Exists(Path.Combine(dir.FullName, "monkify-backend")))
                return new RepoPaths(dir.FullName);
            dir = dir.Parent;
        }

        throw new DeployException(
            "Could not locate the workspace root (a folder containing monkify-meta and monkify-backend). Pass --workspace <path>.");
    }
}
