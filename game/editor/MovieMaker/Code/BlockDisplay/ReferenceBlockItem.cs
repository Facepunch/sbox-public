using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Properties;

namespace Editor.MovieMaker.BlockDisplays;

#nullable enable

public sealed class ReferenceBlockItem<T> : PropertyBlockItem<BindingReference<T>>
	where T : class, IValid
{
	protected override Color BackgroundColor => Theme.Green.Darken( 0.5f ).WithAlpha( 0.75f );

	protected override void OnPaint()
	{
		base.OnPaint();

		if ( Block.GetValue( Block.TimeRange.Start ).TrackId is { } trackId )
		{
			var track = Track.Project.GetTrack( trackId ) as IReferenceTrack;

			PaintText( Block.TimeRange, track?.GetPathString() ?? "unknown" );
		}
		else
		{
			PaintText( Block.TimeRange, "null" );
		}
	}
}
