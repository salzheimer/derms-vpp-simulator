namespace Rva.Modules.GridTopology.Integration.Tests.Infrastructure;

internal static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "VppAggregator.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException("Could not find VppAggregator.slnx above " + AppContext.BaseDirectory);
    }
}
