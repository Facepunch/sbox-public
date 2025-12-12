using System.Linq;
using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable

// [MovieModification( "Stretch", Icon = "width_full" )]
file sealed class StretchModification() : PerTrackModification<StretchOptions>( StretchOptions.Default, true )
{
	public override void Start( TrackListView trackList, TimeSelection selection )
	{
		Options = Options with { SourceDuration = selection.TotalTimeRange.Duration };
	}

	protected override ITrackModification<TValue> OnCreateModification<TValue>( IPropertyTrack<TValue> track ) =>
		new StretchTrackModification<TValue>();
}

file sealed record StretchOptions( MovieTime SourceDuration = default ) : IModificationOptions
{
	public static StretchOptions Default => new();
}

file sealed class StretchTrackModification<T> : ITrackModification<T, StretchOptions>
{
	public IEnumerable<PropertyBlock<T>> Apply( IReadOnlyList<PropertyBlock<T>> original,
		TimeSelection selection, StretchOptions options )
	{
		return options.SourceDuration > 0 && options.SourceDuration != selection.TotalTimeRange
			? original.Select( x => Stretch( x, selection, options ) )
			: original;
	}

	private PropertyBlock<T> Stretch( PropertyBlock<T> original, TimeSelection selection, StretchOptions options )
	{
		var signal = original.Signal.SlidingStretch( options.SourceDuration, selection );

		return new PropertyBlock<T>( signal, original.TimeRange );
	}
}
