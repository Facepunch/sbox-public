namespace Sandbox.Services;

/// <summary>
/// The current player's available AI credits and monthly allowance renewal date.
/// </summary>
public readonly record struct AiCreditBalance
{
	/// <summary>
	/// Remaining monthly and extra credits after deducting debt. Can be negative after an overrun.
	/// </summary>
	public decimal Available { get; init; }

	/// <summary>
	/// Start of the next calendar month in UTC, when the monthly allowance renews.
	/// </summary>
	public DateTimeOffset RenewsAt { get; init; }
}
