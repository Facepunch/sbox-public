using System.ComponentModel.DataAnnotations;

namespace Editor;

[CustomEditor( typeof( List<> ) )]
[CustomEditor( typeof( Array ) )]
[CustomEditor( typeof( NetList<> ) )]
public class ListControlWidget : ControlWidget
{
	public override bool SupportsMultiEdit => true;

	private bool _showAddButton = true;
	public bool ShowAddButton
	{
		get => _showAddButton;
		set
		{
			_showAddButton = value;
			if ( addButton is not null )
			{
				addButton.Visible = value;
			}
		}
	}

	internal SerializedCollection Collection;

	readonly Layout Content;

	IconButton addButton;
	bool preventRebuild = false;
	int? buildHash;
	object buildValue;

	protected override int ValueHash => Collection is null ? base.ValueHash : HashCode.Combine( base.ValueHash, Collection.Count() );

	public ListControlWidget( SerializedProperty property ) : this( property, GetCollection( property ) )
	{
	}

	private static SerializedCollection GetCollection( SerializedProperty property )
	{
		if ( property is null )
			return null;

		if ( !property.TryGetAsObject( out var so ) || so is not SerializedCollection sc )
			return null;

		return sc;
	}

	public ListControlWidget( SerializedProperty property, SerializedCollection sc ) : base( property )
	{
		Layout = Layout.Column();
		Layout.Spacing = 2;

		if ( sc is null && !property.IsMultipleValues ) return;

		if ( sc is not null )
		{
			Collection = sc;
			Collection.OnEntryAdded = Rebuild;
			Collection.OnEntryRemoved = Rebuild;
		}

		buildValue = SerializedProperty?.GetValue<object>();
		Content = Layout.AddColumn();

		Rebuild();
	}

	private void RefreshCollection()
	{
		var value = SerializedProperty?.GetValue<object>();

		if ( buildValue == value )
			return;

		buildValue = value;

		// Collection has changed, need to get the new one
		var sc = GetCollection( SerializedProperty );

		if ( sc is not null )
		{
			Collection = sc;
			Collection.OnEntryAdded = Rebuild;
			Collection.OnEntryRemoved = Rebuild;
		}
	}

	protected override void OnValueChanged()
	{
		RefreshCollection();
		Rebuild();
	}

	public void Rebuild()
	{
		if ( preventRebuild ) return;


		if ( Collection is not null )
		{
			var hash = ValueHash;
			if ( buildHash.HasValue && hash == buildHash.Value ) return;
			buildHash = hash;
		}

		using var _ = SuspendUpdates.For( this );

		Content.Clear( true );
		Content.Margin = 0;

		var column = Content.AddColumn();

		if ( SerializedProperty.IsMultipleValues )
		{
			RebuildMultiple( column );
		}
		else
		{
			RebuildSingle( column );
		}

		// bottom row
		if ( !IsControlDisabled )
		{
			var buttonRow = column.AddRow();
			buttonRow.Margin = new Sandbox.UI.Margin( Theme.RowHeight + 2, 0, 0, 0 );

			// Use the shared button class
			addButton = new CollectionAddButton( Collection );
			addButton.MouseClick = AddEntry;

			if ( !ShowAddButton )
				addButton.Visible = ShowAddButton;

			if ( SerializedProperty.TryGetAttribute<MaxLengthAttribute>( out var maxLengthAttr ) )
			{
				var maxLength = maxLengthAttr.Length;
				if ( Collection is not null )
				{
					addButton.Enabled = Collection.Count() < maxLength;
				}
				else
				{
					addButton.Enabled = GetMultipleMin() < maxLength;
				}
			}

			buttonRow.Add( addButton );
			buttonRow.AddStretchCell( 1 );
		}
	}

	void RebuildSingle( Layout column )
	{
		if ( Collection is null ) return;
		int index = 0;
		foreach ( var entry in Collection )
		{
			column.Add( new ListEntryWidget( this, entry, index ) );
			index++;
		}
	}

	void RebuildMultiple( Layout column )
	{
		var minCount = GetMultipleMin();
		if ( minCount == int.MaxValue || minCount == 0 ) return;
		for ( int i = 0; i < minCount; i++ )
		{
			MultiSerializedObject mo = new();
			foreach ( var listProp in SerializedProperty.MultipleProperties )
			{
				var collection = GetCollection( listProp );
				if ( collection is null ) continue;
				var property = collection.ElementAt( i );
				if ( property is not null )
				{
					mo.Add( property.Parent );
				}
			}
			mo.Rebuild();
			var so = (SerializedObject)mo;
			var entryProp = so.GetProperty( i.ToString() );
			column.Add( new ListEntryWidget( this, entryProp, i ) );
		}
	}

	void AddEntry()
	{
		if ( Collection is not null )
		{
			Collection.Add( null );
		}
		else
		{
			var minCount = GetMultipleMin();
			var maxCount = GetMultipleMax();
			var props = SerializedProperty.MultipleProperties.ToList();
			foreach ( var prop in props )
			{
				var collection = GetCollection( prop );
				if ( collection is null ) continue;
				if ( minCount == maxCount || collection.Count() == minCount )
				{
					if ( collection.Count() == 0 && minCount != 0 ) continue;
					collection?.Add( null );
				}
			}
			Rebuild();
		}
	}

	void RemoveEntry( int index )
	{
		if ( Collection is not null )
		{
			Collection.RemoveAt( index );
		}
		else
		{
			foreach ( var prop in SerializedProperty.MultipleProperties )
			{
				var collection = GetCollection( prop );
				if ( index < collection.Count() )
				{
					collection.RemoveAt( index );
				}
			}
			Rebuild();
		}
	}

	void DuplicateEntry( int index )
	{
		if ( Collection is not null )
		{
			var sourceProperty = Collection.Skip( index ).First();
			var sourceObj = sourceProperty.GetValue<object>();
			var sourceJson = Json.ToNode( sourceObj );

			Collection.Add( index + 1, Json.FromNode( sourceJson, sourceProperty.PropertyType ) );
		}
		else
		{
			foreach ( var prop in SerializedProperty.MultipleProperties )
			{
				var collection = GetCollection( prop );
				if ( index < collection.Count() )
				{
					var sourceProperty = collection.ElementAt( index );
					var sourceObj = sourceProperty.GetValue<object>();
					var sourceJson = Json.ToNode( sourceObj );
					collection.Add( index + 1, Json.FromNode( sourceJson, sourceProperty.PropertyType ) );
				}
			}
			Rebuild();
		}
	}

	void MoveEntry( int index, int delta )
	{
		var movingIndex = index + delta;
		if ( movingIndex < 0 || movingIndex > GetMultipleMin() - 1 ) return;

		preventRebuild = true;

		foreach ( var prop in SerializedProperty.MultipleProperties )
		{
			var collection = GetCollection( prop );
			if ( collection is null ) continue;
			Move( collection, index, delta );
		}

		preventRebuild = false;
		Rebuild();
	}

	void Move( SerializedCollection collection, int index, int delta )
	{
		List<object> list = new();
		var movingIndex = index + delta;
		foreach ( var item in collection )
		{
			list.Add( item.GetValue<object>() );
		}

		var prop = list.ElementAt( movingIndex );
		list.RemoveAt( movingIndex );
		list.Insert( index, prop );

		while ( collection.Count() > 0 )
		{
			collection.RemoveAt( 0 );
		}

		foreach ( var item in list )
		{
			collection.Add( item );
		}
	}

	int GetMultipleMin()
	{
		var minCount = int.MaxValue;
		foreach ( var entry in SerializedProperty.MultipleProperties )
		{
			var collection = GetCollection( entry );
			if ( collection is null ) continue;
			var count = collection.Count();
			if ( count < minCount ) minCount = count;
		}
		if ( minCount == int.MaxValue ) return 0;
		return minCount;
	}

	int GetMultipleMax()
	{
		var maxCount = int.MinValue;
		foreach ( var entry in SerializedProperty.MultipleProperties )
		{
			var collection = GetCollection( entry );
			if ( collection is null ) continue;
			var count = collection.Count();
			if ( count > maxCount ) maxCount = count;
		}
		if ( maxCount == int.MinValue ) return 0;
		return maxCount;
	}

	object GetDragData( int index )
	{
		if ( Collection is not null )
		{
			return index;
		}
		else
		{
			var firstProp = SerializedProperty.MultipleProperties.FirstOrDefault();
			if ( firstProp is null ) return null;
			var collection = GetCollection( firstProp );
			if ( index < collection.Count() )
			{
				return index;
			}
		}
		return null;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;

		Paint.ClearPen();
		Paint.SetBrush( Theme.TextControl.Darken( 0.6f ) );
	}

	// individual list entry
	class ListEntryWidget : Widget
	{
		Drag dragData;
		bool draggingAbove = false;
		bool draggingBelow = false;

		ListControlWidget ListWidget;
		int Index = -1;

		public ListEntryWidget( ListControlWidget parent, SerializedProperty property, int index ) : base( parent )
		{
			ListWidget = parent;
			Index = index;
			Layout = Layout.Row();
			Layout.Margin = new Sandbox.UI.Margin( 0, 2 );
			Layout.Spacing = 2;
			ReadOnly = parent.ReadOnly;
			Enabled = parent.Enabled;

			ToolTip = $"Element {Index}";

			var control = ControlSheetRow.CreateEditor( property );
			control.ReadOnly = ReadOnly;
			control.Enabled = Enabled;

			if ( control.IsControlDisabled )
			{
				Layout.Add( control );
			}
			else
			{
				IsDraggable = !control.IsControlDisabled;
				var dragHandle = new DragHandle( this ) { IconSize = 13, Foreground = Theme.TextControl, Background = Color.Transparent, FixedWidth = Theme.RowHeight, FixedHeight = Theme.RowHeight };
				var removeButton = new IconButton( "clear", () => parent.RemoveEntry( index ) ) { ToolTip = "Remove", Background = Theme.ControlBackground, FixedWidth = Theme.RowHeight, FixedHeight = Theme.RowHeight };

				Layout.Add( dragHandle );
				Layout.Add( control );
				Layout.Add( removeButton );

				dragHandle.MouseRightClick += () =>
				{
					var menu = new ContextMenu( this );

					menu.AddOption( "Remove", "clear", () => parent.RemoveEntry( index ) );
					menu.AddOption( "Duplicate", "content_copy", () => parent.DuplicateEntry( index ) );

					menu.OpenAtCursor();
				};
			}

			AcceptDrops = true;
		}

		protected override void OnPaint()
		{
			base.OnPaint();

			if ( draggingAbove )
			{
				Paint.SetPen( Theme.TextHighlight, 2f, PenStyle.Solid );
				Paint.DrawLine( LocalRect.TopLeft, LocalRect.TopRight );
				draggingAbove = false;
			}
			else if ( draggingBelow )
			{
				Paint.SetPen( Theme.TextHighlight, 2f, PenStyle.Solid );
				Paint.DrawLine( LocalRect.BottomLeft, LocalRect.BottomRight );
				draggingBelow = false;
			}
		}

		public override void OnDragHover( DragEvent ev )
		{
			base.OnDragHover( ev );

			if ( !TryDragOperation( ev, out var dragDelta ) )
			{
				draggingAbove = false;
				draggingBelow = false;
				return;
			}

			draggingAbove = dragDelta > 0;
			draggingBelow = dragDelta < 0;
		}

		public override void OnDragDrop( DragEvent ev )
		{
			base.OnDragDrop( ev );

			if ( !TryDragOperation( ev, out var delta ) ) return;

			ListWidget.MoveEntry( Index, delta );
		}

		bool TryDragOperation( DragEvent ev, out int delta )
		{
			delta = 0;
			var obj = ev.Data.Object;

			if ( obj is null ) return false;

			if ( obj is int otherIndex )
			{
				if ( Index == -1 || otherIndex == -1 )
				{
					return false;
				}

				delta = otherIndex - Index;
				return true;
			}

			return false;
		}

		class DragHandle : IconButton
		{
			ListEntryWidget Entry;

			public DragHandle( ListEntryWidget entry ) : base( "drag_handle" )
			{
				Entry = entry;

				IsDraggable = Entry.IsDraggable;
			}

			protected override void OnDragStart()
			{
				base.OnDragStart();

				Entry.dragData = new Drag( this );
				Entry.dragData.Data.Object = Entry.ListWidget.GetDragData( Entry.Index );
				Entry.dragData.Execute();
			}
		}
	}
}
