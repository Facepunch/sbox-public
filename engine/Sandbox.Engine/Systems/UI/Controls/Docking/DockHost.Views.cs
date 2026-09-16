using Sandbox.UI.Construct;

namespace Sandbox.UI;

public partial class DockHost
{
	sealed class GroupView : Panel
	{
		internal Panel Tabs { get; }
		internal Panel Body { get; }

		internal GroupView()
		{
			AddClass( "dock-group" );
			Tabs = Add.Panel( "dock-tabs" );
			Tabs.CanDragScroll = false;
			Body = Add.Panel( "dock-body" );
		}
	}

	sealed class DockTab : Panel
	{
		readonly DockHost _host;
		readonly DockItem _item;
		bool _leftPressed;

		/// <inheritdoc/>
		public override bool WantsDrag => !_host.UsesWindowDragging;

		internal DockTab( DockHost host, DockItem item )
		{
			_host = host;
			_item = item;
			AddClass( "dock-tab" );
			AcceptsFocus = true;
			if ( !string.IsNullOrWhiteSpace( item.Icon ) ) Add.Icon( item.Icon, "dock-tab-icon" );
			Add.Label( item.Title, "dock-tab-title" );
			if ( item.CanClose ) AddAction( "close", () => host.Close( item.Id ) ).Tooltip = "Close panel";
		}

		Panel AddAction( string icon, Action action )
		{
			var button = Add.Panel( "dock-tab-action" );
			button.Add.Icon( icon );
			button.AddEventListener( "onmousedown", e => e.StopPropagation() );
			button.AddEventListener( "onclick", e =>
			{
				e.StopPropagation();
				action();
			} );
			return button;
		}

		protected override void OnMouseDown( MousePanelEvent e )
		{
			_leftPressed = e.Button == "mouseleft";
			if ( !_leftPressed ) e.StopPropagation();
			// Activation keeps the tab instance alive for the input system's pending drag.
			if ( !_leftPressed ) return;
			_host.Activate( _item.Id );
			if ( _host.UsesWindowDragging )
			{
				e.StopPropagation();
				_host.DragPressed( _host, _item.Id, ScreenMousePosition );
			}
		}

		protected override void OnClick( MousePanelEvent e )
		{
			e.StopPropagation();
			_host.Activate( _item.Id );
		}

		protected override void OnMiddleClick( MousePanelEvent e )
		{
			e.StopPropagation();
			_host.Close( _item.Id );
		}

		protected override void OnRightClick( MousePanelEvent e )
		{
			e.StopPropagation();
			var menu = new Menu();
			if ( _host.UsesWindowDragging && _host.FloatRequested is not null )
				menu.AddOption( "Float", "open_in_new", () => _host.FloatRequested?.Invoke( _item.Id ) );
			if ( _item.CanClose ) menu.AddOption( "Close", "close", () => _host.Close( _item.Id ) );
			if ( menu.Options.Count == 0 ) { menu.Delete( true ); return; }
			menu.Closed += _ => menu.Delete( true );
			menu.Open( this, Popup.PositionMode.UnderMouse );
		}

		protected override void OnDragStart( DragEvent e )
		{
			e.StopPropagation();
			if ( _leftPressed ) _host.BeginDrag( _item.Id );
		}

		protected override void OnDrag( DragEvent e )
		{
			e.StopPropagation();
			_host.UpdateDrag( e.ScreenPosition );
		}

		protected override void OnDragEnd( DragEvent e )
		{
			e.StopPropagation();
			_host.EndDrag( e.ScreenPosition );
		}

		protected override void OnEscape( PanelEvent e )
		{
			e.StopPropagation();
			_host.CancelDrag();
		}

		/// <inheritdoc/>
		public override void OnButtonTyped( ButtonEvent e )
		{
			if ( e.Button == "escape" )
			{
				e.StopPropagation = true;
				_host.CancelDrag();
				return;
			}
			if ( e.Button is "left" or "right" )
			{
				var group = _host._layout.FindGroup( _item.Id );
				if ( group is not null )
				{
					var index = group.Items.IndexOf( _item.Id );
					var next = group.Tabs[(index + (e.Button == "right" ? 1 : group.Tabs.Count - 1)) % group.Tabs.Count];
					_host.Activate( next );
					_host._tabs[next].Focus();
					e.StopPropagation = true;
					return;
				}
			}
			base.OnButtonTyped( e );
		}

		protected override void OnBlur( PanelEvent e )
		{
			_host.CancelDrag();
			base.OnBlur( e );
		}
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
