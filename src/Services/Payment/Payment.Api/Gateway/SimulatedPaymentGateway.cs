using Microsoft.Extensions.Options;
using Payment.Api.Exceptions;

namespace Payment.Api.Gateway;

/// <summary>How the simulation behaves. Bound from the "PaymentSimulation" section.</summary>
public sealed class PaymentSimulationOptions
{
    public const string SectionName = "PaymentSimulation";

    /// <summary>
    /// How long tok_timeout hangs for. Must be comfortably longer than the
    /// caller's HTTP timeout (Ordering waits 5 seconds) or the test proves
    /// nothing.
    /// </summary>
    public int TimeoutDelaySeconds { get; init; } = 10;

    /// <summary>
    /// A small delay on every call, so the happy path is not instantaneous.
    /// Real gateways take a few hundred milliseconds, and code that has only
    /// ever run against an instant dependency tends to hide its timing bugs.
    /// </summary>
    public int NormalDelayMilliseconds { get; init; } = 150;
}

/// <summary>
/// A payment provider that moves no money.
///
/// Everything here is deterministic. Given the same token you get the same
/// answer, every time, on every machine - which is what makes the failure
/// paths testable at all. A simulation that failed randomly would be more
/// "realistic" and completely useless: you could never reproduce a bug, and
/// nobody could tell a real defect from the dice.
/// </summary>
public sealed class SimulatedPaymentGateway : IPaymentGateway
{
    private readonly PaymentSimulationOptions _options;
    private readonly ILogger<SimulatedPaymentGateway> _logger;

    public SimulatedPaymentGateway(
        IOptions<PaymentSimulationOptions> options,
        ILogger<SimulatedPaymentGateway> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PaymentGatewayResult> ChargeAsync(
        string paymentMethodToken,
        decimal amount,
        CancellationToken cancellationToken)
    {
        if (!PaymentMethodTokens.IsKnown(paymentMethodToken))
        {
            throw new UnknownPaymentMethodException(paymentMethodToken);
        }

        _logger.LogInformation(
            "Charging {Amount} with token {Token}",
            amount,
            paymentMethodToken);

        if (paymentMethodToken == PaymentMethodTokens.Timeout)
        {
            // -----------------------------------------------------------
            // THE MOST INSTRUCTIVE LINE IN THIS SERVICE.
            //
            // The caller will give up long before this returns. But notice
            // what happens next: this method carries on, the payment is
            // recorded as SUCCEEDED, and the money "moves" - while the caller
            // has already decided the request failed.
            //
            // That is not a bug in the simulation. It is precisely what a real
            // gateway does when the network drops the response: the charge
            // went through and you have no idea. Any system that treats a
            // timeout as "it did not happen" will eventually charge somebody
            // twice.
            //
            // Note the cancellationToken is deliberately NOT passed to Delay.
            // A real payment provider does not abandon a charge because the
            // client hung up.
            // -----------------------------------------------------------
            _logger.LogWarning(
                "Simulating a slow gateway - sleeping {Seconds}s. The caller will time out, " +
                "but this charge WILL still complete",
                _options.TimeoutDelaySeconds);

            await Task.Delay(TimeSpan.FromSeconds(_options.TimeoutDelaySeconds), CancellationToken.None);

            return PaymentGatewayResult.Approved(NewProviderReference());
        }

        await Task.Delay(_options.NormalDelayMilliseconds, cancellationToken);

        return paymentMethodToken switch
        {
            PaymentMethodTokens.Success =>
                PaymentGatewayResult.Approved(NewProviderReference()),

            PaymentMethodTokens.Declined =>
                PaymentGatewayResult.Declined("The card was declined by the issuing bank."),

            PaymentMethodTokens.InsufficientFunds =>
                PaymentGatewayResult.Declined("The card has insufficient funds."),

            // Unreachable - IsKnown already filtered the rest - but a switch
            // over strings that cannot prove exhaustiveness needs a default,
            // and "throw" is the honest one. Returning a success here would be
            // the worst possible default in a payment system.
            _ => throw new UnknownPaymentMethodException(paymentMethodToken)
        };
    }

    /// <summary>
    /// A plausible-looking provider reference. Real ones look like
    /// "ch_3PqK2mLkdIwHu7ix0jQ8mNpZ" - a prefix and an opaque id.
    /// </summary>
    private static string NewProviderReference() =>
        $"sim_{Guid.CreateVersion7():N}";
}
