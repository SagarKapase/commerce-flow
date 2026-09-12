using System.ComponentModel.DataAnnotations;

namespace Basket.Api.Catalog;

/// <summary>
/// Where the Catalog service lives, and how long we are willing to wait for it.
///
/// The URL is CONFIGURATION, never a constant in code. Three reasons that
/// matter beyond "hard-coding is bad":
///   - Every environment has a different address. A compiled-in localhost URL
///     means the same binary cannot run anywhere else.
///   - It is the seam where a real deployment inserts a load balancer, a
///     service-discovery name, or the API gateway.
///   - It makes the dependency VISIBLE. Read appsettings.json and you can see
///     exactly which services this one needs alive to do its job. A URL buried
///     in a class is a dependency nobody knew about until it broke.
/// </summary>
public sealed class CatalogClientOptions
{
    public const string SectionName = "Services:Catalog";

    [Required]
    [Url]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// HttpClient's default timeout is ONE HUNDRED SECONDS. On a user-facing
    /// path that is not a timeout, it is a hostage situation: a hung Catalog
    /// holds a Basket request - and the thread and connection serving it - for
    /// nearly two minutes, while the customer stares at a spinner and hits
    /// refresh, generating more of the same.
    ///
    /// Five seconds is a decision. Anything internal that has not answered in
    /// five seconds is not going to answer usefully.
    /// </summary>
    [Range(1, 60)]
    public int TimeoutSeconds { get; init; } = 5;
}
