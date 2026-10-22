using LifeGraph.Graph.Contracts;
using LifeGraph.Resources.Domain;
using Limaj.Framework.Core;

namespace LifeGraph.Resources.Application;

/// <summary>
/// The Resources' reading of a link for the pipeline (DA-150, DA-154): the same parsing as a
/// capture, so a <c>url</c> typed in the Node Inspector and a pasted link keep one canonical
/// form. The messages never repeat the link (GEN-043).
/// </summary>
internal sealed class ResourceAddressRules : IResourceAddressRules
{
    public IReadOnlyList<string> ResourceKinds => Domain.ResourceKinds.All;

    public Result<ResourceAddress> Read(string? url, string field)
    {
        var parsing = ResourceUrl.Parse(url);
        if (parsing.Url is not { } resourceUrl)
        {
            var details = new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [MessageOf(parsing.Problem!.Value)] };
            return Result<ResourceAddress>.Fail(GraphErrors.ResourceUrlInvalid.ToError(GraphErrors.ResourceUrlInvalidMessage, details));
        }

        return Result<ResourceAddress>.Ok(new ResourceAddress(
            resourceUrl.Address.OriginalString.Trim(),
            resourceUrl.CanonicalUrl,
            resourceUrl.Kind.ToWireName(),
            resourceUrl.Provider));
    }

    public static string MessageOf(ResourceUrlProblem problem) => problem switch
    {
        ResourceUrlProblem.Missing => "Enter a link.",
        ResourceUrlProblem.TooLong => $"Must be at most {ResourceUrl.MaxLength} characters.",
        ResourceUrlProblem.UnsupportedScheme => "Only http and https links are kept.",
        ResourceUrlProblem.Credentials => "Remove the user name and password from the link.",
        _ => "This is not a valid link.",
    };
}
