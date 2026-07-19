namespace Editor.ProjectSettingPages;

/// <summary>
/// One layer in the sort layer list. Draggable, because reordering is the whole point of the list -
/// the position of a layer is what decides draw order.
/// </summary>
internal sealed class SortLayerRow : Widget
{
	private readonly SortLayerListWidget _list;

	public SortingSettings.SortLayer Layer { get; }

	private Drag _dragData;
	private bool _draggingAbove;
	private bool _draggingBelow;

	public SortLayerRow( SortLayerListWidget list, SortingSettings.SortLayer layer ) : base( null )
	{
		_list = list;
		Layer = layer;

		FixedHeight = 30;
		Cursor = CursorShape.Finger;

		Layout = Layout.Row();
		Layout.Margin = new Sandbox.UI.Margin( 24, 3, 4, 3 );
		Layout.Spacing = 4;

		var nameEntry = new LineEdit( layer.Name );
		nameEntry.PlaceholderText = "Layer name";
		nameEntry.TextEdited += OnNameEdited;
		Layout.Add( nameEntry, 1 );

		// The first layer is what everything falls back to, so it must always exist.
		if ( !IsDefaultLayer )
		{
			var deleteButton = new IconButton( "close" );
			deleteButton.ToolTip = "Delete";
			deleteButton.StatusTip = $"Delete sort layer \"{layer.Name}\"";
			deleteButton.OnClick += () => _list.RemoveLayer( Layer );
			Layout.Add( deleteButton );
		}
		else
		{
			Layout.AddSpacingCell( 24 );
		}

		IsDraggable = true;
		AcceptDrops = true;
	}

	private bool IsDefaultLayer => _list.Settings.Layers.IndexOf( Layer ) == 0;

	private void OnNameEdited( string value )
	{
		// Renaming is free - sprites reference layers by id, so nothing needs fixing up.
		Layer.Name = value;
		_list.NotifyChanged();
	}

	protected override void OnPaint()
	{
		if ( IsUnderMouse )
		{
			Paint.SetBrushAndPen( Theme.Highlight.WithAlpha( 0.1f ) );
			Paint.DrawRect( LocalRect, 3 );
		}

		if ( _dragData?.IsValid ?? false )
		{
			Paint.SetBrushAndPen( Theme.WindowBackground.WithAlpha( 0.5f ) );
			Paint.DrawRect( LocalRect, 3 );
		}

		// Drag handle, hinting that the row can be moved without needing a tooltip to say so.
		Paint.SetPen( Theme.Text.WithAlpha( IsUnderMouse ? 0.6f : 0.3f ) );
		Paint.DrawIcon( new Rect( 4, 0, 20, LocalRect.Height ), "drag_indicator", 16, TextFlag.Center );

		base.OnPaint();

		if ( _draggingAbove )
		{
			Paint.SetPen( Theme.Primary, 2f, PenStyle.Dot );
			Paint.DrawLine( LocalRect.TopLeft, LocalRect.TopRight );
			_draggingAbove = false;
		}
		else if ( _draggingBelow )
		{
			Paint.SetPen( Theme.Primary, 2f, PenStyle.Dot );
			Paint.DrawLine( LocalRect.BottomLeft, LocalRect.BottomRight );
			_draggingBelow = false;
		}
	}

	protected override void OnDragStart()
	{
		base.OnDragStart();

		_dragData = new Drag( this );
		_dragData.Data.Object = Layer;
		_dragData.Execute();
	}

	public override void OnDragHover( DragEvent ev )
	{
		base.OnDragHover( ev );

		if ( !TryDragOperation( ev, out var delta ) )
		{
			_draggingAbove = false;
			_draggingBelow = false;
			return;
		}

		_draggingAbove = delta > 0;
		_draggingBelow = delta < 0;
	}

	public override void OnDragDrop( DragEvent ev )
	{
		base.OnDragDrop( ev );

		if ( !TryDragOperation( ev, out var delta ) ) return;

		var index = _list.Settings.Layers.IndexOf( Layer );

		// Through the settings rather than the list: reordering changes what every layer id maps
		// to, and a stale lookup would silently draw every sprite in the wrong layer.
		_list.Settings.MoveLayer( index + delta, index );

		_list.NotifyChanged();
		_list.Rebuild();
	}

	public override void OnDragLeave()
	{
		base.OnDragLeave();

		_draggingAbove = false;
		_draggingBelow = false;
	}

	private bool TryDragOperation( DragEvent ev, out int delta )
	{
		delta = 0;

		var other = ev.Data.OfType<SortingSettings.SortLayer>().FirstOrDefault();
		if ( other is null || other == Layer ) return false;

		var layers = _list.Settings.Layers;
		var myIndex = layers.IndexOf( Layer );
		var otherIndex = layers.IndexOf( other );

		if ( myIndex < 0 || otherIndex < 0 || myIndex == otherIndex ) return false;

		delta = otherIndex - myIndex;
		return true;
	}
}
