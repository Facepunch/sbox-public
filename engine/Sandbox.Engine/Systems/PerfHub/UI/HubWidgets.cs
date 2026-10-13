using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// One of the <see cref="DebugOverlay"/> drawers, hosted in a panel. The drawers lay themselves out from a position and say
/// how far down they got, so the panel takes that as its height for the next frame.
/// </summary>
internal sealed class HubOverlayView : Panel
{
	internal delegate void DrawOverlay( Painter painter, ref Vector2 position );

	readonly DrawOverlay _draw;
	float _height = 40f;

	public HubOverlayView( DrawOverlay draw )
	{
		_draw = draw;
		AddClass( "overlay-view" );
		Style.Height = _height;
	}

	public override void OnDraw( Painter painter )
	{
		var scale = ScaleToScreen;

		using ( var state = painter.Scope() )
		{
			painter.Scale( scale );

			var pos = Vector2.Zero;
			_draw( painter, ref pos );

			var height = MathF.Max( pos.y, 20f );
			if ( MathF.Abs( height - _height ) > 0.5f )
			{
				_height = height;
				Style.Height = _height;
			}
		}
	}
}
