namespace Ordering.Domain.ValueObjects;

/// <summary>
/// Where the order is going.
///
/// A VALUE OBJECT, not an entity - and the difference is the whole point of
/// this file. An entity has identity: two orders with identical contents are
/// still two different orders, because they have different ids. A value object
/// has no identity: two addresses with the same street, city and postcode ARE
/// the same address. There is nothing else to distinguish them.
///
/// Which is why it is a `record`. C# records give value equality for free -
/// Equals and GetHashCode compare the contents, so
///     new Address("1 High St", ...) == new Address("1 High St", ...)
/// is true. Writing that by hand for an entity would be a bug; here it is the
/// correct semantics, and the language expresses it in one keyword.
///
/// It is also IMMUTABLE. You do not edit an address, you replace it - exactly
/// as you would not edit the number 5 into a 6. That removes a whole class of
/// question ("did changing this address change it on the other order too?")
/// before it can be asked.
///
/// In the database it does not get its own table. EF maps it with OwnsOne, so
/// the four fields become four columns on Orders - see OrderConfiguration.
/// </summary>
public sealed record Address(
    string Line1,
    string City,
    string PostalCode,
    string Country)
{
    public const int Line1MaxLength = 200;
    public const int CityMaxLength = 100;
    public const int PostalCodeMaxLength = 20;
    public const int CountryMaxLength = 100;
}
