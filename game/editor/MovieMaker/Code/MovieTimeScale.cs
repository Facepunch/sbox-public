using System.Text.Json;
using System.Text.Json.Serialization;
using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable

[JsonConverter( typeof( MovieTimeScaleConverter ) )]
public readonly struct MovieTimeScale : IEquatable<MovieTimeScale>
{
	public int Cents { get; }

	public double DurationScale => Inverse.FrequencyScale;
	public double FrequencyScale => Cents == 0 ? 1d : Math.Pow( 2d, Cents / 1200d );

	public MovieTimeScale Inverse => FromCents( -Cents );

	private MovieTimeScale( int cents )
	{
		Cents = cents;
	}

	public bool Equals( MovieTimeScale other ) => Cents == other.Cents;
	public override bool Equals( object? obj ) => obj is MovieTimeScale other && Equals( other );
	public override int GetHashCode() => Cents;

	public static MovieTimeScale FromCents( int cents ) => new( cents );
	public static MovieTimeScale FromDurationScale( double scale ) => FromFrequencyScale( scale ).Inverse;
	public static MovieTimeScale FromFrequencyScale( double scale ) => new( (int)Math.Round( Math.Log2( scale ) * 1200 ) );

	public static MovieTimeScale FromDurationChange( MovieTime oldDuration, MovieTime newDuration ) =>
		FromDurationScale( newDuration.TotalSeconds / oldDuration.TotalSeconds );

	public static MovieTimeScale Identity => default;

	public static bool operator ==( MovieTimeScale a, MovieTimeScale b ) => a.Cents == b.Cents;
	public static bool operator !=( MovieTimeScale a, MovieTimeScale b ) => a.Cents != b.Cents;

	public static MovieTime operator *( MovieTime time, MovieTimeScale timeScale ) =>
		timeScale == Identity ? time : MovieTime.FromSeconds( time.TotalSeconds * timeScale.DurationScale );
	public static MovieTime operator *( MovieTimeScale timeScale, MovieTime time ) =>
		timeScale == Identity ? time : MovieTime.FromSeconds( time.TotalSeconds * timeScale.DurationScale );

	public static MovieTime operator /( MovieTime time, MovieTimeScale timeScale ) =>
		timeScale == Identity ? time : MovieTime.FromSeconds( time.TotalSeconds * timeScale.FrequencyScale );

	public static MovieTimeScale operator *( MovieTimeScale a, MovieTimeScale b ) => FromCents( a.Cents + b.Cents );
	public static MovieTimeScale operator /( MovieTimeScale a, MovieTimeScale b ) => FromCents( a.Cents - b.Cents );

	public override string ToString()
	{
		return this == Identity
			? $"{nameof( MovieTimeScale )} {{ {nameof( Identity )} }}"
			: $"{nameof( MovieTimeScale )} {{ {nameof( Cents )} = {Cents} }}";
	}
}

file sealed class MovieTimeScaleConverter : JsonConverter<MovieTimeScale>
{
	public override MovieTimeScale Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options ) =>
		MovieTimeScale.FromCents( JsonSerializer.Deserialize<int>( ref reader, options ) );

	public override void Write( Utf8JsonWriter writer, MovieTimeScale value, JsonSerializerOptions options ) =>
		writer.WriteNumberValue( value.Cents );
}
