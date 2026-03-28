using System.Linq;
using System.Text.Json.Serialization;
using Sandbox.Diagnostics;
using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>
/// A <see cref="ITrackBlock"/> that has hints for UI painting.
/// </summary>
public interface IPaintHintBlock : ITrackBlock
{
	/// <summary>
	/// Gets time regions, within <paramref name="timeRange"/>, that have constantly changing values.
	/// </summary>
	IEnumerable<MovieTimeRange> GetPaintHints( MovieTimeRange timeRange );
}

/// <summary>
/// A <see cref="IPropertyBlock"/> that can be added to a <see cref="IProjectPropertyTrack"/>.
/// </summary>
public interface IProjectPropertyBlock : IPropertyBlock, IPaintHintBlock
{
	IProjectPropertyBlock? Slice( MovieTimeRange timeRange );
	IProjectPropertyBlock Shift( MovieTime offset );
	IProjectPropertyBlock WithSignal( PropertySignal signal );

	PropertySignal Signal { get; }
}

public static class PropertyBlock
{
	public static IProjectPropertyBlock FromSignal( PropertySignal signal, MovieTimeRange timeRange )
	{
		var propertyType = signal.PropertyType;
		var blockType = typeof(PropertyBlock<>).MakeGenericType( propertyType );

		return (IProjectPropertyBlock)Activator.CreateInstance( blockType, signal, timeRange )!;
	}
}

public sealed partial record PropertyBlock<T>( [property: JsonPropertyOrder( 100 )] PropertySignal<T> Signal, MovieTimeRange TimeRange )
	: IPropertyBlock<T>, IProjectPropertyBlock
{
	public T GetValue( MovieTime time ) => Signal.GetValue( time.Clamp( TimeRange ) );

	public IEnumerable<MovieTimeRange> GetPaintHints( MovieTimeRange timeRange ) =>
		Signal.GetPaintHints( timeRange.Clamp( TimeRange ) );

	public PropertyBlock<T>? Slice( MovieTimeRange timeRange )
	{
		if ( timeRange == TimeRange ) return this;

		if ( timeRange.Intersect( TimeRange ) is not { } intersection )
		{
			return null;
		}

		return new PropertyBlock<T>( Signal.Reduce( intersection ), intersection );
	}

	IProjectPropertyBlock? IProjectPropertyBlock.Slice( MovieTimeRange timeRange ) => Slice( timeRange );
	IProjectPropertyBlock IProjectPropertyBlock.Shift( MovieTime offset ) => new MovieTransform( offset ) * this;

	public IProjectPropertyBlock WithSignal( PropertySignal signal ) => this with { Signal = (PropertySignal<T>)signal };

	PropertySignal IProjectPropertyBlock.Signal => Signal;

	public IEnumerable<ICompiledPropertyBlock<T>> Compile( ProjectPropertyTrack<T> track ) =>
		Compile( track.Project.SampleRate );

	public IEnumerable<ICompiledPropertyBlock<T>> Compile( int? sampleRate = null )
	{
		var compiled = Signal.Compile( TimeRange, sampleRate ).ToArray();

		Assert.AreEqual( TimeRange.Start, compiled[0].TimeRange.Start, "Compiled signal doesn't start at the expected time." );
		Assert.AreEqual( TimeRange.End, compiled[^1].TimeRange.End, "Compiled signal doesn't end at the expected time." );

		for ( var i = 1; i < compiled.Length; i++ )
		{
			Assert.AreEqual( compiled[i - 1].TimeRange.End, compiled[i].TimeRange.Start, "Compiled signal has non-adjacent blocks." );
		}

		return compiled;
	}
}
