namespace Editor;

[CustomEditor( typeof( Gradient ) )]
public class GradientControlWidget : ControlWidget
{
	public override bool SupportsMultiEdit => true;

	public GradientControlWidget( SerializedProperty property ) : base( property )
	{
		SetSizeMode( SizeMode.Default, SizeMode.Default );

		Layout = Layout.Column();
		Layout.Spacing = 2;
		Cursor = CursorShape.Finger;
	}

	protected override void PaintOver()
	{
		Gradient v = SerializedProperty.GetValue<Gradient>();
		v.PaintBlock( LocalRect.Shrink( 2 ) );
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );

		if ( e.LeftMouseButton )
		{
			OpenPopup();
		}
	}

	private void OpenPopup()
	{
		Gradient v = SerializedProperty.GetValue<Gradient>();
		GradientEditorWidget.OpenPopup( this, v, x =>
		{
			if ( SerializedProperty.GetValue<Gradient>().Equals( x ) )
				return;

			SerializedProperty.SetValue( x );
			Update();
		} );
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		var m = new ContextMenu();

		m.AddOption( "Open in Editor", "edit", OpenPopup );

		m.AddSeparator();

		m.AddOption( "Copy", "content_copy", () =>
		{
			var json = JsonSerializer.Serialize( SerializedProperty.GetValue<Gradient>() );
			EditorUtility.Clipboard.Copy( json );
		} );

		var clipboard = EditorUtility.Clipboard.Paste();
		m.AddOption( "Paste", "content_paste", () =>
		{
			var value = JsonSerializer.Deserialize<Gradient>( clipboard );
			SerializedProperty.SetValue( value );
		} );

		m.OpenAtCursor( false );
		e.Accepted = true;
	}
}
