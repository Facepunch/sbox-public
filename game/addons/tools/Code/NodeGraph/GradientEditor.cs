namespace Editor.NodeEditor;

public class GradientEditor : ValueEditor
{
	public string Title { get; set; }
	public Gradient Value { get; set; }
	public NodeUI Node { get; set; }

	public GradientEditor( GraphicsItem parent ) : base( parent )
	{
		HoverEvents = true;
		Cursor = CursorShape.Finger;
	}

	protected override void OnPaint()
	{
		if ( !Enabled )
			return;

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		var bg = Theme.ControlBackground.WithAlpha( 0.4f );
		var fg = Theme.TextControl;

		if ( !Paint.HasMouseOver )
		{
			bg = bg.Darken( 0.1f );
			fg = fg.Darken( 0.1f );
		}

		var rect = LocalRect.Shrink( 1 );
		Paint.ClearPen();
		Paint.SetBrush( bg );
		Paint.DrawRect( rect, 2 );

		PaintBlock( Value, rect.Shrink( 2 ) );
	}

	private void PaintBlock( Gradient gradient, Rect rect )
	{
		Paint.ClearPen();
		Paint.Antialiasing = false;

		Paint.SetBrush( "/image/transparent-small.png" );
		Paint.DrawRect( rect );

		float pixelWidth = 1;

		// this is kind of a lazy way of doing it but
		// it works and is accurate as can be so who cares
		for ( float x = (int)rect.Left; x <= (int)rect.Right; x += pixelWidth )
		{
			float w = pixelWidth;

			if ( x + pixelWidth > rect.Right )
				w = rect.Right - x;

			float normalizedX = ( x - rect.Left ) / rect.Width;
			var c = gradient.Evaluate( normalizedX );
			Paint.SetBrush( c );
			Paint.DrawRect( new Rect( x, rect.Top, w, rect.Height ) );
		}
	}

	protected override void OnMousePressed( GraphicsMouseEvent e )
	{
		base.OnMousePressed( e );

		if ( !Enabled )
			return;

		if ( !e.LeftMouseButton )
			return;

		if ( !LocalRect.IsInside( e.LocalPosition ) )
			return;

		var view = Node.GraphicsView;
		var position = view.ToScreen( view.FromScene( ToScene( new Vector2( Size.x + 1, 1 ) ) ) );
		

		OpenGradientEditorPopup( ( v ) =>
		{
			Value = v;
			Node.Graph.ChildValuesChanged( null );
			Node.Update();

		}, position );

		e.Accepted = true;
	}

	private GradientEditorWidget OpenGradientEditorPopup( Action<Gradient> onChange, Vector2? position = null )
	{
		var popup = new PopupWidget( null );
		popup.WindowTitle = "Gradient Editor";
		popup.SetWindowIcon( "gradient" );
		popup.Layout = Layout.Column();
		popup.Layout.Margin = 8;
		popup.FixedHeight = 350;
		popup.FixedWidth = 500;
		popup.Position = position ?? Application.CursorPosition;

		var editor = popup.Layout.Add( new GradientEditorWidget( popup ), 1 );
		editor.Value = Value;
		editor.ValueChanged += ( v ) => onChange?.Invoke( v );

		popup.Show();
		popup.ConstrainToScreen();

		return editor;
	}
}