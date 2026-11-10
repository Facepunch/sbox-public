using System.Text;
using System.Text.Json.Serialization;
using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>
/// Describes a translation and scale that can be applied to <see cref="MovieTime"/>s.
/// </summary>
/// <param name="Translation">Time offset to apply.</param>
/// <param name="Scale">Time scale to apply.</param>
public readonly record struct MovieTransform(
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	MovieTime Translation = default,
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	MovieTimeScale Scale = default )
{
	public static MovieTransform Identity => default;

	[JsonIgnore]
	public MovieTransform Inverse => new( Scale.Inverse * -Translation, Scale.Inverse );

	public static MovieTime operator *( MovieTransform transform, MovieTime time ) =>
		time * transform.Scale + transform.Translation;

	public static MovieTimeRange operator *( MovieTransform transform, MovieTimeRange timeRange ) =>
		(transform * timeRange.Start, transform * timeRange.End);

	public static MovieTransform operator *( MovieTransform a, MovieTransform b ) =>
		new( a * b.Translation, a.Scale * b.Scale );
	public static MovieTransform operator *( MovieTimeScale a, MovieTransform b ) =>
		new( a * b.Translation, a * b.Scale );

	public static MovieTransform operator +( MovieTransform transform, MovieTime translation ) =>
		transform with { Translation = transform.Translation + translation };

	public static MovieTransform operator -( MovieTransform transform, MovieTime translation ) =>
		transform with { Translation = transform.Translation - translation };

	public static MovieTransform FromTo( MovieTimeRange from, MovieTimeRange to )
	{
		var scale = MovieTimeScale.FromDurationChange( from.Duration, to.Duration );

		return scale * new MovieTransform( -from.Start ) + to.Start;
	}

	private bool PrintMembers( StringBuilder builder )
	{
		if ( this == Identity )
		{
			builder.Append( "Identity" );
			return true;
		}

		if ( !Translation.IsZero )
		{
			builder.Append( $"{nameof( Translation )} = {Translation}" );
		}

		if ( Scale != MovieTimeScale.Identity )
		{
			if ( !Translation.IsZero )
			{
				builder.Append( ", " );
			}

			builder.Append( $"{nameof( Scale )} = {Scale}" );
		}

		return true;
	}
}
