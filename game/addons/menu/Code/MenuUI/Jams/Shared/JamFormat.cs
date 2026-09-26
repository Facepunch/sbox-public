using System;
using System.Globalization;
using Sandbox;
using Sandbox.Services;

namespace MenuProject.MenuUI.Jams;

/// <summary>
/// Dates and countdowns for the jam page, so every surface agrees.
/// </summary>
public static class JamFormat
{
	/// <summary>
	/// "Fri 6 Oct". No time, no timezone. The format comes from the language, so it can
	/// drop the weekday where that reads better.
	/// </summary>
	public static string Day( DateTimeOffset when )
	{
		var format = Language.GetPhrase( "jamformat.day_format" );
		if ( format == "jamformat.day_format" ) format = "ddd d MMM";

		return when.UtcDateTime.ToString( format, CultureInfo.InvariantCulture );
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
		if ( span.TotalDays >= 1 )
		{
			return Language.GetPhrase( ticking ? "jamformat.countdown_days_ticking" : "jamformat.countdown_days", new()
			{
				{ "days", span.Days },
				{ "hours", $"{span.Hours:00}" },
				{ "minutes", $"{span.Minutes:00}" },
				{ "seconds", $"{span.Seconds:00}" }
			} );
		}

		return Language.GetPhrase( ticking ? "jamformat.countdown_hours_ticking" : "jamformat.countdown_hours", new()
		{
			{ "hours", span.Hours },
			{ "minutes", $"{span.Minutes:00}" },
			{ "seconds", $"{span.Seconds:00}" }
		} );
	}

	/// <summary>
	/// "1st", "2nd", "3rd", "4th". The suffix is English; a language that doesn't need it can ignore {suffix}.
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

		var str = Language.GetPhrase( "jamformat.ordinal", new() { { "n", n }, { "suffix", suffix } } );
		return str == "jamformat.ordinal" ? $"{n}{suffix}" : str;
	}

	/// <summary>
	/// A step's name in the current language, keyed off the phase so the schedule doesn't
	/// depend on what the backend calls it. Falls back to the backend's own name.
	/// </summary>
	public static string PhaseName( Jam.Step step )
	{
		return step is null ? null : PhaseName( step.Phase ) ?? step.Name;
	}

	/// <summary>
	/// The phase's name in the current language, or null when the language doesn't have one.
	/// </summary>
	public static string PhaseName( Jam.Phase phase )
	{
		var key = $"jamphase.{phase}";
		var str = Language.GetPhrase( key );
		return str == key ? null : str;
	}

	static string Unit( int count, string name ) => count == 0 ? null : $"{count} {name}{(count == 1 ? "" : "s")}";

	static string Join( string a, string b ) => b is null ? a : $"{a}, {b}";
}
