using System.Linq;
using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable

public enum TickStyle
{
	TimeLabel,
	Major,
	Minor
}

public record struct TimelineTick( TickStyle Style, MovieTime Interval );

partial class Timeline
{
	private const float MinMajorTickWidth = 100f;
	private const float MinMinorTickWidth = 6f;

	private (int FrameRate, float PixelsPerSecond) _tickState;
	private MovieTime _minorTickInterval;
	private MovieTime _majorTickInterval;

	private void UpdateTickIntervals()
	{
		var state = (Session.FrameRate, PixelsPerSecond);

		if ( _tickState == state ) return;

		var baseMinorInterval = MovieTime.FromFrames( 1, state.FrameRate );
		var baseMajorInterval = MovieTime.Max( MovieTime.FromSeconds( 0.1 ), baseMinorInterval );

		_tickState = state;

		_majorTickInterval = TickScales
			.Select( x => x * baseMajorInterval )
			.First( x => x.TotalSeconds * state.PixelsPerSecond >= MinMajorTickWidth );

		_minorTickInterval = TickScales
			.Select( x => x * baseMinorInterval )
			.Where( x => x.TotalSeconds * state.PixelsPerSecond >= MinMinorTickWidth )
			.Where( x => _majorTickInterval.GetFrameIndex( x, out var remainder ) >= 0 && remainder.IsZero )
			.DefaultIfEmpty( _majorTickInterval )
			.First();
	}

	public TimelineTick MajorTick
	{
		get
		{
			UpdateTickIntervals();
			return new TimelineTick( TickStyle.Major, _majorTickInterval );
		}
	}

	public TimelineTick MinorTick
	{
		get
		{
			UpdateTickIntervals();
			return new TimelineTick( TickStyle.Minor, _minorTickInterval );
		}
	}

	public IEnumerable<TimelineTick> Ticks
	{
		get
		{
			UpdateTickIntervals();

			yield return new TimelineTick( TickStyle.TimeLabel, _majorTickInterval );
			yield return new TimelineTick( TickStyle.Major, _majorTickInterval );
			yield return new TimelineTick( TickStyle.Minor, _minorTickInterval );
		}
	}

	private static IEnumerable<int> TickScales
	{
		get
		{
			for ( var i = 1; i <= 1_000_000_000; i *= 10 )
			{
				yield return i;
				yield return i * 2;
				yield return i * 5;
			}
		}
	}
}
