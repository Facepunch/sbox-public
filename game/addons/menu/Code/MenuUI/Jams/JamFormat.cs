using System;
using System.Globalization;

namespace MenuProject.MenuUI.Jams;

/// <summary>
/// Dates and countdowns for the jam page, so every surface agrees.
/// </summary>
public static class JamFormat
{
	/// <summary>
	/// "Fri 6 Oct". No time, no timezone.
	/// </summary>
	public static string Day( DateTimeOffset when )
	{
		return when.UtcDateTime.ToString( "ddd d MMM", CultureInfo.InvariantCulture );
	}

	/// <summary>
	/// How long until <paramref name="target"/>, e.g. "12 days, 4 hours". Empty once it has passed.
	/// </summary>
	public static string Until( DateTimeOffset target, DateTimeOffset now )
	{
		if ( target <= now )
			return "";

		var span = target - now;

		if ( span.TotalDays >= 1 )
			return Join( Unit( span.Days, "day" ), Unit( span.Hours, "hour" ) );

		if ( span.TotalHours >= 1 )
			return Join( Unit( span.Hours, "hour" ), Unit( span.Minutes, "minute" ) );

		if ( span.TotalMinutes >= 1 )
			return Unit( span.Minutes, "minute" );

		return "under a minute";
	}

	/// <summary>
	/// Compact countdown, "20d 06h" by default, "20d 06h 14m 07s" when <paramref name="ticking"/>. Empty once passed.
	/// </summary>
	public static string Countdown( DateTimeOffset target, DateTimeOffset now, bool ticking = false )
	{
		if ( target <= now )
			return "";

		var span = target - now;
		var text = span.TotalDays >= 1 ? $"{span.Days}d {span.Hours:00}h" : $"{span.Hours}h";

		return ticking ? $"{text} {span.Minutes:00}m {span.Seconds:00}s" : text;
	}

	/// <summary>
	/// "1st", "2nd", "3rd", "4th".
	/// </summary>
	public static string Ordinal( int n )
	{
		var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch
		{
			1 => "st",
			2 => "nd",
			3 => "rd",
			_ => "th",
		};

		return $"{n}{suffix}";
	}

	static string Unit( int count, string name ) => count == 0 ? null : $"{count} {name}{(count == 1 ? "" : "s")}";

	static string Join( string a, string b ) => b is null ? a : $"{a}, {b}";
}
