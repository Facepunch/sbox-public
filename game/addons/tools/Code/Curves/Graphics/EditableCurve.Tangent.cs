namespace Editor.GraphicsItems;

public partial class EditableCurve
{
	/// <summary>
	/// Represents entry/exit angle of the curve handle
	/// </summary>
	public class Tangent : GraphicsItem
	{
		public float Value { get; set; }

		Handle Handle;
		EditableCurve EditableCurve => Handle.EditableCurve;

		float CurveValueRange => System.MathF.Abs( EditableCurve.ValueRange.y - EditableCurve.ValueRange.x );

		public bool IsIncoming { get; }
		IDisposable dragScope;

		public Tangent( Handle parent, bool incoming, float value ) : base( parent )
		{
			Handle = parent;
			IsIncoming = incoming;

			HoverEvents = true;
			HandlePosition = 0.5f;
			Size = 16.0f;
			Cursor = CursorShape.Finger;
			Movable = false;
			Selectable = false;
			Value = value;

			UpdatePositionFromValue();
		}

		public void UpdatePositionFromValue()
		{
			//
			// Value is a degree between 90 and -90 describing the direction
			// of the tangent. 90 is straight up, -90 is straight down. Here
			// we want to convert that back into vector position so we can set
			// our position based on it.
			//

			var angle = System.MathF.Atan( Value / 2.0f ) + System.MathF.PI * 0.5f;

			var dir = Vector2.FromRadians( angle );

			if ( IsIncoming ) dir.x *= -1.0f;

			dir.y *= -1.0f;

			Position = dir * EditableCurve.HandleDistance;
		}

		protected override void OnMousePressed( GraphicsMouseEvent e )
		{
			base.OnMousePressed( e );

			if ( e.LeftMouseButton )
				dragScope ??= EditableCurve?.History?.Push( "Move Tangent" );

			e.Accepted = true;
			Update();
		}

		protected override void OnMouseReleased( GraphicsMouseEvent e )
		{
			base.OnMouseReleased( e );

			dragScope?.Dispose();
			dragScope = null;

			e.Accepted = true;
			Update();
		}

		protected override void OnMouseMove( GraphicsMouseEvent e )
		{
			base.OnMouseMove( e );

			// Get the direction from the parent handle
			var p = Handle.FromScene( e.ScenePosition );

			// clamp to something sensible so we don't mirror
			if ( !IsIncoming && p.x < 0.01f ) p.x = 0.01f;
			if ( IsIncoming && p.x > -0.01f ) p.x = -0.01f;
			if ( IsIncoming ) p.y *= -1.0f;

			// Get the angle of that, rotate it 90 degrees
			var angle = System.MathF.Atan2( p.x, p.y ) + System.MathF.PI * 0.5f;

			// convert to a tangent
			var tan = System.MathF.Tan( angle );

			// value is the tangent multiplied by 2.0f
			Value = tan * 2.0f;

			UpdatePositionFromValue();
			Handle?.OnTangentChanged( IsIncoming );
			EditableCurve?.OnEdited();
		}

		protected override void OnPaint()
		{
			var size = 5;

			Paint.SetPen( EditableCurve.CurveColor.WithAlpha( 0.1f ), 1 );
			Paint.ClearBrush();

			if ( Handle.Hovered )
			{
				Paint.SetPen( EditableCurve.SelectionColor, 1 );
			}

			if ( Paint.HasMouseOver )
			{
				Paint.SetPen( EditableCurve.SelectionColor, 1 );
				size += 2;
			}


			if ( Paint.HasPressed )
			{
				Paint.SetPen( EditableCurve.SelectionColor, 1 );
				size += 3;
			}

			Paint.DrawRect( new Rect( Size * 0.5f - size * 0.5f, size ) );
		}
	}
}
