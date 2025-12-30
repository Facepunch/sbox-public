namespace Editor.TerrainEditor;

public class BrushSettings
{
	[Property, Range( 8, 1024 ), Step( 1 ), WideMode] public int Size { get; set; } = 200;
	[Property, Range( 0.0f, 1.0f ), Step( 0.01f ), WideMode] public float Opacity { get; set; } = 0.5f;
}

public class BrushSettingsWidgetWindow : WidgetWindow
{
	class BrushSelectedWidget : Widget
	{
		public BrushSelectedWidget( Widget parent ) : base( parent )
		{
			MinimumSize = new( 48, 48 );
			Cursor = CursorShape.Finger;
		}

		protected override void OnPaint()
		{
			base.OnPaint();

			Paint.Antialiasing = true;

			Paint.ClearPen();
			Paint.DrawRect( LocalRect );

			var pixmap = TerrainEditorTool.Brush.Pixmap;
			Paint.Draw( LocalRect.Contain( pixmap.Size ), pixmap );
		}

		protected override void OnMouseClick( MouseEvent e )
		{
			var popup = new PopupWidget( null );
			popup.Position = Application.CursorPosition;
			popup.Visible = true;
			popup.Layout = Layout.Column();
			popup.Layout.Margin = 10;
			popup.MaximumSize = new Vector2( 300, 150 );

			var list = new BrushListWidget();
			list.BrushSelected += () => { popup.Close(); Update(); };
			popup.Layout.Add( list );
		}
	}

	public BrushSettingsWidgetWindow( Widget parent, SerializedObject so ) : base( parent, "Brush Settings" )
	{
		Layout = Layout.Row();
		Layout.Margin = 8;
		MaximumWidth = 400.0f;

		var cs = new ControlSheet();
		cs.AddObject( so );
		Layout.Add( new BrushSelectedWidget( this ) );

		var l = Layout.Row();
		l.Add( cs );

		Layout.Add( l );
	}
}
