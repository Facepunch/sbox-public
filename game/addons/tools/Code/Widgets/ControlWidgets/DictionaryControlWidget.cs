namespace Editor;

[CustomEditor( typeof( Dictionary<,> ) )]
[CustomEditor( typeof( NetDictionary<,> ) )]
public class DictionaryControlWidget : ControlWidget
{
	public override bool SupportsMultiEdit => false;

	SerializedCollection Collection;

	readonly Layout Content;

	protected override int ValueHash => Collection is null ? base.ValueHash : HashCode.Combine( base.ValueHash, Collection.Count() );

	int? buildHash;
	object buildValue;

	public DictionaryControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Column();
		Layout.Spacing = 2;

		if ( !property.TryGetAsObject( out var so ) || so is not SerializedCollection sc )
			return;

		Collection = sc;
		Collection.OnEntryAdded = Rebuild;
		Collection.OnEntryRemoved = Rebuild;

		buildValue = SerializedProperty?.GetValue<object>();

		Content = Layout.Column();

		Layout.Add( Content );

		Rebuild();
	}

	private void RefreshCollection()
	{
		var value = SerializedProperty?.GetValue<object>();

		if ( buildValue == value )
			return;

		buildValue = value;

		if ( !SerializedProperty.TryGetAsObject( out var so ) || so is not SerializedCollection sc )
			return;

		Collection = sc;
		Collection.OnEntryAdded = Rebuild;
		Collection.OnEntryRemoved = Rebuild;
	}

	protected override void OnValueChanged()
	{
		RefreshCollection();
		Rebuild();
	}

	public void Rebuild()
	{
		if ( Collection is not null )
		{
			var hash = ValueHash;
			if ( buildHash.HasValue && hash == buildHash.Value ) return;
			buildHash = hash;
		}

		using var _ = SuspendUpdates.For( this );

		Content.Clear( true );
		Content.Margin = 0;

		var grid = Layout.Grid();
		grid.VerticalSpacing = 2;
		grid.HorizontalSpacing = 2;
		grid.SetMinimumColumnWidth( 1, 10 );
		grid.SetMinimumColumnWidth( 3, 150 );
		grid.SetColumnStretch( 0, 1, 0, 100, 0 );

		int y = 0;
		foreach ( var entry in Collection )
		{
			var key = entry.GetKey();

			var keyControl = ControlSheetRow.CreateEditor( key );
			var valControl = ControlSheetRow.CreateEditor( entry );

			keyControl.ReadOnly = ReadOnly;
			keyControl.Enabled = Enabled;
			keyControl.FixedHeight = MathF.Max( keyControl.MinimumHeight, Theme.ControlHeight );

			valControl.ReadOnly = ReadOnly;
			valControl.Enabled = Enabled;

			var index = y;
			//grid.AddCell( 0, y, new IconButton( "drag_handle" ) { IconSize = 13, Foreground = Theme.ControlBackground, Background = Color.Transparent, FixedWidth = Theme.RowHeight, FixedHeight = Theme.RowHeight } );
			grid.AddCell( 1, y, keyControl, 1, 1, TextFlag.Center );
			grid.AddCell( 2, y, new IconButton( ":" ) { IconSize = 13, Foreground = Theme.TextControl, Background = Color.Transparent, FixedWidth = Theme.RowHeight, FixedHeight = Theme.RowHeight } );
			grid.AddCell( 3, y, valControl, 1, 1, valControl.CellAlignment );

			if ( !IsControlDisabled )
			{
				grid.AddCell( 4, y, new IconButton( "clear", () => RemoveEntry( key.GetValue<object>() ) ) { Background = Theme.ControlBackground, FixedWidth = Theme.RowHeight, FixedHeight = Theme.RowHeight } );
			}

			y++;
		}

		// Add entry
		if ( !IsControlDisabled )
		{
			var newKeyProperty = Collection.NewKeyProperty();
			var keyControl = ControlSheetRow.CreateEditor( newKeyProperty );
			var addButton = new IconButton( "add" ) { Background = Theme.ControlBackground, ToolTip = "Add Entry", FixedWidth = Theme.RowHeight, FixedHeight = Theme.RowHeight };
			addButton.MouseClick = () =>
			{
				var value = newKeyProperty.GetValue<object>() ?? newKeyProperty.GetDefault();
				value ??= newKeyProperty.PropertyType == typeof( string ) ? string.Empty : null;

				if ( value is not null )
				{
					AddEntry( value );
				}
			};

			grid.AddCell( 1, y, keyControl, 1, 1, keyControl.CellAlignment );
			grid.AddCell( 2, y, addButton, 3 );
		}

		Content.Add( grid );

		//	Content.Margin = y > 0 ? 3 : 0;
	}

	void AddEntry( object key )
	{
		if ( Collection.Add( key, null ) )
		{
			Rebuild();
		}
	}

	void RemoveEntry( object key )
	{
		if ( Collection.RemoveAt( key ) )
		{
			Rebuild();
		}
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;

		//	Paint.ClearPen();
		//Paint.SetBrush( Theme.TextControl.Darken( 0.6f ) );

		if ( Collection is not null && Collection.Count() > 0 )
		{
			//Paint.DrawRect( Content.OuterRect, 2.0f );
			//	Paint.DrawRect( new Rect( addButton.Position, addButton.Size ).Grow( 0, 8, 0, 0 ), 2.0f );
		}
		else
		{
			//	Paint.DrawRect( new Rect( addButton.Position, addButton.Size ).Grow( 0, 0, 0, 0 ), 2.0f );
		}


	}

}
