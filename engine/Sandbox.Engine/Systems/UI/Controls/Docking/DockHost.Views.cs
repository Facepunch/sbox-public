using Sandbox.UI.Construct;

namespace Sandbox.UI;

public partial class DockHost
{
	sealed class DockTabBar( DockHost host ) : TabBar
	{
		// Reparenting and selection are committed together by the docking layout.
		protected override void OnChildRemoved( Panel child ) { }

		/// <summary>
		/// Activate the tab through the dock layout.
		/// </summary>
		public override void SelectTab( Tab tab )
		{
			if ( tab is DockTab dock ) host.Activate( dock.Item.Id );
		}

		/// <summary>
		/// Close the tab through the dock layout.
		/// </summary>
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

		/// <summary>
		/// Use panel dragging unless the host delegates dragging to a window.
		/// </summary>
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

		protected override void OnDragCancel( DragEvent e )
		{
			e.StopPropagation();
			_host.CancelDrag();
		}
	}

	// Tab groups accommodate every tab, so switching tabs does not move the splitters.
	// Absolute content constraints can guide the allocation without depending on its result.
	(float Min, float Max) SizeLimits( DockNode node, bool vertical )
	{
		if ( node is DockGroup group ) return GroupSizeLimits( group, vertical );

		var split = (DockSplit)node;
		var first = SizeLimits( split.First, vertical );
		var second = SizeLimits( split.Second, vertical );
		if ( split.Vertical != vertical )
		{
			var minimum = MathF.Max( first.Min, second.Min );
			var maximum = MathF.Min( first.Max, second.Max );
			return (minimum, MathF.Max( minimum, maximum ));
		}

		var divider = _views.TryGetValue( split, out var splitView ) && splitView is SplitView sizing ? sizing.DividerSize : 5;
		return (first.Min + second.Min + divider, first.Max + second.Max + divider);
	}

	(float Min, float Max) GroupSizeLimits( DockGroup group, bool vertical )
	{
		float minimum = vertical ? 80 : 120;
		float maximum = 0;
		var chrome = GroupChromeSize( group, vertical );

		foreach ( var id in group.Items )
		{
			var style = Find( id )?.Content?.ComputedStyle;
			var min = vertical ? style?.MinHeight : style?.MinWidth;
			var max = vertical ? style?.MaxHeight : style?.MaxWidth;

			// Computed lengths are screen pixels; split sizes use logical pixels.
			if ( min is { Unit: LengthUnit.Pixels } minPixels )
			{
				minimum = MathF.Max( minimum, minPixels.Value * ScaleFromScreen + chrome );
			}

			var contentMaximum = float.PositiveInfinity;
			if ( max is { Unit: LengthUnit.Pixels } maxPixels )
			{
				contentMaximum = maxPixels.Value * ScaleFromScreen + chrome;
			}

			maximum = MathF.Max( maximum, contentMaximum );
		}

		return (minimum, MathF.Max( minimum, maximum ));
	}

	float GroupChromeSize( DockGroup group, bool vertical )
	{
		if ( !_views.TryGetValue( group, out var view ) || view is not GroupView pane ) return 0;

		var box = pane.Body.Box;
		var size = vertical
			? pane.Tabs.Box.RectOuter.Height + box.Border.Top + box.Border.Bottom + box.Padding.Top + box.Padding.Bottom
			: box.Border.Left + box.Border.Right + box.Padding.Left + box.Padding.Right;

		return MathF.Max( 0, size ) * ScaleFromScreen;
	}

	sealed class SplitView : Panel
	{
		readonly DockHost _host;
		readonly DockSplit _split;
		readonly Panel _handle;
		bool _dragging;
		float _grabOffset;
		float _startPosition;
		SplitView _resizeRoot;
		readonly List<(float StartSize, float Minimum, float Maximum)> _resizePanes = new();
		readonly Dictionary<DockSplit, float> _startFractions = new();
		float[] _resizeSizes;
		int _resizeBoundary;

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
				_grabOffset = Axis( _handle.MousePosition ) * ScaleFromScreen;
				BeginResize();
				SetClass( "resizing", true );
			} );
			_handle.AddEventListener( "onmouseup", e =>
			{
				if ( e is MousePanelEvent { Button: "mouseleft" } ) StopDragging();
			} );
		}

		float Axis( Vector2 value ) => _split.Vertical ? value.y : value.x;
		internal float DividerSize => Axis( _handle.Box.Rect.Size ) * ScaleFromScreen;
		float Available => MathF.Max( 0, Axis( Box.RectInner.Size ) * ScaleFromScreen - DividerSize );

		float ClampFraction( float fraction )
		{
			var first = _host.SizeLimits( _split.First, _split.Vertical );
			var second = _host.SizeLimits( _split.Second, _split.Vertical );
			var available = Available;
			// Minimums win over maximums. When neither fits, keep both panes usable.
			if ( available < first.Min + second.Min ) return first.Min / (first.Min + second.Min);
			if ( available > first.Max + second.Max ) return first.Max / (first.Max + second.Max);
			var lower = MathF.Max( first.Min, available - second.Max );
			var upper = MathF.Min( first.Max, available - second.Min );
			return Math.Clamp( fraction, lower / available, upper / available );
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

		void BeginResize()
		{
			_startPosition = Axis( _handle.Box.Rect.Position ) * ScaleFromScreen;
			_resizeRoot = this;

			// A divider can borrow space through neighbouring panes on the same axis.
			while ( _resizeRoot.Parent?.Parent is SplitView parent && parent._split.Vertical == _split.Vertical )
			{
				_resizeRoot = parent;
			}

			_resizePanes.Clear();
			_startFractions.Clear();
			CollectResizePanes( _resizeRoot._split );
			_resizeSizes = new float[_resizePanes.Count];
		}

		void CollectResizePanes( DockNode node )
		{
			if ( node is DockSplit split && split.Vertical == _split.Vertical )
			{
				_startFractions.Add( split, split.Fraction );
				CollectResizePanes( split.First );

				if ( split == _split ) _resizeBoundary = _resizePanes.Count;

				CollectResizePanes( split.Second );
				return;
			}

			var size = Axis( _host._views[node].Box.Rect.Size ) * ScaleFromScreen;
			var limits = _host.SizeLimits( node, _split.Vertical );

			// Don't jump to the minimum or maximum if the pane already falls outside it.
			var minimum = MathF.Min( size, limits.Min );
			var maximum = MathF.Max( size, limits.Max );
			_resizePanes.Add( (size, minimum, maximum) );
		}

		void Resize( float delta )
		{
			for ( int i = 0; i < _resizePanes.Count; i++ )
			{
				_resizeSizes[i] = _resizePanes[i].StartSize;
			}

			var direction = MathF.Sign( delta );
			var firstCapacity = ResizeCapacity( 0, _resizeBoundary, grow: delta > 0 );
			var secondCapacity = ResizeCapacity( _resizeBoundary, _resizePanes.Count, grow: delta < 0 );
			var distance = MathF.Min( MathF.Abs( delta ), MathF.Min( firstCapacity, secondCapacity ) );
			var movement = distance * direction;

			DistributeResize( _resizeBoundary - 1, -1, -1, movement );
			DistributeResize( _resizeBoundary, _resizePanes.Count, 1, -movement );

			var paneIndex = 0;
			ApplyResize( _resizeRoot._split, ref paneIndex );
		}

		float ResizeCapacity( int start, int end, bool grow )
		{
			float capacity = 0;

			for ( int i = start; i < end; i++ )
			{
				var pane = _resizePanes[i];
				capacity += grow ? pane.Maximum - pane.StartSize : pane.StartSize - pane.Minimum;
			}

			return capacity;
		}

		void DistributeResize( int start, int end, int step, float remaining )
		{
			// Start next to the divider and pass leftover movement to the next pane.
			for ( int i = start; i != end; i += step )
			{
				var pane = _resizePanes[i];
				var change = Math.Clamp( remaining, pane.Minimum - pane.StartSize, pane.Maximum - pane.StartSize );
				_resizeSizes[i] += change;
				remaining -= change;
			}
		}

		float ApplyResize( DockNode node, ref int paneIndex )
		{
			if ( node is not DockSplit split || split.Vertical != _split.Vertical ) return _resizeSizes[paneIndex++];

			var firstSize = ApplyResize( split.First, ref paneIndex );
			var secondSize = ApplyResize( split.Second, ref paneIndex );
			var fraction = Math.Clamp( firstSize / (firstSize + secondSize), 0.05f, 0.95f );
			_host._layout.SetFraction( split, fraction );

			var view = (SplitView)_host._views[split];
			return firstSize + secondSize + view.DividerSize;
		}

		void CancelResize()
		{
			StopDragging();
			foreach ( var (split, fraction) in _startFractions ) _host._layout.SetFraction( split, fraction );
		}

		/// <summary>
		/// Keep the split within its size limits and stop resizing when the pointer leaves.
		/// </summary>
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
			Resize( Axis( ScreenMousePosition ) * ScaleFromScreen - _grabOffset - _startPosition );
		}

		protected override void OnEscape( PanelEvent e )
		{
			if ( !_dragging ) return;
			e.StopPropagation();
			CancelResize();
		}

		/// <summary>
		/// Restore the starting layout when Escape is pressed during a resize.
		/// </summary>
		public override void OnButtonTyped( ButtonEvent e )
		{
			if ( e.Button == "escape" && _dragging )
			{
				e.StopPropagation = true;
				CancelResize();
				return;
			}
			base.OnButtonTyped( e );
		}
	}
}
