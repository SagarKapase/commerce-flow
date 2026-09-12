namespace Basket.Api.Catalog;

/// <summary>
/// Catalog's product, as BASKET understands it.
///
/// ============ THE MOST IMPORTANT FILE IN THIS PHASE ============
/// Catalog's real ProductResponse has ten properties: id, name, description,
/// sku, price, categoryId, categoryName, isActive, createdAtUtc, updatedAtUtc.
/// This record declares FOUR - the only four Basket has any use for.
///
/// System.Text.Json ignores JSON properties it has no member for, so this is a
/// "tolerant reader": Catalog can add fields, reorder them, or rename ones we
/// do not read, and Basket keeps working without a redeploy. Only removing or
/// renaming one of THESE four is a breaking change - which is now a small,
/// explicit list instead of "everything".
///
/// The alternative everybody reaches for first is a shared Contracts assembly
/// holding one ProductResponse that both services reference. It looks tidy and
/// it quietly destroys the property that makes microservices worth the trouble:
/// once Catalog and Basket share a compiled type, adding a field to it means
/// rebuilding and redeploying both, in lockstep, forever. That is a distributed
/// monolith - all the operational cost of microservices, none of the
/// independence.
///
/// Duplicating four property names is the cheap option. It only looks like the
/// expensive one.
/// ================================================================
///
/// On casing: Catalog sends camelCase ("productName"), these members are
/// PascalCase, and it binds anyway because ReadFromJsonAsync uses
/// JsonSerializerDefaults.Web, which is case-insensitive. Worth knowing - the
/// bare JsonSerializer default is NOT.
/// </summary>
public sealed record CatalogProduct(
    Guid Id,
    string Name,
    decimal Price,
    bool IsActive);
