using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox.MovieMaker;

#nullable enable

/// <summary>
/// Represents a segment of time, given by <see cref="Start"/> and <see cref="End"/> times.
/// </summary>
/// <param name="Start">Minimum time in the range.</param>
/// <param name="End">Maximum time in the range.</param>
[JsonConverter( typeof( MovieTimeRangeConverter ) )]
public readonly record struct MovieTimeRange( MovieTime Start, MovieTime End )
{
	public MovieTime Duration => End - Start;
	public MovieTime Center => MovieTime.Lerp( Start, End, 0.5 );

	public bool IsEmpty => Start >= End;

	public MovieTimeRange? Intersect( MovieTimeRange other )
	{
		if ( Start > other.End || End < other.Start ) return null;

		return new MovieTimeRange( MovieTime.Max( Start, other.Start ), MovieTime.Min( End, other.End ) );
	}

	public MovieTimeRange Union( MovieTimeRange? other )
	{
		return other is { } value
			? new MovieTimeRange( MovieTime.Min( Start, value.Start ), MovieTime.Max( End, value.End ) )
			: this;
	}

	public MovieTimeRange Clamp( MovieTimeRange? range )
	{
		return new MovieTimeRange( Start.Clamp( range ), End.Clamp( range ) );
	}

	public MovieTimeRange ClampStart( MovieTime? start )
	{
		return start is { } value
			? new MovieTimeRange( MovieTime.Max( value, Start ), MovieTime.Max( value, End ) )
			: this;
	}

	public MovieTimeRange ClampEnd( MovieTime? end )
	{
		return end is { } value
			? new MovieTimeRange( MovieTime.Min( value, Start ), MovieTime.Min( value, End ) )
			: this;
	}

	public MovieTimeRange Grow( MovieTime startEndDelta ) => Grow( startEndDelta, startEndDelta );

	public MovieTimeRange Grow( MovieTime startDelta, MovieTime endDelta )
	{
		if ( startDelta.IsZero && endDelta.IsZero ) return this;

		var start = Start - startDelta;
		var end = End + endDelta;

		if ( end >= start )
		{
			return new MovieTimeRange( start, end );
		}

		// Work out how far start / end can move before they cross

		var t = (Start - End).Ticks / (double)(startDelta + endDelta).Ticks;

		return MovieTime.Lerp( start, start + startDelta, t );
	}

	public bool Contains( MovieTime time ) => time >= Start && time <= End;
	public bool Contains( MovieTimeRange timeRange ) => timeRange.Start >= Start && timeRange.End <= End;
	public float GetFraction( MovieTime time ) => Duration.GetFraction( time - Start );

	public IEnumerable<MovieTime> GetSampleTimes( int sampleRate ) =>
		GetSampleTimes( Start, Duration.GetFrameCount( sampleRate ), sampleRate );

	public IEnumerable<MovieTime> GetSampleTimes( MovieTime firstSampleTime, int sampleCount, int sampleRate )
	{
		var firstIndex = Math.Max( 0, (Start - firstSampleTime).GetFrameIndex( sampleRate ) );
		var lastIndex = Math.Min( sampleCount, (End - firstSampleTime).GetFrameCount( sampleRate ) );

		return Enumerable.Range( firstIndex, lastIndex - firstIndex )
			.Select( i => firstSampleTime + MovieTime.FromFrames( i, sampleRate ) );
	}

	public override string ToString() => $"[{Start}, {End}]";

	#region Operators

	public static implicit operator MovieTimeRange( MovieTime time ) => new( time, time );

	public static implicit operator MovieTimeRange( (MovieTime, MovieTime) tuple ) => new( tuple.Item1, tuple.Item2 );

	public static MovieTimeRange operator +( MovieTimeRange range, MovieTime offset ) => (range.Start + offset, range.End + offset);
	public static MovieTimeRange operator -( MovieTimeRange range, MovieTime offset ) => (range.Start - offset, range.End - offset);

	#endregion
}

file sealed class MovieTimeRangeConverter : JsonConverter<MovieTimeRange>
{
	private readonly record struct LegacyModel( MovieTime Start, MovieTime End );

	public override MovieTimeRange Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		if ( reader.TokenType == JsonTokenType.StartObject )
		{
			var model = JsonSerializer.Deserialize<LegacyModel>( ref reader, options );

			return new MovieTimeRange( model.Start, model.End );
		}

		var str = JsonSerializer.Deserialize<string>( ref reader, options );

		Assert.NotNull( str );

		var split = str!.IndexOf( ',' );

		Assert.True( split > 0 );

		var startTicks = int.Parse( str.AsSpan( 0, split ) );
		var endTicks = int.Parse( str.AsSpan( split + 1 ) );

		return new MovieTimeRange( MovieTime.FromTicks( startTicks ), MovieTime.FromTicks( endTicks ) );
	}

	public override void Write( Utf8JsonWriter writer, MovieTimeRange value, JsonSerializerOptions options )
	{
		writer.WriteStringValue( $"{value.Start.Ticks},{value.End.Ticks}" );
	}
}
