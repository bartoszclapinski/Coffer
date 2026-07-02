namespace Coffer.Core.Spending;

/// <summary>
/// One merchant's spend within a window (and within a drilled-into category): the positive magnitude of
/// its debits (<see cref="Total"/>, <c>decimal</c>, hard rule #1), its <see cref="Share"/> of the level's
/// total (0..1), and the transaction count. <see cref="Merchant"/> is the raw merchant string, or
/// <c>null</c> for the "unknown merchant" bucket (transactions with no merchant); the presentation layer
/// localises that fallback label.
/// </summary>
public sealed record MerchantSpend(
    string? Merchant,
    decimal Total,
    decimal Share,
    int Count);
