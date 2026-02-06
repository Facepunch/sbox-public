using System.Linq;
using System.Reflection;
using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;

namespace Editor.MovieMaker;

#nullable enable

internal static class MovieExtensions
{
	/// <summary>
	/// Gets the <see cref="GameObject"/> that the given property is contained within.
	/// </summary>
	public static GameObject? GetTargetGameObject( this ITrackTarget property )
	{
		while ( property is ITrackProperty memberProperty )
		{
			property = memberProperty.Parent;
		}

		return property switch
		{
			ITrackReference<GameObject> goProperty => goProperty.Value,
			ITrackReference { Value: Component cmp } => cmp.GameObject,
			_ => null
		};
	}

	/// <summary>
	/// Repeats a time range with <paramref name="innerDuration"/> to fill up the given <paramref name="outerRange"/>.
	/// </summary>
	public static IEnumerable<(MovieTimeRange Range, MovieTransform Transform)> Repeat( this MovieTime innerDuration,
		MovieTimeRange outerRange )
	{
		return new MovieTimeRange( 0d, innerDuration ).Repeat( outerRange );
	}

	/// <summary>
	/// Repeats a <paramref name="innerRange"/> to fill up the given <paramref name="outerRange"/>.
	/// </summary>
	public static IEnumerable<(MovieTimeRange Range, MovieTransform Transform)> Repeat( this MovieTimeRange innerRange, MovieTimeRange outerRange )
	{
		if ( !innerRange.Duration.IsPositive )
		{
			// Avoid infinite loops

			yield break;
		}

		var firstOffset = (outerRange.Start - innerRange.Start).GetFrameIndex( innerRange.Duration ) * innerRange.Duration;
		var lastOffset = (outerRange.End - innerRange.Start).GetFrameIndex( innerRange.Duration ) * innerRange.Duration;

		for ( var offset = firstOffset; offset <= lastOffset; offset += innerRange.Duration )
		{
			yield return ((innerRange + offset).Clamp( outerRange ), new MovieTransform( offset ));
		}
	}

	private static MethodInfo ToProjectBlockMethod { get; } = typeof( MovieExtensions )
		.GetMethods( BindingFlags.Static | BindingFlags.Public )
		.First( x => x is { IsGenericMethodDefinition: true, Name: nameof( ToProjectBlock ) } );

	public static IEnumerable<IProjectPropertyBlock> ToProjectBlocks( this IEnumerable<ICompiledPropertyBlock> blocks ) =>
		blocks.Select( x => x.ToProjectBlock() );

	public static IEnumerable<PropertyBlock<T>> ToProjectBlocks<T>( this IEnumerable<ICompiledPropertyBlock<T>> blocks ) =>
		blocks.Select( x => x.ToProjectBlock() );

	public static IProjectPropertyBlock ToProjectBlock( this ICompiledPropertyBlock block )
	{
		return (IProjectPropertyBlock)ToProjectBlockMethod.MakeGenericMethod( block.PropertyType )
			.Invoke( null, [block] )!;
	}

	public static PropertyBlock<T> ToProjectBlock<T>( this ICompiledPropertyBlock<T> block )
	{
		return block switch
		{
			CompiledConstantBlock<T> constantBlock => constantBlock,
			CompiledSampleBlock<T> sampleBlock => sampleBlock,
			_ => throw new NotImplementedException()
		};
	}
}
