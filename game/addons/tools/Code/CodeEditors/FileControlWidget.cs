namespace Editor.CodeEditors;

/// <summary>
/// When added to a string property, becomes a file picker for a file on disk (not a project asset).
/// </summary>
[AttributeUsage( AttributeTargets.Property )]
internal class FileAttribute : Attribute
{
	/// <summary>
	/// The extension to filter by in the browse dialog. If empty, all files are shown.
	/// </summary>
	public string Extension { get; set; } = "";
}

[CustomEditor( typeof( string ), WithAllAttributes = new[] { typeof( FileAttribute ) } )]
internal class FileControlWidget : ControlWidget
{
	public override bool IsControlButton => true;

	FileAttribute attribute;

	public FileControlWidget( SerializedProperty property ) : base( property )
	{
		property.TryGetAttribute( out attribute );

		Cursor = CursorShape.Finger;
		MouseTracking = true;
		AcceptDrops = true;
	}

	protected override void PaintControl()
	{
		var path = SerializedProperty.GetValue<string>( "" );
		var isEmpty = string.IsNullOrWhiteSpace( path );

		var rect = new Rect( 0, Size );

		var iconRect = rect.Shrink( 2 );
		iconRect.Width = iconRect.Height;

		rect.Left = iconRect.Right + 10;

		Paint.ClearPen();
		Paint.SetBrush( Theme.SurfaceBackground.WithAlpha( 0.2f ) );
		Paint.DrawRect( iconRect, 2 );

		Paint.SetPen( Theme.Text.WithAlpha( isEmpty ? 0.3f : 0.9f ) );
		Paint.DrawIcon( iconRect, "insert_drive_file", Math.Max( 16, iconRect.Height / 2 ) );

		if ( isEmpty )
		{
			var textRect = rect.Shrink( 0, 3 );
			Paint.SetDefaultFont( italic: true );
			Paint.SetPen( Theme.Text.WithAlpha( 0.2f ) );
			Paint.DrawText( textRect, "Select File...", TextFlag.LeftCenter );
			return;
		}

		var textRect2 = rect.Shrink( 0, 6 );
		Paint.SetPen( Theme.Text.WithAlpha( 0.9f ) );
		Paint.SetHeadingFont( 8, 450 );
		var t = Paint.DrawText( textRect2, System.IO.Path.GetFileNameWithoutExtension( path ), TextFlag.LeftCenter );

		textRect2.Left = t.Right + 6;
		Paint.SetDefaultFont( 7 );
		Theme.DrawFilename( textRect2, path, TextFlag.LeftBottom, Theme.Text.WithAlpha( 0.5f ) );
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		var m = new ContextMenu();

		var path = SerializedProperty.GetValue<string>( "" );
		var isEmpty = string.IsNullOrEmpty( path );

		m.AddOption( "Copy", "file_copy", action: Copy ).Enabled = !isEmpty;
		m.AddOption( "Paste", "content_paste", action: Paste );
		m.AddSeparator();
		m.AddOption( "Clear", "backspace", action: Clear ).Enabled = !isEmpty;

		m.OpenAtCursor( false );
		e.Accepted = true;
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( ReadOnly ) return;

		Browse();
	}

	void Browse()
	{
		var path = EditorUtility.OpenFileDialog( $"Select {SerializedProperty.DisplayName}", attribute?.Extension ?? "", SerializedProperty.GetValue<string>( "" ) );
		if ( string.IsNullOrEmpty( path ) )
			return;

		SetPath( path );
	}

	void SetPath( string path )
	{
		SerializedProperty.Parent?.NoteStartEdit( SerializedProperty );
		SerializedProperty.SetValue( path );
		SerializedProperty.Parent?.NoteFinishEdit( SerializedProperty );

		Update();
	}

	bool CanAssign( string path )
	{
		var extensions = (attribute?.Extension ?? "").Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
		if ( extensions.Length == 0 )
			return true;

		var ext = System.IO.Path.GetExtension( path ).TrimStart( '.' );
		return extensions.Contains( ext, StringComparer.OrdinalIgnoreCase );
	}

	public override void OnDragHover( DragEvent ev )
	{
		if ( !ev.Data.HasFileOrFolder || !CanAssign( ev.Data.FileOrFolder ) )
			return;

		ev.Action = DropAction.Link;
	}

	public override void OnDragDrop( DragEvent ev )
	{
		if ( !ev.Data.HasFileOrFolder || !CanAssign( ev.Data.FileOrFolder ) )
			return;

		SetPath( ev.Data.FileOrFolder );
		ev.Action = DropAction.Link;
	}

	void Copy()
	{
		var path = SerializedProperty.GetValue<string>( "" );
		if ( string.IsNullOrEmpty( path ) ) return;

		EditorUtility.Clipboard.Copy( path );
	}

	void Paste()
	{
		var path = EditorUtility.Clipboard.Paste();
		if ( string.IsNullOrEmpty( path ) ) return;

		SetPath( path );
	}

	void Clear()
	{
		SetPath( null );
	}
}
