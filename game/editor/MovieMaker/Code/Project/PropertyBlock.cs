using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;
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
/// A <see cref="ITrackBlock"/> that can change dynamically, usually for previewing edits / live recordings.
/// </summary>
public interface IDynamicBlock : ITrackBlock
{
	event Action<MovieTimeRange>? Changed;
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
		Signal.Compile( TimeRange, track.Project.SampleRate );
}
