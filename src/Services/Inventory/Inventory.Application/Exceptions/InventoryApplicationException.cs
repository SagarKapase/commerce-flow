namespace Inventory.Application.Exceptions;

/// <summary>Base type for expected failures raised by the application layer.</summary>
public abstract class InventoryApplicationException : Exception
{
    protected InventoryApplicationException(string message)
        : base(message)
    {
    }
}
