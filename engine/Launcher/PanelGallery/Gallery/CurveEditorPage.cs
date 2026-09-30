using Sandbox.UI;

namespace Sandbox.PanelGallery;

/// <summary>
/// Curve and particle-envelope editing, including multiple channels, selection, and differing units.
/// </summary>
public class CurveEditorPage : GalleryPage
{
	public CurveEditorPage() : base( "Curve Editor", "Select and shape curves directly. Shift-click adds keys; drag empty space to box-select. Double-click a curve to insert a key without changing its shape." )
	{
		var row = Case( "Position · X / Y / Z", column: true );
		var xyz = row.AddChild<CurveEditor>();
		xyz.Channels = [
			new( "X", (Color)"#ed7676", new Curve( new Curve.Frame( 0, 0.15f ), new Curve.Frame( 0.4f, 0.85f ), new Curve.Frame( 1, 0.5f ) ) ),
			new( "Y", (Color)"#85ce83", new Curve( new Curve.Frame( 0, 0.7f ), new Curve.Frame( 0.6f, 0.25f ), new Curve.Frame( 1, 0.8f ) ) ),
			new( "Z", (Color)"#73a6f3", new Curve( new Curve.Frame( 0, 0.35f ), new Curve.Frame( 0.3f, 0.5f ), new Curve.Frame( 1, 0.1f ) ) )
		];
		xyz.NavigationBounds = new Rect( 0, 0, 1, 1 );
		var canvas = xyz.Descendants.OfType<CanvasPanel>().Single();
		canvas.ZoomAt( canvas.CanvasToScreen( new( 0.5f ) ), 1.5f );
		xyz.SelectKey( 1 );
		var channelsOutput = Output();
		xyz.ChannelsChanged = channels => channelsOutput.Text = string.Join( " · ", channels.Select( c => $"{c.Name}: {c.Value.Evaluate( 0.5f ):0.###}" ) );
		channelsOutput.Text = "All channels share the graph. Click a curve or key to edit it; Advanced opens the key inspector. Shaded areas are outside the normal value range. Pan vertically or zoom out to explore them; zoom back in to restore the scale.";

		row = Case( "Colour · R / G / B", column: true );
		var rgb = row.AddChild<CurveEditor>();
		rgb.Channels = [
			new( "R", (Color)"#ed7676", Curve.EaseIn ),
			new( "G", (Color)"#85ce83", new Curve( new Curve.Frame( 0, 0.2f ), new Curve.Frame( 0.5f, 0.9f ), new Curve.Frame( 1, 0.3f ) ) ),
			new( "B", (Color)"#73a6f3", new Curve( new Curve.Frame( 0, 0.9f ), new Curve.Frame( 1, 0.1f ) ) )
		];

		row = Case( "Particle lifetime · editable range", column: true );
		var range = row.AddChild<CurveEditor>();
		range.Style.Width = Length.Percent( 100 );
		var a = new Curve( new Curve.Frame( 0, 0.1f ), new Curve.Frame( 0.3f, 0.4f ), new Curve.Frame( 1, 0.05f ) );
		var b = new Curve( new Curve.Frame( 0, 0.3f ), new Curve.Frame( 0.45f, 0.95f ), new Curve.Frame( 1, 0.25f ) );
		a.ValueRange = b.ValueRange = new( 0, 100 );
		range.RangeValue = new CurveRange( a, b );
		range.SelectKeys( [new( 0, 1 )] );
		range.SnapIncrement = new( 0.01f, 1 );

		row = Case( "Animation curve · multi-key editing", column: true );
		var editor = row.AddChild<CurveEditor>();
		editor.Style.Width = Length.Percent( 100 );
		editor.Value = new Curve( new Curve.Frame( 0, 0 ), new Curve.Frame( 0.25f, 0.2f ), new Curve.Frame( 0.65f, 0.8f ), new Curve.Frame( 1, 1 ) );
		editor.SelectKeys( [new( 0, 1 ), new( 0, 2 )] );
		var output = Output();
		editor.ValueChanged = curve => output.Text = $"Animation: {curve.Length} keys · at t=0.5: {curve.Evaluate( 0.5f ):0.###}";
		range.RangeValueChanged = curves => output.Text = $"Particle range at half lifetime: {curves.A.Evaluate( 0.5f ):0.##} – {curves.B.Evaluate( 0.5f ):0.##}";
		output.Text = "Click either boundary to edit it. Edits to both boundaries share one undo history. Ctrl+A selects all; F frames the selection.";

		row = Case( "Different units · independent ranges and stepped boundaries", column: true );
		var units = row.AddChild<CurveEditor>();
		units.Style.Width = Length.Percent( 100 );
		a = new Curve( new Curve.Frame( 0, 0.2f ) { Mode = Curve.HandleMode.Stepped }, new Curve.Frame( 0.35f, 0.6f ) { Mode = Curve.HandleMode.Linear }, new Curve.Frame( 1, 0.1f ) );
		a.TimeRange = new( 0, 5 ); a.ValueRange = new( -10, 10 );
		b = Curve.Ease;
		b.TimeRange = new( 1, 4 ); b.ValueRange = new( 2, 12 );
		units.RangeValue = new CurveRange( a, b ); units.FitView();
	}
}
