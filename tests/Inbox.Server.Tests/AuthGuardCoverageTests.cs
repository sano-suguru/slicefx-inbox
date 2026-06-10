using Inbox.Server.Filters;
using SliceFx;
using System.Reflection;

namespace Inbox.Server.Tests;

/// <summary>
/// Fail-closed guard: every authenticated endpoint must declare WorkspaceAuthFilter.
/// Walks the assembly-level SliceFeatureRouteAttribute manifest so the check reflects
/// the actual generated route table, not just source annotations.
/// </summary>
public class AuthGuardCoverageTests
{
    // Public endpoints that intentionally have no workspace auth.
    private static readonly HashSet<string> s_publicEndpoints = new(StringComparer.Ordinal)
    {
        "Workspaces.CreateWorkspace",
        "Workspaces.EnsureDemo",
        "Share.GetSharePage",
        "Feeds.RefreshAllFeeds",
    };

    private static readonly string s_filterFqn =
        typeof(WorkspaceAuthFilter).FullName!;

    [Fact]
    public void Every_non_public_endpoint_carries_WorkspaceAuthFilter()
    {
        var asm = typeof(Inbox.Server.Features.Items.GetItem).Assembly;
        var routes = asm.GetCustomAttributes<SliceFeatureRouteAttribute>();

        var missing = new List<string>();
        foreach (var route in routes)
        {
            if (s_publicEndpoints.Contains(route.EndpointName))
                continue;

            var filters = route.SerializedSliceFilterTypes?
                .Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [];

            if (!filters.Any(f => f.Equals(s_filterFqn, StringComparison.Ordinal)))
                missing.Add(route.EndpointName);
        }

        Assert.Empty(missing);
    }
}
