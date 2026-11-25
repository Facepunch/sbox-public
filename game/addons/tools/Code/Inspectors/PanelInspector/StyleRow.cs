using Sandbox.UI;

namespace Editor.Inspectors;

partial class StyleRow : Widget
{
	StyleInspector ParentInspector;
	public IStyleBlock Block { get; }
	IStyleBlock.StyleProperty Entry;

	Button saveButton;
	Button restoreButton;

	Layout ContentLayout;

	public StyleRow( StyleInspector parent, Sandbox.UI.IStyleBlock block, IStyleBlock.StyleProperty entry ) : base( parent )
	{
		ParentInspector = parent;
		Block = block;
		Entry = entry;

		Layout = Layout.Row();

		// Save button
		{
			var widget = Layout.Add( new Widget( this ) );
			widget.Layout = Layout.Row();
			widget.FixedWidth = 48;
			widget.Layout.Spacing = 4;

			saveButton = widget.Layout.Add( new Button( this ) { Text = "save" } );
			saveButton.SetStyles( $"Button {{ font-family: 'Material Symbols Outlined'; padding: 0; border: 0; font-size: 13px; color: {Theme.Blue.Hex}; }}\n Button:hover {{ color: white; }}" );
			saveButton.Visible = Entry.Value != Entry.OriginalValue;
			saveButton.Clicked = SaveChanges;
			saveButton.ToolTip = $"Save changes to {Block.FileName}";

			restoreButton = widget.Layout.Add( new Button( this ) { Text = "restore" } );
			restoreButton.SetStyles( $"Button {{ font-family: 'Material Symbols Outlined'; padding: 0; border: 0; font-size: 13px; color: {Theme.Red.Hex}; }}\n Button:hover {{ color: white; }}" );
			restoreButton.Visible = Entry.Value != Entry.OriginalValue;
			restoreButton.Clicked = RestoreValue;
			restoreButton.ToolTip = $"Restore To Default";

			widget.Layout.AddStretchCell();
		}

		ContentLayout = Layout.AddRow( 1 );

		Rebuild( false );
	}

	public void Rebuild( bool withEditor )
	{
		ContentLayout.Clear( true );

		ContentLayout.Margin = new Sandbox.UI.Margin( 0, 0, 8, 0 );

		var l = ContentLayout.Add( new Label( $"{Entry.Name}:" ) { Alignment = TextFlag.RightCenter } );
		l.Color = Entry.IsValid ? Theme.Blue : Color.White;
		foreach ( var block in ParentInspector.Panel.ActiveStyleBlocks.Reverse() )
		{
			if ( block == Block )
			{
				break;
			}

			foreach ( var entry in block.GetRawValues() )
			{
				if ( entry.Name == Entry.Name )
				{
					l.SetStyles( "text-decoration: line-through; " );
					break;
				}
			}
		}
		ContentLayout.AddSpacingCell( 4 );


		if ( Entry.IsValid )
		{
			if ( Entry.Name == "color" || Entry.Name == "background-color" )
			{
				var color = Color.Parse( Entry.Value ) ?? Color.Magenta;

				var w = ContentLayout.Add( new Widget( this ), 0 );
				w.FixedHeight = 15;
				w.FixedWidth = 25;
				w.SetStyles( $"margin-right: 4px; background-color: {color.Hex}; border: 1px solid #aaa; border-radius: 2px;" );
				w.Cursor = CursorShape.Finger;
				w.MouseLeftPress = () => ColorPicker.OpenColorPopup( color, ( c ) =>
				{
					UpdateValue( c.ToString( true, true ) );
					w.SetStyles( $"background-color: {c.Hex}; border: 1px solid #aaa; border-radius: 2px;" );
				}, w.ScreenRect.BottomLeft );
			}
		}

		if ( withEditor )
		{
			var v = ContentLayout.Add( new LineEdit( $"{Entry.Value}", this ) { Alignment = TextFlag.LeftCenter } );
			v.ContentMargins = 0;
			v.SetStyles( $"padding: 0; margin: 0; min-height: 0; height: 13px; border-bottom: 0px solid {Theme.Green.Hex}; selection-background-color: {Theme.Green.Hex}77;" );
			v.SelectAll();
			v.Focus();

			void ResizeTextEdit( string text )
			{
				var size = Paint.MeasureText( ContentLayout.OuterRect, text, ContentLayout.Alignment );
				v.FixedSize = new( size.Size.x + 3, size.Size.y );
			}
			ResizeTextEdit( v.Text );

			v.TextChanged += ( t ) =>
			{
				ResizeTextEdit( t );
				UpdateValue( t );
			};
			v.EditingFinished += () =>
			{
				UpdateValue( v.Value );
				Rebuild( false );
			};

			var suffix = ContentLayout.Add( new Label( ";", this ) { Alignment = TextFlag.LeftCenter } );
			suffix.Color = Color.White;
		}
		else
		{
			var v = ContentLayout.Add( new Label( $"{Entry.Value};", this ) { Alignment = TextFlag.LeftCenter } );
			v.Color = Color.White;
			v.Cursor = CursorShape.IBeam;
			v.MouseClick = () => Rebuild( true );
			v.SetStyles( $"QLabel:hover:!pressed {{ border-bottom: 1px dotted {Color.White.Hex}; }}" );
			//row.Add( new LineEdit( this ) { Value = entry.Value } , 1 );
		}

		if ( !Entry.IsValid )
		{
			l.SetStyles( "text-decoration: line-through; " );
			var v = ContentLayout.Add( new Label( "⚠️", this ) );
			v.ToolTip = "Invalid property";
			v.SetStyles( "text-decoration: none; " );
		}

		ContentLayout.AddStretchCell();
	}

	void UpdateValue( string value )
	{
		value = value.TrimEnd( ';', ' ' );

		if ( value.Contains( ';' ) )
			return;

		Entry.Value = value;
		bool success = Block.SetRawValue( Entry.Name, Entry.Value );
		Entry.IsValid = success;

		saveButton.Visible = Entry.Value != Entry.OriginalValue;
		restoreButton.Visible = Entry.Value != Entry.OriginalValue;
	}

	void SaveChanges()
	{
		//Log.Info( $"Open file {Block.FileName} at line {Entry.Line} and change {Entry.OriginalValue} to {Entry.Value}" );

		var lines = System.IO.File.ReadAllLines( Block.AbsolutePath );
		var line = lines[Entry.Line];

		var e = line.IndexOf( Entry.Name, System.StringComparison.OrdinalIgnoreCase );
		if ( e == -1 )
		{
			Log.Warning( $"Couldn't find '{Entry.Name}' in '{line}' ({Block.FileName}:{Entry.Line})" );
			return;
		}

		e = line.IndexOf( ':', e );

		if ( e == -1 )
		{
			Log.Warning( $"Couldn't find ':' after '{Entry.Name}' in '{line}' ({Block.FileName}:{Entry.Line})" );
			return;
		}

		e = line.IndexOf( Entry.OriginalValue, e );

		if ( e == -1 )
		{
			Log.Warning( $"Couldn't find '{Entry.OriginalValue}' after '{Entry.Name}:' in '{line}' ({Block.FileName}:{Entry.Line})" );
			return;
		}

		// remove old value
		line = line.Remove( e, Entry.OriginalValue.Length );

		// add new value
		line = line.Insert( e, Entry.Value );

		// replace it in the array
		lines[Entry.Line] = line;

		FileSystem.SuppressNextHotload();
		System.IO.File.WriteAllLines( Block.AbsolutePath, lines );

		Entry.OriginalValue = Entry.Value;
		saveButton.Visible = false;
		restoreButton.Visible = false;

		// overwrite the "original" value too
		Block.SetRawValue( Entry.Name, Entry.Value, Entry.Value );
	}

	void RestoreValue()
	{
		UpdateValue( Entry.OriginalValue );

		saveButton.Visible = false;
		restoreButton.Visible = false;

		Rebuild( false );
	}
}
