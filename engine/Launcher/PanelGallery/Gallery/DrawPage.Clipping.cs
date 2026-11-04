namespace Sandbox.PanelGallery;

public partial class DrawPage
{
	void PerspectiveClipExamples()
	{
		Add.Label( "Clips follow CSS transforms. Compare a rectangle with a larger surface clipped to the same bounds; both should fill the cyan guide.", "page-blurb" );
		ClipPair( "Affine clipping", "none" );
		ClipPair( "Perspective clipping", "perspective(300px) rotateY(-45deg)" );
	}

	void ClipPair( string title, string transform )
	{
		var row = Case( title );
		for ( int i = 0; i < 2; i++ )
		{
			var column = row.Add.Panel();
			column.Style.Set( "width: 360px; flex-shrink: 0; flex-direction: column; gap: 12px;" );
			column.Add.Label( i == 0 ? "Rectangle" : "Clipped oversized rectangle" );
			var stage = column.Add.Panel();
			stage.Style.Set( "position: relative; width: 350px; height: 240px; background-color: #101820; overflow: visible;" );
			var sample = new ClipSample( i == 1 );
			sample.Style.Set( $"position: absolute; left: 20px; top: 20px; width: 300px; height: 160px; transform-origin: 0% 0%; transform: {transform};" );
			stage.AddChild( sample );
		}
	}

	sealed class ClipSample( bool clipped ) : Panel
	{
		public override void OnDraw( Painter painter )
		{
			var clip = new Rect( 0, 0, 140, 140 );
			using ( painter.Scope() )
			{
				painter.Fill = Color.FromBytes( 255, 135, 35 );
				if ( clipped ) painter.Clip( clip );
				painter.Rect( clipped ? new Rect( 0, 0, 300, 160 ) : clip );
			}
			painter.Fill = Fill.None;
			painter.Stroke = Stroke.Solid( Color.Cyan, 2 );
			painter.Rect( clip );
		}
	}
}
