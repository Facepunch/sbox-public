using System.Threading;

namespace Sandbox;

/// <summary>
/// One fraction for a whole game load, for the loading screen's bar. Each stage gets a share by how long it usually
/// takes, filled by the stage's own progress when it reports some and eased along by time when it doesn't, so the bar
/// keeps moving through stages that can't say how far they are.
/// </summary>
internal static class LoadProgress
{
	// Seconds a warm load usually spends in each stage. Only the proportions matter.
	static readonly Dictionary<string, double> Expected = new()
	{
		["start"] = 0,
		["teardown"] = 0.1,
		["reset"] = 0.6,
		["fetch"] = 0.3,
		["install"] = 1.5,
		["compile"] = 1,
		["assemblies"] = 0.5,
		["map"] = 1,
		["resources"] = 1,
		["fonts"] = 0.05,
		["scene"] = 2.5,
	};

	const double UnknownStage = 0.3;

	// Only part of some loads: they count towards the whole once they start, so a load without them still fills the bar.
	static readonly HashSet<string> Optional = ["compile", "map"];

	// A stage eased by time gets this close to its end and waits there for the next one.
	const double TimeCap = 0.9;

	static readonly double Always = Expected.Where( x => !Optional.Contains( x.Key ) ).Sum( x => x.Value );
	static double _extra;
	static double Total => Always + _extra;

	static readonly Lock _lock = new();
	static double _done;
	static string _stage;
	static RealTimeSince _since;
	static double? _reported;
	static double _reportedFrom;
	static double _shown;

	/// <summary>
	/// The last load got all the way into the game.
	/// </summary>
	public static bool Completed { get; private set; }

	public static void Complete() => Completed = true;

	static double Of( string stage ) => Expected.TryGetValue( stage, out var s ) ? s : UnknownStage;

	public static void Begin()
	{
		lock ( _lock )
		{
			_done = 0;
			_extra = 0;
			Completed = false;
			_stage = "start";
			_since = 0;
			_reported = null;
			_shown = 0;
		}
	}

	public static void Stage( string name )
	{
		lock ( _lock )
		{
			if ( _stage is not null ) _done += Of( _stage );
			if ( Optional.Contains( name ) || !Expected.ContainsKey( name ) ) _extra += Of( name );
			_stage = name;
			_since = 0;
			_reported = null;
		}
	}

	/// <summary>
	/// How far through its own work the current stage is, 0 to 1. Takes over from the time based guess from wherever
	/// that had got to, so the bar doesn't jump back.
	/// </summary>
	public static void Report( double fraction )
	{
		lock ( _lock )
		{
			if ( _reported is null ) _reportedFrom = ByTime();
			_reported = Math.Clamp( fraction, 0, 1 );
		}
	}

	static double ByTime()
	{
		var expected = Of( _stage ?? "" );
		return expected <= 0 ? 0 : TimeCap * (1 - Math.Exp( -_since / expected ));
	}

	public static double Fraction
	{
		get
		{
			lock ( _lock )
			{
				var stage = _reported is { } r ? _reportedFrom + (1 - _reportedFrom) * r : ByTime();
				var fraction = Math.Clamp( (_done + Of( _stage ?? "" ) * stage) / Total, 0, 1 );

				// Never back: a stage that runs long holds the bar, the next one moves it on.
				_shown = Math.Max( _shown, fraction );
				return _shown;
			}
		}
	}
}
