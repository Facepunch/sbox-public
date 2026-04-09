using System;

namespace Editor
{
	public partial class Widget : QObject
	{
		#region mouse events


		static Widget _currentPressedWidget;
		internal static Widget CurrentlyPressedWidget
		{
			get => _currentPressedWidget;
			set
			{
				if ( _currentPressedWidget == value ) return;

				if ( _currentPressedWidget.IsValid() ) _currentPressedWidget.Update();
				_currentPressedWidget = value;
				if ( _currentPressedWidget.IsValid() ) _currentPressedWidget.Update();
			}
		}

		/// <summary>
		/// Whether this widget is currently being pressed down or not.
		/// </summary>
		public bool IsPressed => CurrentlyPressedWidget == this;

		internal void InternalWheelEvent( QWheelEvent e ) => OnMouseWheel( new WheelEvent( e ) );

		/// <summary>
		/// Mouse wheel was scrolled while the mouse cursor was over this widget.
		/// </summary>
		protected virtual void OnMouseWheel( WheelEvent e )
		{
#pragma warning disable CS0618 // Type or member is obsolete
			OnWheel( e );
#pragma warning restore CS0618 // Type or member is obsolete
		}

		/// <summary>
		/// Mouse wheel was scrolled while the mouse cursor was over this widget.
		/// </summary>
		[Obsolete( $"Use {nameof( OnMouseWheel )}" )]
		protected virtual void OnWheel( WheelEvent e )
		{

		}

		bool canClick = false;
		bool canRightClick = false;
		Vector2 startClickPos;
		Vector2 startRightClickPos;

		internal bool InternalMouseReleaseEvent( QMouseEvent e )
		{
			supressBaseAction = true;
			var me = new MouseEvent( e );
			OnMouseReleased( me );
			return supressBaseAction;
		}

		/// <inheritdoc cref="OnMouseReleased"/>
		public Action MouseRelease;

		/// <summary>
		/// Called when mouse is released over this widget.
		/// </summary>
		protected virtual void OnMouseReleased( MouseEvent e )
		{
			supressBaseAction = false;
			MouseRelease?.Invoke();

			if ( e.LeftMouseButton && canClick )
			{
				var hitPos = e.LocalPosition;

				// did we end the click in the widget?
				if ( !LocalRect.IsInside( hitPos ) )
					return;

				// did we move significantly during the click?
				if ( startClickPos.Distance( hitPos ) > 6.0f )
					return;

				if ( _isDragStart )
					return;

				InternalMouseClickEvent( e );
			}

			if ( e.RightMouseButton && canRightClick )
			{
				var hitPos = e.LocalPosition;

				// did we end the click in the widget?
				if ( !LocalRect.IsInside( hitPos ) )
					return;

				// did we move significantly during the click?
				if ( startRightClickPos.Distance( hitPos ) > 6.0f )
					return;

				OnMouseRightClick( e );
			}
		}

		/// <inheritdoc cref="OnMouseClick"/>
		public Action MouseClick;

		internal void InternalMouseClickEvent( MouseEvent e )
		{
			Application.OnWidgetClicked?.Invoke( this, e );
			if ( e.Accepted ) return;

			OnMouseClick( e );
		}

		/// <summary>
		/// Called when this widget is left clicked (on mouse release).
		/// </summary>
		protected virtual void OnMouseClick( MouseEvent e )
		{
			MouseClick?.Invoke();
		}

		/// <inheritdoc cref="OnMouseRightClick"/>
		public Action MouseRightClick;

		/// <summary>
		/// Called when this widget is right clicked (on mouse release).
		/// </summary>
		protected virtual void OnMouseRightClick( MouseEvent e )
		{
			MouseRightClick?.Invoke();
		}

		bool supressBaseAction;

		internal bool InternalMousePressEvent( QMouseEvent e )
		{
			supressBaseAction = true;
			var me = new MouseEvent( e );

			if ( me.LeftMouseButton )
				CurrentlyPressedWidget = this;

			if ( me.LeftMouseButton )
			{
				canClick = !me.Accepted;
				startClickPos = me.LocalPosition;
				_isDragStart = false;
			}

			if ( me.RightMouseButton )
			{
				canRightClick = !me.Accepted;
				startRightClickPos = me.LocalPosition;
			}

			OnMousePress( me );
			return supressBaseAction;
		}

		/// <summary>
		/// Called when this widget is left clicked (on mouse press).
		/// </summary>
		public Action MouseLeftPress;

		/// <summary>
		/// Called when this widget is right clicked (on mouse press).
		/// </summary>
		public Action MouseRightPress;

		/// <summary>
		/// Called when this widget is clicked with the mouse wheel (on mouse press).
		/// </summary>
		public Action MouseMiddlePress;

		/// <summary>
		/// Called when mouse is pressed over this widget.
		/// </summary>
		protected virtual void OnMousePress( MouseEvent e )
		{
			if ( e.LeftMouseButton && MouseLeftPress != null )
			{
				e.Accepted = true;
				MouseLeftPress?.Invoke();
			}

			if ( e.RightMouseButton && MouseRightPress != null )
			{
				e.Accepted = true;
				MouseRightPress?.Invoke();
			}

			if ( e.MiddleMouseButton && MouseMiddlePress != null )
			{
				e.Accepted = true;
				MouseMiddlePress?.Invoke();
			}

			supressBaseAction = false;
		}

		/// <summary>
		/// Whether this widget can be drag and dropped onto other widgets.
		/// </summary>
		public bool IsDraggable { get; set; }

		internal void InternalMouseMoveEvent( QMouseEvent e )
		{
			var me = new MouseEvent( e );
			OnMouseMove( me );
		}

		/// <inheritdoc cref="OnMouseMove"/>
		public Action<Vector2> MouseMove;

		/// <summary>
		/// Protect against calling OnDragStart multiple times
		/// </summary>
		bool _isDragStart;

		/// <summary>
		/// Called when the mouse cursor is moved while being over this widget.
		/// </summary>
		protected virtual void OnMouseMove( MouseEvent e )
		{
			if ( IsDraggable && startClickPos.Distance( e.LocalPosition ) > 20 && (e.ButtonState & MouseButtons.Left) != 0 && !_isDragStart )
			{
				_isDragStart = true;
				OnDragStart();
			}

			MouseMove?.Invoke( e.LocalPosition );
		}

		/// <summary>
		/// Mouse cursor entered the bounds of this widget.
		/// </summary>
		protected virtual void OnMouseEnter()
		{
			Update();
		}
		internal void InternalMouseEnterEvent( QMouseEvent e ) => OnMouseEnter();

		/// <summary>
		/// Mouse cursor exited the bounds of this widget.
		/// </summary>
		protected virtual void OnMouseLeave()
		{
			Update();
		}
		internal void InternalMouseLeaveEvent( QMouseEvent e ) => OnMouseLeave();

		/// <summary>
		/// Called after <see cref="OnMouseRightClick"/>, for the purposes of opening a context menu.
		/// </summary>
		protected virtual void OnContextMenu( ContextMenuEvent e )
		{
		}
		internal void InternalContextMenuEvent( QContextMenuEvent e ) => OnContextMenu( new ContextMenuEvent { ptr = e } );

		/// <summary>
		/// Called when the widget was double clicked with any mouse button.
		/// </summary>
		/// <param name="e"></param>
		protected virtual void OnDoubleClick( MouseEvent e )
		{
			e.IsDoubleClick = true;
			OnMousePress( e );
		}
		internal void InternalMouseDoubleClickEvent( QMouseEvent e ) => OnDoubleClick( new MouseEvent( e ) { IsDoubleClick = true } );

		#endregion

		#region keyboard events

		/// <summary>
		/// A key has been pressed. Your widget needs keyboard focus for this to be called - see FocusMode.
		/// </summary>
		protected virtual void OnKeyPress( KeyEvent e )
		{
			if ( ProvidesDebugMode && e.HasAlt && e.Key == KeyCode.Pause )
			{
				DebugModeEnabled = !DebugModeEnabled;
			}
		}
		internal void InternalKeyPressEvent( QKeyEvent e ) => OnKeyPress( new KeyEvent( e ) );

		/// <summary>
		/// A key has been released.
		/// </summary>
		protected virtual void OnKeyRelease( KeyEvent e )
		{

		}
		internal void InternalKeyReleaseEvent( QKeyEvent e ) => OnKeyRelease( new KeyEvent( e ) );

		/// <summary>
		/// A shortcut has been activated. This is called on the focused control so they can override it.
		/// </summary>
		protected virtual void OnShortcutPressed( KeyEvent e )
		{

		}
		internal void InternalShortcutOverrideEvent( QKeyEvent e ) => OnShortcutPressed( new KeyEvent( e ) );

		/// <summary>
		/// Called when the widget gains keyboard focus.
		/// </summary>
		internal event Action<FocusChangeReason> Focused;

		/// <summary>
		/// Called when the widget gains keyboard focus.
		/// </summary>
		protected virtual void OnFocus( FocusChangeReason reason )
		{
			Focused?.Invoke( reason );
		}
		internal void InternalFocusInEvent( FocusChangeReason reason ) => OnFocus( reason );

		/// <summary>
		/// Called when the widget loses keyboard focus.
		/// </summary>
		internal event Action<FocusChangeReason> Blurred;

		/// <summary>
		/// Called when the widget loses keyboard focus.
		/// </summary>
		protected virtual void OnBlur( FocusChangeReason reason )
		{
			Blurred?.Invoke( reason );
		}
		internal void InternalFocusOutEvent( FocusChangeReason reason ) => OnBlur( reason );

		#endregion

		/// <summary>
		/// Called when the widgets' size was changed.
		/// </summary>
		protected virtual void OnResize()
		{

		}
		internal void InternalOnResizeEvent( QResizeEvent e ) => OnResize();

		/// <summary>
		/// Called when the widget is moved to a new position relative to it's parent.
		/// </summary>
		public event Action Moved;

		/// <summary>
		/// Called when the widget was moved to a new position relative to it's parent.
		/// </summary>
		protected virtual void OnMoved()
		{
			Moved?.Invoke();
		}
		internal void InternalOnMoveEvent( QMoveEvent e ) => OnMoved();

		bool skipDraw = false;

		/// <summary>
		/// Called from Widget paint event. If e is null then we're drawing manually
		/// which usually means directx is expected to draw a frame.
		/// </summary>
		internal bool InternalPaintEvent( QPainter e, int flags )
		{
			if ( !e.IsValid )
				return false;

			skipDraw = true;

			StateFlag f = (StateFlag)flags;

			if ( IsPressed ) f |= StateFlag.Sunken;

			using ( Paint.Start( e, f, DpiScale ) )
			{
				Paint.LocalRect = LocalRect;

				if ( OnPaintOverride is not null )
				{
					if ( OnPaintOverride() )
					{
						return skipDraw;
					}
				}

				OnPaint();
			}

			return skipDraw;
		}

		/// <summary>
		/// Override the widget's paint process.
		///
		/// Return <see langword="true"/> to prevent the default paint action, which is to call <see cref="OnPaint"/>.
		/// </summary>
		public Func<bool> OnPaintOverride;

		/// <summary>
		/// Override to custom paint your widget, for example using <see cref="Paint"/>. Can be overwritten by <see cref="OnPaintOverride" />.
		/// </summary>
		protected virtual void OnPaint()
		{
			skipDraw = false;
		}

		internal bool InternalCloseEvent( QEvent e )
		{
			if ( !OnClose() )
			{
				e.ignore();
				return true;
			}

			OnClosed();

			return false;
		}

		/// <summary>
		/// Called when a window is about to be closed.
		/// </summary>
		protected virtual bool OnClose()
		{
			return true;
		}

		/// <summary>
		/// Called when a window is closed.
		/// </summary>
		protected virtual void OnClosed()
		{

		}

		bool _recurseBreaker = false;

		internal void InternalOnEvent( Native.EventType e )
		{
			// Unused for now

			if ( e == Native.EventType.LayoutRequest || e == Native.EventType.Resize )
			{
				if ( _recurseBreaker )
					return;

				try
				{
					_recurseBreaker = true;
					DoLayout();
				}
				finally
				{
					_recurseBreaker = false;
				}
			}

			if ( e == Native.EventType.Show )
			{
				OnVisibilityChanged( true );
			}

			if ( e == Native.EventType.Hide )
			{
				OnVisibilityChanged( false );
			}
		}

		internal bool InternalFocusNextPrevChild( bool next )
		{
			if ( next ) return FocusNext();
			return FocusPrevious();
		}

		/// <summary>
		/// Called when the visibility of this widget changes.
		/// </summary>
		public event Action<bool> VisibilityChanged;

		/// <summary>
		/// Called when the visibility of this widget changes.
		/// </summary>
		protected virtual void OnVisibilityChanged( bool visible )
		{
			VisibilityChanged?.Invoke( visible );
		}

		/// <summary>
		/// Called when Tab is pressed to find the next widget to focus.
		/// Return true to prevent focusing.
		/// </summary>
		protected virtual bool FocusNext()
		{
			return false;
		}

		/// <summary>
		/// Called when Shift + Tab is pressed to find the next widget to focus.
		/// Return true to prevent focusing.
		/// </summary>
		protected virtual bool FocusPrevious()
		{
			return false;
		}

		/// <summary>
		/// Called to make sure all child panels are in correct positions and have correct sizes.
		/// This is typically called when the size of this widget changes, but there are other cases as well.
		/// </summary>
		protected virtual void DoLayout()
		{

		}

		internal Vector3 InternalSizeHint( int w, int h )
		{
			_baseSizeHint.x = w;
			_baseSizeHint.y = h;

			return SizeHint();
		}

		internal Vector3 InternalMinimumSizeHint( int w, int h )
		{
			_baseMiniumSizeHint.x = w;
			_baseMiniumSizeHint.y = h;

			var s = MinimumSizeHint();

			// enforce limits
			if ( MinimumWidth > 0 ) s.x = MathF.Max( s.x, MinimumWidth );
			if ( MinimumHeight > 0 ) s.y = MathF.Max( s.y, MinimumHeight );
			if ( MaximumHeight > 0 ) s.y = MathF.Min( s.y, MaximumHeight );
			if ( MaximumWidth > 0 ) s.x = MathF.Min( s.x, MaximumWidth );

			return s;
		}

		Vector2 _baseMiniumSizeHint;

		/// <summary>
		/// Return the minimum size this widget wants to be
		/// </summary>
		protected virtual Vector2 MinimumSizeHint()
		{
			return _baseMiniumSizeHint;
		}

		Vector2 _baseSizeHint;

		/// <summary>
		/// Should return the size this widget really wants to be if it can its way. The default
		/// is that you don't care - and just to return whatever the base value is.
		/// </summary>
		protected virtual Vector2 SizeHint()
		{
			return _baseSizeHint;
		}

		#region drag and drop events

		private bool _beingDroppedOn;

		/// <summary>
		/// Whether something is being dragged over this widget.
		/// </summary>
		public bool IsBeingDroppedOn
		{
			get => _beingDroppedOn;

			private set
			{
				if ( _beingDroppedOn == value ) return;
				_beingDroppedOn = value;
				Update();
			}
		}

		/// <summary>
		/// Information about a widget drag and drop event.
		/// </summary>
		public class DragEvent
		{
			internal Action<DropAction> setDropAction;

			/// <summary>
			/// Cursor position, local to this widget.
			/// </summary>
			public Vector2 LocalPosition { get; set; }

			/// <summary>
			/// The drag data.
			/// </summary>
			public DragData Data { get; internal set; }

			internal DropAction _action; // ignore default

			/// <summary>
			/// The keyboard modifier keys that were held down at the moment the event triggered.
			/// </summary>
			public KeyboardModifiers KeyboardModifiers { get; set; }

			/// <summary>
			/// Whether <c>Shift</c> key was being held down at the time of the event.
			/// </summary>
			public bool HasShift => KeyboardModifiers.Contains( KeyboardModifiers.Shift );

			/// <summary>
			/// Whether <c>Control</c> key was being held down at the time of the event.
			/// </summary>
			public bool HasCtrl => KeyboardModifiers.Contains( KeyboardModifiers.Ctrl );

			/// <summary>
			/// Whether <c>Alt</c> key was being held down at the time of the event.
			/// </summary>
			public bool HasAlt => KeyboardModifiers.Contains( KeyboardModifiers.Alt );

			/// <summary>
			/// Set this to what action will be (or was) performed.
			/// </summary>
			public DropAction Action
			{
				get => _action;
				set
				{
					_action = value;
					setDropAction?.Invoke( value );
				}
			}
		}

		internal bool InternalDragMoveEvent( QDragMoveEvent e )
		{
			var de = new DragEvent();
			de.LocalPosition = (Vector2)e.pos();
			de.Data = DragData.Current.IsValid() ? DragData.Current : (DragData)FindOrCreate( e.mimeData() );
			de._action = e.dropAction();
			de.setDropAction = e.setDropAction;
			de.KeyboardModifiers = QtHelpers.Translate( e.keyboardModifiers() );

			Update();
			OnDragHover( de );
			if ( de.Action != DropAction.Ignore )
			{
				e.accept();
				IsBeingDroppedOn = true;
				return true;
			}

			IsBeingDroppedOn = false;

			if ( AcceptDrops )
			{
				e.accept();
				return true;
			}

			return false;
		}

		internal bool InternalDragLeaveEvent( QDragLeaveEvent e )
		{
			IsBeingDroppedOn = false;
			OnDragLeave();
			return AcceptDrops;
		}

		internal bool InternalDropEvent( QDropEvent e )
		{
			var de = new DragEvent();
			de.LocalPosition = (Vector2)e.pos();
			de.Data = DragData.Current.IsValid() ? DragData.Current : (DragData)FindOrCreate( e.mimeData() );
			de._action = e.dropAction();
			de.setDropAction = e.setDropAction;
			de.KeyboardModifiers = QtHelpers.Translate( e.keyboardModifiers() );

			Update();
			IsBeingDroppedOn = false;

			OnDragDrop( de );
			if ( de.Action != DropAction.Ignore )
			{
				e.accept();
				return true;
			}

			return false;
		}

		internal bool InternalDragEnterEvent( QDragEnterEvent e )
		{
			Update();

			var de = new DragEvent();
			de.LocalPosition = (Vector2)e.pos();
			de.Data = DragData.Current.IsValid() ? DragData.Current : (DragData)FindOrCreate( e.mimeData() );
			de._action = e.dropAction();
			de.setDropAction = e.setDropAction;
			de.KeyboardModifiers = QtHelpers.Translate( e.keyboardModifiers() );

			OnDragHover( de );

			if ( de.Action != DropAction.Ignore )
			{
				e.accept();
				IsBeingDroppedOn = true;
				return true;
			}

			IsBeingDroppedOn = false;

			if ( AcceptDrops )
			{
				e.accept();
				return true;
			}

			return false;
		}

		/// <summary>
		/// Called when dragging. <see cref="IsDraggable"/> should be true.
		/// </summary>
		protected virtual void OnDragStart()
		{

		}

		/// <summary>
		/// Cursor with drag and drop data left the bounds of this widget.
		/// <para>Requires <see cref="AcceptDrops"/> to function.</para>
		/// </summary>
		public virtual void OnDragLeave()
		{

		}

		/// <summary>
		/// Cursor with drag and drop data moved on this widget.
		/// <para>Requires <see cref="AcceptDrops"/> to function.</para>
		/// </summary>
		/// <param name="ev">The drag event info.</param>
		public virtual void OnDragHover( DragEvent ev )
		{

		}

		/// <summary>
		/// Something was dragged and dropped on this widget. Apply the data here, if its valid.
		/// <para>Requires <see cref="AcceptDrops"/> to function.</para>
		/// </summary>
		/// <param name="ev">The drag event info.</param>
		public virtual void OnDragDrop( DragEvent ev )
		{

		}

		#endregion
	}

	/// <summary>
	/// Describes why a <see cref="Widget"/>s' keyboard focus has changed via <see cref="Widget.OnFocus"/> and <see cref="Widget.OnBlur"/> callbacks.
	/// </summary>
	public enum FocusChangeReason
	{
		Mouse,
		Tab,
		Backtab,
		ActiveWindow,
		Popup,
		Shortcut,
		MenuBar,
		Other,
		None
	}
}
