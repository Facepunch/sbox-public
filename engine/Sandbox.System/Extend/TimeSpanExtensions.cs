namespace Sandbox;

public static partial class SandboxSystemExtensions
{
	/// <summary>
	/// Formats a remaining duration as a compact countdown, such as "2d 03h 04m 05s".
	/// Leading zero units are omitted, and zero or negative durations return an empty string.
	/// </summary>
	/// <param name="span">The time remaining, calculated using the caller's clock.</param>
	/// <param name="includeSeconds">
	/// Includes every unit down to seconds. When false, shows days and hours, hours and minutes,
	/// or minutes alone; a positive duration below one minute is shown as "&lt;1m".
	/// </param>
	public static string ToCountdownString( this TimeSpan span, bool includeSeconds = true )
	{
		if ( span <= TimeSpan.Zero ) return "";

		if ( span.TotalDays >= 1 )
		{
			return includeSeconds
				? FormattableString.Invariant( $"{span.Days}d {span.Hours:00}h {span.Minutes:00}m {span.Seconds:00}s" )
				: FormattableString.Invariant( $"{span.Days}d {span.Hours:00}h" );
		}

		if ( span.TotalHours >= 1 )
		{
			return includeSeconds
				? FormattableString.Invariant( $"{span.Hours}h {span.Minutes:00}m {span.Seconds:00}s" )
				: FormattableString.Invariant( $"{span.Hours}h {span.Minutes:00}m" );
		}

		if ( span.TotalMinutes >= 1 )
		{
			return includeSeconds
				? FormattableString.Invariant( $"{span.Minutes}m {span.Seconds:00}s" )
				: FormattableString.Invariant( $"{span.Minutes}m" );
		}

		return includeSeconds ? FormattableString.Invariant( $"{span.Seconds}s" ) : "<1m";
	}

	public static string ToRelativeTimeString( this TimeSpan span )
	{
		if ( span.TotalMinutes < 30 ) return "just now";
		if ( span.TotalHours < 6 ) return "recently";
		if ( span.TotalHours < 24 ) return "today";
		if ( span.TotalHours < 48 ) return "yesterday";
		if ( span.TotalDays < 8 ) return "this week";
		if ( span.TotalDays < 15 ) return "last week";
		if ( span.TotalDays < 30 ) return "this month";
		if ( span.TotalDays < 60 ) return "last month";
		if ( span.TotalDays < 365 ) return "this year";
		if ( span.TotalDays < 365 * 2 ) return "last year";
		return "ages ago";
	}

	public static string ToRemainingTimeString( this TimeSpan span )
	{
		if ( span == TimeSpan.Zero )
			return "";

		if ( span == TimeSpan.MaxValue )
			return "Calculating...";

		if ( span.TotalDays >= 1 )
			return $"{span.Days} day{(span.Days > 1 ? "s" : "")} remaining";
		if ( span.TotalHours >= 1 )
			return $"{span.Hours} hour{(span.Hours > 1 ? "s" : "")} remaining";
		if ( span.TotalMinutes >= 1 )
			return $"{span.Minutes} minute{(span.Minutes > 1 ? "s" : "")} remaining";

		return $"{span.Seconds} second{(span.Seconds > 1 ? "s" : "")} remaining";
	}
}
