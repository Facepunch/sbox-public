namespace Sandbox.Services;

public static partial class Ai
{
	/// <summary>
	/// The current player's available AI credits and monthly allowance renewal date.
	/// </summary>
	public readonly record struct CreditBalance
	{
		/// <summary>
		/// Remaining monthly and extra credits after deducting debt. Can be negative after an overrun.
		/// </summary>
		public decimal Available { get; init; }

		/// <summary>
		/// Start of the next calendar month in UTC, when the monthly allowance renews.
		/// </summary>
		public DateTimeOffset RenewsAt { get; init; }

		internal CreditBalance( ServiceApi.CreditBalance balance )
		{
			Available = balance.Available;
			RenewsAt = balance.RenewsAt;
		}

		internal static CreditBalance? FromDto( ServiceApi.CreditBalance? balance )
			=> balance is { } value ? new CreditBalance( value ) : null;
	}
}
