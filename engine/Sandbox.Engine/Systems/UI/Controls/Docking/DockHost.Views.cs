using Sandbox.UI.Construct;

namespace Sandbox.UI;

public partial class DockHost
{
	sealed class DockTabBar( DockHost host ) : TabBar
	{
		// Reparenting and selection are committed together by the docking layout.
		protected override void OnChildRemoved( Panel child ) { }

		public override void SelectTab( Tab tab )
		{
			if ( tab is DockTab dock ) host.Activate( dock.Item.Id );
		}

		public override bool CloseTab( Tab tab ) => tab is DockTab dock && host.Close( dock.Item.Id );
	}

	sealed class GroupView : Panel
	{
		internal TabBar Tabs { get; }
		internal Panel Body { get; }

		internal GroupView( DockHost host )
		{
			AddClass( "dock-group" );
			Tabs = AddChild( new DockTabBar( host ) );
			Tabs.AddClass( "dock-tabs" );
			Body = Add.Panel( "dock-body" );
		}
	}

	// Shared tab chrome, closing, menus and keyboard navigation; docking owns the drag operation.
	sealed class DockTab : Tab
	{
		readonly DockHost _host;
		internal DockItem Item { get; }
		bool _leftPressed;

		public override bool WantsDrag => !_host.UsesWindowDragging;

		internal DockTab( DockHost host, DockItem item )
		{
			_host = host;
			Item = item;
			Text = item.Title;
			Icon = item.Icon;
			CanClose = item.CanClose;
			AddClass( "dock-tab" );
			foreach ( var child in Children )
			{
				if ( child.HasClass( "tab-title" ) ) child.AddClass( "dock-tab-title" );
				if ( child.HasClass( "tab-icon" ) && !string.IsNullOrWhiteSpace( item.Icon ) ) child.AddClass( "dock-tab-icon" );
				if ( child.HasClass( "tab-close" ) && item.CanClose )
				{
					child.AddClass( "dock-tab-action" );
					child.Tooltip = "Close panel";
				}
			}
			BuildContextMenu = menu =>
			{
				if ( host.UsesWindowDragging && host.FloatRequested is not null )
					menu.AddOption( "Float", "open_in_new", () => host.FloatRequested?.Invoke( item.Id ) );
			};
		}

		protected override void OnMouseDown( MousePanelEvent e )
		{
			_leftPressed = e.Button == "mouseleft";
			base.OnMouseDown( e );
			if ( _leftPressed && _host.UsesWindowDragging )
			{
				e.StopPropagation();
				_host.DragPressed( _host, Item.Id, ScreenMousePosition );
			}
		}

		protected override void OnDragStart( DragEvent e )
		{
			e.StopPropagation();
			if ( _leftPressed ) _host.BeginDrag( Item.Id );
		}
		protected override void OnDrag( DragEvent e ) { e.StopPropagation(); _host.UpdateDrag( e.ScreenPosition ); }
		protected override void OnDragEnd( DragEvent e ) { e.StopPropagation(); _host.EndDrag( e.ScreenPosition ); }
		protected override void OnEscape( PanelEvent e ) { e.StopPropagation(); _host.CancelDrag(); }

		public override void OnButtonTyped( ButtonEvent e )
		{
			if ( e.Button == "escape" )
			{
				e.StopPropagation = true;
				_host.CancelDrag();
				return;
			}
			base.OnButtonTyped( e );
		}

		protected override void OnBlur( PanelEvent e ) { _host.CancelDrag(); base.OnBlur( e ); }
	}

	static Vector2 MinimumSize( DockNode node )
	{
		if ( node is not DockSplit split ) return new Vector2( 120, 80 );
		var first = MinimumSize( split.First );
		var second = MinimumSize( split.Second );
		return split.Vertical
			? new Vector2( MathF.Max( first.x, second.x ), first.y + second.y + 5 )
			: new Vector2( first.x + second.x + 5, MathF.Max( first.y, second.y ) );
	}

	sealed class SplitView : Panel
	{
		readonly DockHost _host;
		readonly DockSplit _split;
		readonly Panel _handle;
		bool _dragging;
		float _grabOffset;
		float _startFraction;

		internal Panel First { get; }
		internal Panel Second { get; }

		internal SplitView( DockHost host, DockSplit split )
		{
			_host = host;
			_split = split;
			AddClass( "dock-split" );
			SetClass( "vertical", split.Vertical );
			First = Add.Panel( "dock-branch" );
			_handle = Add.Panel( "dock-splitter" );
			_handle.AcceptsFocus = true;
			Second = Add.Panel( "dock-branch" );
			_handle.AddEventListener( "onmousedown", e =>
			{
				e.StopPropagation();
				if ( e is not MousePanelEvent { Button: "mouseleft" } ) return;
				_host.CancelDrag();
				_dragging = true;
				_startFraction = split.Fraction;
				_grabOffset = Axis( _handle.MousePosition ) * ScaleFromScreen;
				SetClass( "resizing", true );
			} );
			_handle.AddEventListener( "onmouseup", e =>
			{
				if ( e is MousePanelEvent { Button: "mouseleft" } ) StopDragging();
			} );
		}

		float Axis( Vector2 value ) => _split.Vertical ? value.y : value.x;
		float Available => MathF.Max( 0, Axis( Box.Rect.Size ) * ScaleFromScreen - 5 );

		float ClampFraction( float fraction )
		{
			var first = Axis( MinimumSize( _split.First ) );
			var second = Axis( MinimumSize( _split.Second ) );
			var available = Available;
			// When the host is too small, share the space rather than overflow or invert the split.
			if ( available < first + second ) return first / (first + second);
			return Math.Clamp( fraction, first / available, 1 - second / available );
		}

		internal void UpdateFraction()
		{
			var fraction = ClampFraction( _split.Fraction );
			First.Style.FlexGrow = fraction;
			Second.Style.FlexGrow = 1 - fraction;
		}

		void StopDragging()
		{
			_dragging = false;
			SetClass( "resizing", false );
		}

		/// <inheritdoc/>
		public override void Tick()
		{
			base.Tick();
			UpdateFraction();
			if ( _dragging && _host.UISystem?.Input is SurfaceInput input && !input.MouseInside ) StopDragging();
		}

		protected override void OnMouseMove( MousePanelEvent e )
		{
			if ( !_dragging || Available <= 0 ) return;
			e.StopPropagation();
			var fraction = (Axis( MousePosition ) * ScaleFromScreen - _grabOffset) / Available;
			_host._layout.SetFraction( _split, Math.Clamp( ClampFraction( fraction ), 0.05f, 0.95f ) );
		}

		protected override void OnEscape( PanelEvent e )
		{
			if ( !_dragging ) return;
			e.StopPropagation();
			StopDragging();
			_host._layout.SetFraction( _split, _startFraction );
		}

		/// <inheritdoc/>
		public override void OnButtonTyped( ButtonEvent e )
		{
			if ( e.Button == "escape" && _dragging )
			{
				e.StopPropagation = true;
				StopDragging();
				_host._layout.SetFraction( _split, _startFraction );
				return;
			}
			base.OnButtonTyped( e );
		}
	}
}
