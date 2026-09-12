using System.ComponentModel.DataAnnotations;
using Catalog.Domain.Entities;

namespace Catalog.Application.Common.Validation;

/// <summary>
/// Validates that a decimal is a usable amount of money: inside the allowed
/// range and no finer than two decimal places.
///
/// WHY A CUSTOM ATTRIBUTE INSTEAD OF [Range]:
/// [Range(0.01, 1000000)] covers the bounds but says nothing about scale, so a
/// price of 10.999 would sail through and then be rounded somewhere deep in the
/// persistence layer. Catching it here turns a silent data corruption into a
/// clear 400 Bad Request that names the offending field.
///
/// This also shows how DataAnnotations actually work: ASP.NET Core calls
/// IsValid on every attribute it finds while binding the request, collects the
/// failures into ModelState, and [ApiController] turns a non-empty ModelState
/// into a 400 ValidationProblemDetails before your action ever runs.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class MoneyAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        // A missing JSON property binds to 0m on a non-nullable decimal, which
        // fails the range check below and produces the right message.
        if (value is not decimal amount)
        {
            return false;
        }

        if (amount < Product.MinimumPrice || amount > Product.MaximumPrice)
        {
            return false;
        }

        return decimal.Round(amount, 2) == amount;
    }

    public override string FormatErrorMessage(string name)
    {
        return $"{name} must be between {Product.MinimumPrice} and {Product.MaximumPrice} " +
               "and have at most two decimal places.";
    }
}
