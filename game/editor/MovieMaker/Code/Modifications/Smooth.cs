using System.Linq;
using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable

[MovieModification( "Smoothen", Group = "Recording", Icon = "blur_on" )]
file sealed class SmoothModification() : PerTrackModification<SmoothOptions>( SmoothOptions.Default, true )
{
	public override bool CanStart( TrackListView trackList, TimeSelection selection )
	{
		return trackList.EditablePropertyTracks
			.Select( x => x.Track )
			.OfType<IProjectPropertyTrack>()
			.Where( x => Interpolator.CanInterpolate( x.TargetType ) )
			.Any( x => x.GetBlocks( selection.TotalTimeRange ).Any() );
	}

	public override void AddControls( ToolBarGroup group )
	{
		group.AddSlider( "Smooth Size",
			() => Options.Steps,
			value => Options = Options with { Steps = (int)value },
			minimum: 0,
			maximum: 8,
			step: 1,
			getLabel: () => $"{Options.Size.TotalSeconds:F2}s" );
	}

	protected override ITrackModification<TValue> OnCreateModification<TValue>( IPropertyTrack<TValue> track ) =>
		new SmoothTrackModification<TValue>();
}

file sealed record SmoothOptions( int Steps ) : IModificationOptions
{
	public static SmoothOptions Default => new( 4 );

	public MovieTime Size => Math.Pow( 2d, Steps ) / 32d;
}

file sealed class SmoothTrackModification<T> : ITrackModification<T, SmoothOptions>
{
	public IEnumerable<PropertyBlock<T>> Apply( IReadOnlyList<PropertyBlock<T>> original,
		TimeSelection selection, SmoothOptions options )
	{
		return options.Size > 0 ? original.Select( x => Smooth( x, selection, options ) ) : original;
	}

	private PropertyBlock<T> Smooth( PropertyBlock<T> original, TimeSelection selection, SmoothOptions options )
	{
		var signal = original.Signal;
		var smoothed = signal.Smooth( options.Size );

		return original with { Signal = signal.CrossFade( smoothed, selection ) };
	}
}
