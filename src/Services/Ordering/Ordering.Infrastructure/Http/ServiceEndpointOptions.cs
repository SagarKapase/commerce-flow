using System.ComponentModel.DataAnnotations;

namespace Ordering.Infrastructure.Http;

/// <summary>
/// Where a downstream service lives, and how long we will wait for it.
///
/// ONE CLASS, TWO INSTANCES - bound as NAMED OPTIONS ("Basket" and
/// "Inventory") in Program.cs. Basket needed only one of these in Phase 5, so a
/// single CatalogClientOptions was enough; Ordering talks to two services with
/// the same shape of configuration, and named options is the idiomatic answer
/// to "same settings type, several configured instances".
///
/// The alternative - BasketClientOptions and InventoryClientOptions, identical
/// but for their section name - would be two files, two validators, and a third
/// the day a Payment client arrives.
///
/// Retrieve one with IOptionsMonitor&lt;ServiceEndpointOptions&gt;.Get("Basket").
/// </summary>
public sealed class ServiceEndpointOptions
{
    [Required]
    [Url]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// HttpClient's default is one hundred seconds. On a checkout path that is
    /// not a timeout, it is a customer staring at a spinner for nearly two
    /// minutes and then pressing the button again.
    /// </summary>
    [Range(1, 60)]
    public int TimeoutSeconds { get; init; } = 5;
}
