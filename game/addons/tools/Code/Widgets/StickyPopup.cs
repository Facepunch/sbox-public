namespace Editor;

public partial class StickyPopup : Widget
{
	public static readonly HashSet<StickyPopup> All = new();
	public Widget Owner { get; init; }

	public StickyPopup( Widget parent = null ) : base( parent )
	{
		MouseTracking = true;
		WindowFlags = WindowFlags.Tool | WindowFlags.FramelessWindowHint | WindowFlags.WindowStaysOnTopHint;
		TranslucentBackground = true;
		SetSizeMode( SizeMode.CanShrink, SizeMode.CanShrink );
		Layout = Layout.Column();

		All.Add( this );
	}

	/// <summary>
	/// Try to align the popup with a widget
	/// </summary>
	/// <param name="widget"></param>
	public void AlignTo( Widget widget )
	{
		var screenRect = EditorWindow.ScreenRect;
		var targetRect = widget.ScreenRect;
		var posBelowRight = targetRect.BottomRight - new Vector2( Width, 0 );
		var bottomEdge = posBelowRight.y + Height;

		if ( bottomEdge > screenRect.Bottom )
		{
			Position = targetRect.TopRight - new Vector2( Width, Height );
		}
		else
		{
			Position = posBelowRight;
		}
	}

	public override void OnDestroyed()
	{
		All.Remove( this );
		base.OnDestroyed();
	}

	/// <summary>
	/// Create a popup editor for the given serialized object.
	/// If the type has a <see cref="InspectorWidget"/> it'll use that instead of a control sheet.
	/// </summary>
	/// <param name="so"></param>
	public void CreateProperties( SerializedObject so )
	{
		if ( !so.IsValid() ) return;

		var scrollArea = new ScrollArea( this );
		scrollArea.Canvas = new Widget( this );
		scrollArea.SetStyles( "QScrollArea { border: 0px solid red; }" );

		scrollArea.Canvas.Layout = Layout.Column();
		scrollArea.Canvas.Layout.Margin = new( 8, 0, 8, 8 );
		scrollArea.Canvas.Name = "StickyPopupCanvas";

		//
		// Check for a custom inspector
		// It'll take priority over the control sheet
		//
		var inspector = InspectorWidget.Create( so );
		if ( inspector.IsValid() )
		{
			scrollArea.Canvas.Layout.Add( inspector, 1 );
		}
		else
		{
			scrollArea.Canvas.Layout.Add( ControlSheet.Create( so ) );
		}

		scrollArea.Canvas.Layout.AddStretchCell();

		Layout.Add( scrollArea );
	}

	protected override void OnPaint()
	{
		Paint.SetBrushAndPen( Theme.WindowBackground.WithAlpha( 0.9f ) );
		Paint.DrawRect( LocalRect );
	}

	protected float PreferredWidth => 386f;
	protected float MaxHeight => 512f;

	protected override Vector2 SizeHint()
	{
		var totalHeight = Children.Sum( x => x is ScrollArea scrollArea ? scrollArea.Canvas.Height : x.Height );
		return new Vector2( PreferredWidth, MathF.Min( totalHeight, MaxHeight ) );
	}

	protected override void DoLayout()
	{
		AdjustSize();
		AlignTo( Owner );
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		if ( e.Key == KeyCode.Escape )
		{
			Destroy();
			e.Accepted = true;
			return;
		}

		base.OnKeyPress( e );
	}

	/// <summary>
	/// Destroys popups that are not related (not in the same popup chain/tree).
	/// Related means: same root popup, or ancestor/descendant at any depth.
	/// </summary>
	public void DestroyUnrelatedPopups()
	{
		var myRoot = GetRootPopup( this );

		foreach ( var popup in StickyPopup.All.ToList() )
		{
			if ( !popup.IsValid() ) continue;
			if ( ReferenceEquals( popup, this ) ) continue;

			var theirRoot = GetRootPopup( popup );

			if ( ReferenceEquals( myRoot, theirRoot ) || IsAncestor( this, popup ) || IsAncestor( popup, this ) )
			{
				continue;
			}

			popup.Destroy();
		}
	}

	private static StickyPopup FindParentPopup( Widget start )
	{
		var current = start;
		while ( current != null )
		{
			if ( current is StickyPopup popup )
				return popup;

			current = current.Parent;
		}

		return null;
	}

	private static StickyPopup GetRootPopup( StickyPopup popup )
	{
		var root = popup;
		var current = popup;

		var parentPopup = FindParentPopup( current.Owner );
		while ( parentPopup != null )
		{
			root = parentPopup;
			current = parentPopup;
			parentPopup = FindParentPopup( current.Owner );
		}

		return root;
	}

	private static bool IsAncestor( StickyPopup maybeAncestor, StickyPopup node )
	{
		var curWidget = node.Owner;
		while ( curWidget != null )
		{
			var popup = curWidget as StickyPopup ?? FindParentPopup( curWidget );
			if ( popup == null ) break;

			if ( ReferenceEquals( popup, maybeAncestor ) )
				return true;

			curWidget = popup.Owner;
		}
		return false;
	}

	private Vector2 _lastPos;
	private Vector2 _lastSize;
	private bool _initialized;

	/// <summary>
	/// This is bullshit, but I can't figure out a better way right now.
	/// Subwindow doesn't work because it doesn't capture input.
	/// </summary>
	[EditorEvent.Frame]
	void OnFrame()
	{
		// If the application isn't in focus, destroy the popup
		if ( !Application.FocusWidget.IsValid() )
		{
			Destroy();
			return;
		}

		var window = EditorWindow;
		if ( window == null ) return;

		if ( !_initialized )
		{
			_lastPos = window.Position;
			_lastSize = window.Size;
			_initialized = true;
			return;
		}

		if ( window.Position != _lastPos || window.Size != _lastSize )
		{
			Destroy();
			return;
		}

		_lastPos = window.Position;
		_lastSize = window.Size;
	}
}
