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
		var blockType = typeof( PropertyBlock<> ).MakeGenericType( propertyType );

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

	/// <summary>
	/// Tries to reduce this block's <see cref="Signal"/> based on its <see cref="TimeRange"/>.
	/// Returns a new block with the reduced signal if any reduction was possible, otherwise returns this block.
	/// </summary>
	public PropertyBlock<T> Reduce()
	{
		var reducedSignal = Signal.Reduce( TimeRange );

		return !reducedSignal.Equals( Signal )
			? this with { Signal = reducedSignal }
			: this;
	}

	/// <summary>
	/// We can merge adjacent blocks with identical values at the interface,
	/// or that both have keyframes at the interface.
	/// </summary>
	public bool CanMerge( PropertyBlock<T> next )
	{
		if ( TimeRange.End != next.TimeRange.Start ) return false;

		var connectionTime = TimeRange.End;

		var prevValue = GetValue( connectionTime );
		var nextValue = next.GetValue( connectionTime );

		if ( EqualityComparer<T>.Default.Equals( prevValue, nextValue ) ) return true;

		if ( Signal is not KeyframeSignal<T> prevKeyframeSignal ) return false;
		if ( next.Signal is not KeyframeSignal<T> nextKeyframeSignal ) return false;

		if ( prevKeyframeSignal.Keyframes.All( x => x.Time != connectionTime ) ) return false;
		if ( nextKeyframeSignal.Keyframes.All( x => x.Time != connectionTime ) ) return false;

		return true;
	}
}

public static class BlockExtensions
{
	extension<T>( List<PropertyBlock<T>> blocks )
	{
		/// <summary>
		/// Merge all adjacent blocks that satisfy <see cref="PropertyBlock{T}.CanMerge"/>.
		/// </summary>
		public void Merge()
		{
			for ( var i = blocks.Count - 2; i >= 0; --i )
			{
				var prev = blocks[i];
				var next = blocks[i + 1];

				if ( !prev.CanMerge( next ) ) continue;

				var combinedTimeRange = prev.TimeRange.Union( next.TimeRange );
				var combinedSignal = prev.Signal.HardCut( next.Signal, prev.TimeRange.End ).Reduce( combinedTimeRange );

				blocks[i] = new PropertyBlock<T>( combinedSignal, combinedTimeRange );
				blocks.RemoveAt( i + 1 );
			}
		}
	}
}
