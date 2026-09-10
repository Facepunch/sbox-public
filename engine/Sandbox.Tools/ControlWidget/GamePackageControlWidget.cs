namespace Editor;

[CustomEditor( typeof( string ), NamedEditor = "package:game" )]
public class GamePackageControlWidget : ControlWidget
{
	public Package CurrentPackage { get; set; }
	public override bool IsControlButton => !IsControlDisabled;

	public GamePackageControlWidget( SerializedProperty property ) : base( property )
	{
		HorizontalSizeMode = SizeMode.CanGrow | SizeMode.Expand;
		Cursor = CursorShape.Finger;
		MouseTracking = true;

		var ident = property.GetValue<string>( null );
		if ( !string.IsNullOrEmpty( ident ) )
		{
			GetPackageFromIdent( ident );
		}
	}

	protected override void PaintControl()
	{
		var rect = new Rect( 0, Size );

		var iconRect = rect.Shrink( 3 );
		iconRect.Width = iconRect.Height;

		rect.Left = iconRect.Right + 10;

		Paint.ClearPen();
		Paint.SetBrush( Theme.SurfaceBackground.WithAlpha( 0.2f ) );
		Paint.DrawRect( iconRect, 2 );

		var alpha = IsControlDisabled ? 0.6f : 1f;

		var textRect = rect.Grow( 4, 1, 0, 0 );
		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			Paint.Draw( iconRect, CurrentPackage?.Thumb );

			Paint.SetDefaultFont();
			Paint.SetPen( Theme.MultipleValues.WithAlpha( alpha ) );
			Paint.DrawText( textRect, $"Multiple Values", TextFlag.LeftCenter );
		}
		else
		{
			Paint.Draw( iconRect, CurrentPackage?.Thumb );

			Paint.SetDefaultFont();
			Paint.SetPen( Theme.TextControl.WithAlpha( alpha ) );
			Paint.DrawText( textRect, CurrentPackage?.Title ?? "No package selected...", TextFlag.LeftCenter );
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( ReadOnly ) return;

		var ident = SerializedProperty.GetValue<string>( null );

		PropertyStartEdit();

		var picker = new PackageSelector( this, "type:game", ( packages ) =>
		{
			var package = packages.FirstOrDefault();
			if ( package is not null )
			{
				SerializedProperty.SetValue( package.FullIdent );
				GetPackageFromIdent( package.FullIdent );
			}
			PropertyFinishEdit();
		}, [CurrentPackage] );
		picker.WindowTitle = $"Select {SerializedProperty.DisplayName}";
		picker.Show();
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		var m = new ContextMenu();

		var ident = SerializedProperty.GetValue<string>( null );

		m.AddOption( "Copy", "file_copy", action: Copy ).Enabled = !string.IsNullOrEmpty( ident );
		m.AddOption( "Paste", "content_paste", action: Paste );
		m.AddSeparator();
		m.AddOption( "Clear", "backspace", action: Clear ).Enabled = !string.IsNullOrEmpty( ident );

		m.OpenAtCursor( false );
		e.Accepted = true;
	}
	void Copy()
	{
		var ident = SerializedProperty.GetValue<string>( null );
		if ( ident == null ) return;

		EditorUtility.Clipboard.Copy( ident );
	}

	void Paste()
	{
		var ident = EditorUtility.Clipboard.Paste();
		if ( !string.IsNullOrEmpty( ident ) )
		{
			GetPackageFromIdent( ident );
		}
	}

	void Clear()
	{
		SerializedProperty.SetValue( (string)null );
		CurrentPackage = null;
	}

	async void GetPackageFromIdent( string ident )
	{
		SerializedProperty.Parent.NoteStartEdit( SerializedProperty );
		SerializedProperty.SetValue( ident );
		SerializedProperty.Parent.NoteFinishEdit( SerializedProperty );
		CurrentPackage = await Package.Fetch( ident, true );
	}
}
