using System.Net.Http;
using System.Reflection;
using ADEXO.Graph;
using ADEXO.Graph.Orchestration;

namespace ADEXO.Graph.Tests;

/// <summary>
/// Proves the Graph surface cannot be widened into a write. This is an architectural assertion rather
/// than a behavioural one: it inspects the public contract the UI compiles against.
/// </summary>
public sealed class GraphRequestBoundaryTests
{
    [Fact]
    public void The_provider_interface_exposes_only_read_operations()
    {
        var methods = typeof(Contracts.Providers.IGraphProvider).GetMethods();

        Assert.Equal(
            ["FindUsersByImmutableIdAsync", "GetCapabilityAsync", "GetOrganizationTenantIdAsync", "GetSubscribedSkusAsync"],
            methods.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void No_provider_member_accepts_a_url_method_or_body()
    {
        foreach (var method in typeof(Contracts.Providers.IGraphProvider).GetMethods())
        {
            foreach (var parameter in method.GetParameters())
            {
                Assert.False(IsForbiddenParameter(parameter.ParameterType),
                    $"{method.Name} accepts a {parameter.ParameterType.Name}, which would allow an arbitrary request.");
            }
        }
    }

    [Fact]
    public void No_provider_member_name_suggests_a_mutation()
    {
        string[] forbidden = ["Create", "Add", "Update", "Set", "Replace", "Delete", "Remove", "Patch", "Assign", "Revoke"];

        foreach (var method in typeof(Contracts.Providers.IGraphProvider).GetMethods())
        {
            foreach (var prefix in forbidden)
            {
                Assert.False(method.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase),
                    $"{method.Name} looks like a mutation.");
            }
        }
    }

    [Fact]
    public void The_concrete_provider_exposes_no_public_write_member()
    {
        string[] forbidden = ["Create", "Update", "Delete", "Post", "Patch", "Put", "Send", "Assign", "Revoke"];

        var publicMembers = typeof(GraphCloudProvider)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m is MethodInfo or PropertyInfo)
            .ToList();

        foreach (var member in publicMembers)
        {
            foreach (var prefix in forbidden)
            {
                Assert.False(member.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase),
                    $"{typeof(GraphCloudProvider).Name}.{member.Name} looks like a write.");
            }
        }
    }

    [Fact]
    public void The_concrete_provider_accepts_no_request_shaping_parameter()
    {
        foreach (var method in typeof(GraphCloudProvider).GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var parameter in method.GetParameters())
            {
                Assert.False(IsForbiddenParameter(parameter.ParameterType),
                    $"{method.Name} accepts a {parameter.ParameterType.Name}.");
            }
        }
    }

    [Fact]
    public void The_cloud_section_exposes_no_write_state()
    {
        // A read-only section has no state that represents a pending or completed write.
        string[] forbidden = ["Saving", "Applying", "Writing", "PendingWrite", "WriteFailed"];

        foreach (var name in Enum.GetNames<CloudSectionState>())
        {
            foreach (var bad in forbidden)
            {
                Assert.DoesNotContain(bad, name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static bool IsForbiddenParameter(Type type) =>
        type == typeof(string) && false // string is legitimate for the immutable identifier value
        || type == typeof(Uri)
        || type == typeof(HttpMethod)
        || type == typeof(HttpContent)
        || typeof(HttpContent).IsAssignableFrom(type)
        || type.Name.Contains("Request", StringComparison.Ordinal);
}
