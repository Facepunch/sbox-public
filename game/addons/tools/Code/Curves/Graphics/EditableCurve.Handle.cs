namespace Editor.GraphicsItems;

/// <summary>
/// Represents a keyframe on an EditableCurve
/// </summary>
public partial class EditableCurve
{
	public class Handle : GraphicsItem
	{
		public Curve.Frame Frame;
		public EditableCurve EditableCurve;
		public Tangent In;
		public Tangent Out;
		public Vector2 RealPosition;

		public Handle( EditableCurve parent, in Curve.Frame k ) : base( parent )
		{
			Frame = k;
			EditableCurve = parent;

			HoverEvents = true;
			HandlePosition = 0.5f;
			Size = 32.0f;
			Cursor = CursorShape.Finger;
			Movable = true;
			Focusable = true;
			Selectable = true;

			In = new Tangent( this, true, k.In );
			Out = new Tangent( this, false, k.Out );

			UpdatePositionFromValue();
		}

		internal void UpdatePositionFromValue()
		{
			var val = new Vector2( Frame.Time, Frame.Value );
			Position = ValueToPosition( val );
		}

		Vector2 ValueToPosition( Vector2 pos )
		{
			var x = pos.x.Remap( 0, 1, EditableCurve.TimeRange.x, EditableCurve.TimeRange.y, false );
			var y = pos.y.Remap( 0, 1, EditableCurve.ValueRange.x, EditableCurve.ValueRange.y, false );
			var drawnX = x.Remap( EditableCurve.ViewportRangeX.x, EditableCurve.ViewportRangeX.y, 0, 1, false );
			var drawnY = y.Remap( EditableCurve.ViewportRangeY.x, EditableCurve.ViewportRangeY.y, 0, 1, false );
			RealPosition = new Vector2( drawnX, 1f - drawnY ) * EditableCurve.Size;
			return RealPosition;
		}

		Vector2 PositionToValue( Vector2 pos )
		{
			// Map the position to the viewport range
			var x = pos.x.Remap( 0, EditableCurve.Size.x, EditableCurve.ViewportRangeX.x, EditableCurve.ViewportRangeX.y, false );
			var y = pos.y.Remap( 0, EditableCurve.Size.y, EditableCurve.ViewportRangeY.y, EditableCurve.ViewportRangeY.x, false );
			// Now remap the viewport range to the actual range
			x = x.Remap( EditableCurve.TimeRange.x, EditableCurve.TimeRange.y, 0, 1, false );
			y = y.Remap( EditableCurve.ValueRange.x, EditableCurve.ValueRange.y, 0, 1, false );
			return new Vector2( x, y );
		}

		/// <summary>
		/// Set the value from range scaled values
		/// </summary>
		public void SetValue( float x, float y )
		{
			x = x.Remap( EditableCurve.TimeRange.x, EditableCurve.TimeRange.y, 0, 1, false );
			y = y.Remap( EditableCurve.ValueRange.x, EditableCurve.ValueRange.y, 0, 1, false );

			Position = ValueToPosition( new Vector2( x, y ) );
			EditableCurve?.OnHandleMoved();
			OnMoved();
		}

		/// <summary>
		/// Convert this point into a keyframe, appropriate for saving into a curve
		/// </summary>
		internal Curve.Frame GetKeyFrame()
		{
			var kf = new Curve.Frame();

			var value = PositionToValue( RealPosition );

			kf.Time = value.x;
			kf.Value = value.y;

			kf.In = In?.Value ?? 0;
			kf.Out = Out?.Value ?? 0;

			kf.Mode = Frame.Mode;

			return kf;
		}

		protected override void OnPaint()
		{
			if ( !EditableCurve.BoundingRect.IsInside( Position ) )
				return;

			var size = 4.0f;

			Paint.SetBrush( EditableCurve.CurveColor );
			Paint.ClearPen();

			if ( Paint.HasMouseOver ) // todo - don't do this if over a child and not us
			{
				Paint.SetPen( EditableCurve.SelectionColor, 2 );
				size += 2;
			}

			if ( Paint.HasSelected )
			{
				Paint.SetBrush( EditableCurve.SelectionColor );
				size += 3;
			}

			Paint.DrawCircle( Size * 0.5f, size );
		}

		/// <summary>
		/// Paint extra background stuff
		/// </summary>
		public void PaintExtras()
		{
			if ( Hovered )
			{
				Paint.ClearBrush();

				if ( (In?.Hovered ?? false) || (Out?.Hovered ?? false) )
				{
					Paint.SetPen( EditableCurve.CurveColor.WithAlpha( 0.1f ), 1, PenStyle.Dot );
					Paint.DrawCircle( 0, EditableCurve.HandleDistance * 2.0f );
				}

				var maxLineSize = EditableCurve.Size.Length;

				// Guide Lines
				Paint.SetPen( EditableCurve.CurveColor.WithAlpha( 0.3f ), 1, PenStyle.Dot );
				//Paint.DrawLine( TangentIn.Position * maxLineSize, TangentIn.Position );
				//Paint.DrawLine( TangentOut.Position * maxLineSize, TangentOut.Position );
				Paint.DrawLine( new Vector2( -maxLineSize, 0 ), new Vector2( maxLineSize, 0 ) );

			}

			Paint.SetPen( Hovered ? EditableCurve.SelectionColor.WithAlpha( 0.5f ) : EditableCurve.CurveColor.WithAlpha( 0.1f ), 1, PenStyle.Dot );
			Paint.ClearBrush();

			if ( In.IsValid() && Out.IsValid() )
			{
				Paint.DrawLine( 0, In.Position );
				Paint.DrawLine( 0, Out.Position );
			}

			if ( Hovered )
			{
				//Paint.RenderMode = RenderMode.HardLight;
				//Paint.ClearPen();

				//Paint.SetBrushRadial( 0, 100, Curve.CurveColor.WithAlpha( 0.2f ), Curve.CurveColor.WithAlpha( 0 ) );
				//Paint.DrawCircle( 0, 200 );
			}
		}

		protected override void OnMoved()
		{
			// An active drag owns its handles - it applies values and snapping for the group
			if ( EditableCurve?.editor?.Drag.IsDragging( this ) ?? false )
				return;

			if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
			{
				Position = SnapToGridPosition( Position );
			}

			UpdateValueFromPosition();
			EditableCurve?.OnHandleMoved();
		}

		/// <summary>
		/// Snap a chart position to the editor's background grid
		/// </summary>
		internal Vector2 SnapToGridPosition( Vector2 position )
		{
			var xInc = EditableCurve.editor.GetBackgroundIncrementX();
			var yInc = EditableCurve.editor.GetBackgroundIncrementY();
			var value = PositionToValue( position );
			value.x = value.x
					.Remap( 0, 1, EditableCurve.TimeRange.x, EditableCurve.TimeRange.y, false )
					.SnapToGrid( xInc )
					.Remap( EditableCurve.TimeRange.x, EditableCurve.TimeRange.y, 0, 1, false );
			value.y = value.y
					.Remap( 0, 1, EditableCurve.ValueRange.x, EditableCurve.ValueRange.y, false )
					.SnapToGrid( yInc )
					.Remap( EditableCurve.ValueRange.x, EditableCurve.ValueRange.y, 0, 1, false );
			return ValueToPosition( value );
		}

		/// <summary>
		/// Update this handle's keyframe to match where it is on the chart
		/// </summary>
		internal void UpdateValueFromPosition()
		{
			RealPosition = Position;
			var value = PositionToValue( Position );
			Frame.Time = value.x;
			Frame.Value = value.y;
		}

		protected override void OnPositionChanged()
		{
			base.OnPositionChanged();

			// Dragged handles are limited as a group, so the selection keeps its shape
			if ( !(EditableCurve?.editor?.Drag.Moved( this ) ?? false) )
			{
				Position = Position.Clamp( 0, EditableCurve.Size );
			}

			In?.UpdatePositionFromValue();
			Out?.UpdatePositionFromValue();
		}

		protected override void OnHoverEnter( GraphicsHoverEvent e )
		{
			EditableCurve?.Update();
			EditableCurve?.UpdateTooltip();
		}
		protected override void OnHoverLeave( GraphicsHoverEvent e )
		{
			EditableCurve?.Update();
			EditableCurve?.UpdateTooltip();
		}

		protected override void OnMousePressed( GraphicsMouseEvent e )
		{
			base.OnMousePressed( e );

			if ( e.LeftMouseButton )
				EditableCurve?.editor?.Drag.Begin( this );

			if ( e.RightMouseButton )
				OpenContextMenu( e.ScreenPosition );
		}

		protected override void OnMouseReleased( GraphicsMouseEvent e )
		{
			base.OnMouseReleased( e );

			EditableCurve?.editor?.Drag.End();
		}

		public virtual void OpenContextMenu( Vector2 pos )
		{
			var m = new ContextMenu( null );

			var enums = DisplayInfo.ForEnumValues( typeof( Curve.HandleMode ) );
			var values = System.Enum.GetValues<Curve.HandleMode>();

			foreach ( var e in DisplayInfo.ForEnumValues<Curve.HandleMode>() )
			{
				var o = m.AddOption( e.info.Name, e.info.Icon, () => SetHandleMode( e.value ) );
				o.Enabled = Frame.Mode != e.value;
			}

			m.AddSeparator();
			m.AddOption( "Delete", "delete", DeleteHandle );

			m.OpenAt( pos, false );
		}

		public void SetHandleMode( Curve.HandleMode mode )
		{
			if ( Frame.Mode == mode )
				return;

			Frame.Mode = mode;
			OnHandleModeChanged();
			EditableCurve.SaveCurve();
			EditableCurve.UpdateTooltip();
		}

		public void OnHandleModeChanged()
		{
			In ??= new Tangent( this, true, Frame.In );
			Out ??= new Tangent( this, false, Frame.Out );

			OnTangentChanged( true );

			if ( Frame.Mode == Curve.HandleMode.Mirrored ) return;
			if ( Frame.Mode == Curve.HandleMode.Split ) return;

			In?.Destroy();
			In = null;

			Out?.Destroy();
			Out = null;
		}

		public void DeleteHandle()
		{
			EditableCurve.Delete( this );
		}

		protected override void OnKeyPress( KeyEvent e )
		{
			base.OnKeyPress( e );

			// Forward keypresses onto the curve,
			// so it can apply them to all selected
			EditableCurve?.FocusedHandleKeyPress( e );
		}

		internal void OnTangentChanged( bool isIncoming )
		{
			var i = In;
			var o = Out;

			if ( !isIncoming )
			{
				i = o;
				o = In;
			}

			if ( Frame.Mode == Curve.HandleMode.Mirrored )
			{
				o.Value = -i.Value;
				o.UpdatePositionFromValue();
			}
		}

		protected override void OnSelectionChanged()
		{
			base.OnSelectionChanged();

			EditableCurve?.HandleSelectionChanged();
		}
	}

}
