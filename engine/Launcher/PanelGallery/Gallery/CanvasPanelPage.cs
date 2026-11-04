using Sandbox.UI;

namespace Sandbox.PanelGallery;

/// <summary>
/// Ordinary panel trees inside a navigable canvas, with manually drawn connections.
/// </summary>
public class CanvasPanelPage : GalleryPage
{
	public CanvasPanelPage() : base( "Canvas Panel", "These are ordinary panels: edit the text, toggle the checkbox and press the button. Drag a card by its heading. Drag empty space to select cards; Shift adds to the selection. Middle-drag to pan; scroll to zoom." )
	{
		var handlesRow = Case( "Draggable handles", column: true );
		handlesRow.Add.Label( "Drag the coloured handles. Hold Shift to lock an axis; Escape cancels. Middle-drag to pan and scroll to zoom — handles keep their screen size.", "canvas-handle-help" );
		var handles = new HandleDemoCanvas();
		var handleTools = handlesRow.AddChild<Toolbar>();
		handleTools.AddButton( "Reset", "restart_alt", handles.Reset );
		handleTools.AddToggle( "Dragging enabled", "open_with", true, handles.EnableDragging );
		handleTools.AddToggle( "Shift locks axis", "straighten", true, handles.EnableAxisLock );
		handlesRow.AddChild( handles );
		handles.Readout = handlesRow.Add.Label( "Drag a handle to see its position and drag events.", "canvas-handle-readout" );

		var row = Case( "Interactive panels and drawn connections", column: true );
		var canvas = new DemoCanvas();
		var tools = row.AddChild<Toolbar>();
		tools.AddToggle( "Box select", "select_all", true, value => canvas.BoxSelectionEnabled = value );
		tools.AddToggle( "Limit view", "lock", false, value => canvas.NavigationBounds = value ? new Rect( 0, 0, 1000, 600 ) : null );
		tools.AddButton( "Reset view", "fit_screen", canvas.ResetView );
		tools.AddButton( "", "remove", () => canvas.Zoom( 1.25f ) ).Tooltip = "Zoom out";
		tools.AddButton( "", "add", () => canvas.Zoom( 0.8f ) ).Tooltip = "Zoom in";
		row.AddChild( canvas );

		// No special canvas item type: compose each card from normal panels and controls.
		var source = canvas.Content.Add.Panel( "canvas-demo-card" );
		canvas.PlaceCard( source, "Source", new( 70, 90 ) );
		var name = source.AddChild<Sandbox.UI.TextEntry>(); name.Text = "Particle emitter";
		var enabled = source.AddChild<Sandbox.UI.Checkbox>(); enabled.LabelText = "Enabled"; enabled.Checked = true;

		var transform = canvas.Content.Add.Panel( "canvas-demo-card" );
		canvas.PlaceCard( transform, "Transform", new( 380, 260 ) );
		transform.Add.Label( "Scale" );
		var scale = transform.AddChild<Sandbox.UI.NumberEntry>(); scale.Text = "1.0";
		transform.Add.Label( "Layout and input scale with the card.", "canvas-demo-description" );

		var output = canvas.Content.Add.Panel( "canvas-demo-card" );
		canvas.PlaceCard( output, "Output", new( 700, 110 ) );
		var result = output.Add.Label( "Ready", "canvas-demo-description" );
		int clicks = 0;
		output.AddChild( new Sandbox.UI.Button( "Run", "play_arrow", () => result.Text = $"{name.Text}: run {++clicks}" ) );
		canvas.Readout = Output();
	}

	sealed class HandleDemoCanvas : CanvasPanel
	{
		readonly List<(Handle Handle, Vector2 Start)> _handles = new();
		readonly Rect _bounds = new( 460, 170, 260, 100 );
		public Sandbox.UI.Label Readout { get; set; }

		public HandleDemoCanvas()
		{
			AddClass( "canvas-handle-demo" );
			MinimumViewSpan = 50;
			MaximumViewSpan = 4000;
			AddHandle( "Free", new( 140, 75 ), "free" );
			var horizontal = AddHandle( "Horizontal", new( 400, 75 ), "horizontal" );
			horizontal.MovementAxis = Handle.Axis.Horizontal;
			var vertical = AddHandle( "Vertical", new( 660, 75 ), "vertical" );
			vertical.MovementAxis = Handle.Axis.Vertical;
			var snap = AddHandle( "Snap 25", new( 200, 225 ), "snap" );
			snap.SnapIncrement = new( 25 );
			var bounded = AddHandle( "Bounded", new( 590, 225 ), "bounded" );
			bounded.MovementBounds = _bounds;
			Reset();
		}

		Handle AddHandle( string title, Vector2 position, string name )
		{
			var handle = AddChild<Handle>();
			handle.AddClass( "demo-handle " + name );
			handle.Position = position;
			handle.Add.Label( title );
			_handles.Add( (handle, position) );
			void Report( string state )
			{
				if ( Readout is not null )
					Readout.Text = $"{title} · {state} · X {handle.Position.x:0.0}   Y {handle.Position.y:0.0}";
			}
			handle.DragStarted += () => Report( "started" );
			handle.DragMoved += _ => Report( "dragging" );
			handle.DragFinished += cancelled => Report( cancelled ? "cancelled — position restored" : "finished" );
			return handle;
		}

		public void Reset()
		{
			foreach ( var (handle, start) in _handles )
			{
				handle.FinishDrag( true );
				handle.Position = start;
			}
			SetView( Vector2.Zero, new( 800, 320 ) );
			if ( Readout is not null )
				Readout.Text = "Positions and view reset.";
		}

		public void EnableDragging( bool enabled )
		{
			foreach ( var (handle, _) in _handles )
			{
				handle.DragEnabled = enabled;
				handle.SetClass( "drag-disabled", !enabled );
			}
		}

		public void EnableAxisLock( bool enabled )
		{
			foreach ( var (handle, _) in _handles )
				handle.LockAxisWithShift = enabled;
		}

		public override void OnDraw( Painter painter )
		{
			using var scope = painter.Scope();
			painter.Clip( Viewport );
			var min = ScreenToCanvas( Viewport.Position );
			var max = ScreenToCanvas( Viewport.Position + Viewport.Size );
			painter.Stroke = Stroke.Solid( Color.White.WithAlpha( 0.045f ), 1 );
			for ( float x = MathF.Ceiling( min.x / 25 ) * 25; x <= max.x; x += 25 )
				painter.Line( CanvasToScreen( new( x, min.y ) ), CanvasToScreen( new( x, max.y ) ) );
			for ( float y = MathF.Ceiling( min.y / 25 ) * 25; y <= max.y; y += 25 )
				painter.Line( CanvasToScreen( new( min.x, y ) ), CanvasToScreen( new( max.x, y ) ) );
			painter.Stroke = Stroke.Solid( ((Color)"#bb9af7").WithAlpha( 0.5f ), 1 );
			painter.Line( CanvasToScreen( new( 290, 75 ) ), CanvasToScreen( new( 510, 75 ) ) );
			painter.Line( CanvasToScreen( new( 660, 20 ) ), CanvasToScreen( new( 660, 135 ) ) );
			painter.Stroke = Stroke.Solid( ((Color)"#e8b56b").WithAlpha( 0.7f ), 1 );
			painter.Fill = ((Color)"#e8b56b").WithAlpha( 0.04f );
			var topLeft = CanvasToScreen( _bounds.Position );
			painter.Rect( new Rect( topLeft, CanvasToScreen( _bounds.Position + _bounds.Size ) - topLeft ) );
		}
	}

	// Only the grid, connections and example-specific heading dragging are custom.
	sealed class DemoCanvas : CanvasPanel
	{
		readonly List<Panel> _cards = new();
		public Sandbox.UI.Label Readout { get; set; }

		public DemoCanvas()
		{
			Style.Width = Length.Percent( 100 ); Style.Height = 440;
			Style.BackgroundColor = (Color)"#101216";
			MinimumViewSpan = 50; MaximumViewSpan = 10000;
			ResetView();
			BoxSelectionEnabled = true;
			Panel[] before = [], additive = [];
			BoxSelectionStarted += shift =>
			{
				before = _cards.Where( x => x.HasClass( "selected" ) ).ToArray();
				additive = shift ? before : [];
			};
			BoxSelectionChanged += rect =>
			{
				foreach ( var card in _cards )
					card.SetClass( "selected", additive.Contains( card ) || rect.IsInside( Position( card ) + card.Box.Rect.Size / ScaleToScreen * 0.5f ) );
			};
			BoxSelectionFinished += cancelled =>
			{
				if ( cancelled ) foreach ( var card in _cards ) card.SetClass( "selected", before.Contains( card ) );
			};
		}
		public void ResetView() => SetView( Vector2.Zero, new( 1000, 600 ) );
		public void Zoom( float factor ) => ZoomAt( Box.Rect.Size * 0.5f, factor );
		Vector2 Position( Panel panel ) => (panel.Box.Rect.Position - Content.Box.Rect.Position) / ScaleToScreen;

		public void PlaceCard( Panel card, string title, Vector2 position )
		{
			card.Style.Left = position.x; card.Style.Top = position.y;
			var heading = card.AddChild<CardHeading>();
			heading.AddClass( "canvas-demo-heading" );
			heading.Add.Label( title );
			_cards.Add( card );
		}
		sealed class CardHeading : CanvasPanel.Handle
		{
			Vector2 _start;

			public CardHeading()
			{
				Positioned = false;
			}

			protected override bool OnDragStart( MousePanelEvent e )
			{
				_start = (Parent.Box.Rect.Position - Parent.Parent.Box.Rect.Position) / ScaleToScreen;
				return true;
			}

			protected override void OnDragMove( Vector2 delta )
			{
				Parent.Style.Left = _start.x + delta.x;
				Parent.Style.Top = _start.y + delta.y;
			}

			protected override void OnDragFinish( bool cancelled )
			{
				if ( cancelled )
					OnDragMove( Vector2.Zero );
			}
		}

		public override void OnDraw( Painter painter )
		{
			using var scope = painter.Scope();
			painter.Clip( Viewport );
			painter.Stroke = Stroke.Solid( Color.White.WithAlpha( 0.07f ), 1 );
			var min = ScreenToCanvas( Viewport.Position );
			var max = ScreenToCanvas( Viewport.Position + Viewport.Size );
			float step = MathF.Pow( 10, MathF.Floor( MathF.Log10( (max.x - min.x) / 10 ) ) );
			for ( float x = MathF.Ceiling( min.x / step ) * step; x <= max.x; x += step )
				painter.Line( CanvasToScreen( new( x, min.y ) ), CanvasToScreen( new( x, max.y ) ) );
			for ( float y = MathF.Ceiling( min.y / step ) * step; y <= max.y; y += step )
				painter.Line( CanvasToScreen( new( min.x, y ) ), CanvasToScreen( new( max.x, y ) ) );
			painter.Stroke = Stroke.Solid( (Color)"#7da5d9", 1 );
			for ( int i = 0; i < _cards.Count - 1; i++ )
			{
				var a = _cards[i]; var b = _cards[i + 1];
				painter.Line( CanvasToScreen( Position( a ) + new Vector2( a.Box.Rect.Width, a.Box.Rect.Height / 2 ) / ScaleToScreen ),
					CanvasToScreen( Position( b ) + new Vector2( 0, b.Box.Rect.Height / 2 ) / ScaleToScreen ) );
			}
		}
		public override void Tick()
		{
			base.Tick();
			if ( Readout is not null ) Readout.Text = $"View: {ViewMin.x:0}, {ViewMin.y:0} to {ViewMax.x:0}, {ViewMax.y:0}";
		}
	}
}
