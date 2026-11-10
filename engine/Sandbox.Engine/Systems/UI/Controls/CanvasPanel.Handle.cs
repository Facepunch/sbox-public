namespace Sandbox.UI;

public partial class CanvasPanel
{
	/// <summary>
	/// A draggable panel on a canvas. By default it positions itself in canvas coordinates,
	/// retaining its CSS size through zoom. Override the drag methods to edit other data instead.
	/// </summary>
	public class Handle : Panel
	{
		/// <summary>
		/// Directions in which the handle can move.
		/// </summary>
		public enum Axis
		{
			Both,
			Horizontal,
			Vertical
		}

		/// <summary>
		/// The nearest containing canvas.
		/// </summary>
		public CanvasPanel Canvas => Ancestors.OfType<CanvasPanel>().FirstOrDefault();

		/// <summary>
		/// Position in canvas coordinates. Direct canvas children retain their CSS size through zoom.
		/// </summary>
		public Vector2 Position { get; set; }

		/// <summary>
		/// Fraction of the panel size placed over Position. Defaults to its centre.
		/// </summary>
		public Vector2 Anchor { get; set; } = new( 0.5f );

		/// <summary>
		/// Position this panel automatically. Disable for a handle laid out inside another panel, such as a card heading.
		/// </summary>
		public bool Positioned { get; set; } = true;

		/// <summary>
		/// Allow dragging with the left mouse button.
		/// </summary>
		public bool DragEnabled { get; set; } = true;

		/// <summary>
		/// Restrict movement to one axis, or allow both.
		/// </summary>
		public Axis MovementAxis { get; set; }

		/// <summary>
		/// Holding Shift at the start locks to the dominant axis after four logical pixels.
		/// </summary>
		public bool LockAxisWithShift { get; set; } = true;

		/// <summary>
		/// Snap positions to multiples of these increments. Zero disables snapping on that axis.
		/// </summary>
		public Vector2 SnapIncrement { get; set; }

		/// <summary>
		/// Optional limits for the handle's position, in canvas coordinates.
		/// </summary>
		public Rect? MovementBounds { get; set; }

		/// <summary>
		/// Whether this handle currently owns a drag.
		/// </summary>
		public bool IsDragging { get; private set; }

		/// <summary>
		/// Position at the start of the drag, before constraints are applied.
		/// </summary>
		public Vector2 DragOrigin { get; private set; }

		/// <summary>
		/// Current axis restriction, including any Shift lock.
		/// </summary>
		public Axis DragAxis { get; private set; }

		/// <summary>
		/// Raised after a drag starts.
		/// </summary>
		public event Action DragStarted;

		/// <summary>
		/// Reports the total constrained movement in canvas coordinates.
		/// </summary>
		public event Action<Vector2> DragMoved;

		/// <summary>
		/// Raised when the drag ends. True means cancelled; the default implementation restores Position.
		/// </summary>
		public event Action<bool> DragFinished;

		CanvasPanel _dragCanvas;
		Vector2 _mouseStart, _screenStart, _lastMouse;
		bool _lockAxis;

		public Handle()
		{
			AddClass( "canvas-handle" );
			AcceptsFocus = true;
			CanDragScroll = false;
			Style.PointerEvents = PointerEvents.All;
		}

		/// <summary>
		/// Accept a drag after performing item-specific selection or hit testing.
		/// </summary>
		protected virtual bool OnDragStart( MousePanelEvent e ) => true;

		/// <summary>
		/// Apply total movement from DragOrigin. Override to move other panels or edit a data model.
		/// </summary>
		protected virtual void OnDragMove( Vector2 delta ) => Position = DragOrigin + delta;

		/// <summary>
		/// Finish editing, restoring the original position on cancellation.
		/// </summary>
		protected virtual void OnDragFinish( bool cancelled )
		{
			if ( cancelled )
				Position = DragOrigin;
		}

		protected override void OnMouseDown( MousePanelEvent e )
		{
			if ( e.Button != "mouseleft" )
			{
				base.OnMouseDown( e );
				return;
			}
			e.StopPropagation();
			var canvas = Canvas;
			if ( !DragEnabled || IsDragging || canvas is null || canvas.IsPanning || canvas.IsBoxSelecting || !canvas.CanNavigate )
				return;
			Focus();
			if ( !OnDragStart( e ) )
				return;
			_dragCanvas = canvas;
			DragOrigin = Position;
			_mouseStart = canvas.ScreenToCanvas( canvas.MousePosition );
			_screenStart = _lastMouse = canvas.MousePosition;
			DragAxis = MovementAxis;
			_lockAxis = LockAxisWithShift && e.HasShift;
			IsDragging = true;
			DragStarted?.Invoke();
		}

		void MoveDrag()
		{
			if ( _dragCanvas.MousePosition == _lastMouse )
				return;
			_lastMouse = _dragCanvas.MousePosition;
			var delta = _dragCanvas.ScreenToCanvas( _dragCanvas.MousePosition ) - _mouseStart;
			var pixels = _dragCanvas.MousePosition - _screenStart;
			if ( _lockAxis && DragAxis == Axis.Both && pixels.Length > 4 * ScaleToScreen )
				DragAxis = MathF.Abs( pixels.x ) > MathF.Abs( pixels.y ) ? Axis.Horizontal : Axis.Vertical;
			var point = DragOrigin + delta;
			if ( SnapIncrement.x > 0 )
				point.x = MathF.Round( point.x / SnapIncrement.x ) * SnapIncrement.x;
			if ( SnapIncrement.y > 0 )
				point.y = MathF.Round( point.y / SnapIncrement.y ) * SnapIncrement.y;
			if ( MovementBounds is { } bounds )
			{
				point.x = Math.Clamp( point.x, bounds.Left, bounds.Right );
				point.y = Math.Clamp( point.y, bounds.Top, bounds.Bottom );
			}
			delta = point - DragOrigin;
			if ( DragAxis == Axis.Horizontal )
				delta.y = 0;
			else if ( DragAxis == Axis.Vertical )
				delta.x = 0;
			OnDragMove( delta );
			DragMoved?.Invoke( delta );
		}

		/// <summary>
		/// End this handle's drag. Cancellation is also automatic on Escape, focus/capture loss or deletion.
		/// </summary>
		public void FinishDrag( bool cancelled = false )
		{
			if ( !IsDragging )
				return;
			IsDragging = false;
			_dragCanvas = null;
			OnDragFinish( cancelled );
			DragFinished?.Invoke( cancelled );
		}

		protected override void OnMouseMove( MousePanelEvent e )
		{
			if ( !IsDragging )
			{
				base.OnMouseMove( e );
				return;
			}
			e.StopPropagation();
			MoveDrag();
		}

		protected override void OnMouseUp( MousePanelEvent e )
		{
			if ( !IsDragging || e.Button != "mouseleft" )
			{
				base.OnMouseUp( e );
				return;
			}
			e.StopPropagation();
			MoveDrag();
			FinishDrag();
		}

		protected override void OnBlur( PanelEvent e )
		{
			if ( e.Target == this )
				FinishDrag( true );
			base.OnBlur( e );
		}

		public override void OnButtonTyped( ButtonEvent e )
		{
			if ( IsDragging && e.Button == "escape" )
			{
				FinishDrag( true );
				e.StopPropagation = true;
				return;
			}
			base.OnButtonTyped( e );
		}

		public override void Tick()
		{
			base.Tick();
			if ( IsDragging && (!HasActive || !DragEnabled || !IsVisible || Canvas != _dragCanvas) )
				FinishDrag( true );
			if ( Positioned && Canvas is { } canvas && Parent is not null )
			{
				var point = canvas.CanvasToScreen( Position ) + canvas.Box.Rect.Position - Parent.Box.Rect.Position;
				Style.Position = PositionMode.Absolute;
				Style.Left = (point.x - Box.Rect.Width * Anchor.x) / ScaleToScreen;
				Style.Top = (point.y - Box.Rect.Height * Anchor.y) / ScaleToScreen;
			}
		}

		public override void OnDeleted()
		{
			FinishDrag( true );
			base.OnDeleted();
		}
	}
}
