namespace Editor;

[Icon( "gradient" )]
public partial class GradientEditorWidget : Widget
{
	public Action<Gradient> ValueChanged { get; set; }

	GradientStopWidget alphaBar;
	GradientStopWidget colorBar;

	public bool IsColorSelection { get; private set; }
	GradientStopWidget.Point selectedPoint;

	[Title( "Color" ), ColorUsage( hasAlpha: false )]
	public Color ColorValue
	{
		get
		{
			if ( selectedPoint is null || !IsColorSelection )
				return Color.White;

			return selectedPoint.Value;
		}
		set
		{
			if ( selectedPoint is null || !IsColorSelection )
				return;

			selectedPoint.Value = value.WithAlpha( 1.0f );
			UpdateFromPoints();
		}
	}

	[Range( 0.0f, 1.0f ), Title( "Alpha" )]
	public float AlphaValue
	{
		get
		{
			if ( selectedPoint is null || IsColorSelection )
				return 0.0f;

			return selectedPoint.Value.a;
		}
		set
		{
			if ( selectedPoint is null || IsColorSelection )
				return;

			selectedPoint.Value = new Color( 0, 0, 0, value );
		}
	}

	[Range( 0.0f, 1.0f, slider: false ), Step( 0.01f ), Title( "Location" )]
	public float TimeValue
	{
		get => selectedPoint?.Time ?? 0.0f;
		set
		{
			if ( selectedPoint is null )
				return;

			selectedPoint.Time = value;
		}
	}


	Label labelMultiple;
	ControlWidget editColor;
	ControlWidget editAlpha;
	ControlWidget editPosition;

	Gradient _value;
	/// <summary>
	/// The current color value
	/// </summary>
	public Gradient Value
	{
		get => _value;

		set
		{
			_value = value;
			Update();
			UpdatePoints();
		}
	}

	public SerializedProperty SerializedProperty { get; set; }

	public GradientEditorWidget( Widget parent = null ) : base( parent )
	{
		_value = new Gradient( new Gradient.ColorFrame( 0, Color.White ), new Gradient.ColorFrame( 1, Color.Black ) );

		Layout = Layout.Column();
		FocusMode = FocusMode.Click;

		labelMultiple = Layout.Add( new Label( this ) );
		labelMultiple.Text = "Multiple Values Selected. Making changes will modify all.";
		labelMultiple.SetStyles( $"color: {Theme.MultipleValues.Hex};" );
		labelMultiple.Visible = false;

		alphaBar = Layout.Add( new GradientStopWidget( this ) );
		alphaBar.OnAddPoint = ( f ) =>
		{
			_value.AddAlpha( f, _value.Evaluate( f ).a );
			UpdatePoints();
			OnEdited();
		};

		Layout.Add( new GradientAreaWidget( this ) );

		colorBar = Layout.Add( new GradientStopWidget( this ) );
		colorBar.OnAddPoint = ( f ) =>
		{
			_value.AddColor( f, _value.Evaluate( f ).WithAlpha( 1 ) );
			UpdatePoints();
			OnEdited();
		};

		Layout.AddSpacingCell( 6 );

		var so = this.GetSerialized();
		so.OnPropertyChanged = OnColorEdited;

		var row = Layout.AddRow();
		var controls = Layout.Grid();
		controls.Margin = 8;
		controls.Spacing = 8;
		row.AddLayout( controls );
		row.AddStretchCell( 1 );

		{
			editPosition = controls.AddCell( 0, 0, new FloatControlWidget( so.GetProperty( "TimeValue" ) ) { Label = null, Icon = "timeline" } );
			editPosition.Enabled = false;
			editPosition.MaximumWidth = 300;

			var options = controls.AddCell( 1, 0, Layout.Row(), alignment: TextFlag.Left );
			options.Spacing = 8;

			var delete = options.Add( new IconButton( "delete", DeletePoint ) );
			delete.ToolTip = "Remove";
			delete.Bind( "Enabled" ).ReadOnly().From( () => selectedPoint is not null, null );

			options.AddStretchCell( 1 );

			var selectNext = options.Add( new IconButton( "chevron_left", () => SelectNext( false ) ) { ToolTip = "Select previous" } );
			selectNext.Bind( "Enabled" ).ReadOnly().From( () => selectedPoint is not null, null );

			var selectPrev = options.Add( new IconButton( "chevron_right", () => SelectNext( true ) ) { ToolTip = "Select next" } );
			selectPrev.Bind( "Enabled" ).ReadOnly().From( () => selectedPoint is not null, null );

			options.Add( new IconButton( "more_horiz", DoMoreOptionsMenu ) );
		}

		{
			editColor = controls.AddCell( 0, 1, new ColorControlWidget( so.GetProperty( "ColorValue" ) ) );
			editColor.Enabled = false;
			editColor.MaximumWidth = 300;

			editAlpha = controls.AddCell( 1, 1, new FloatControlWidget( so.GetProperty( "AlphaValue" ) ) { Label = "a" } );
			editAlpha.Enabled = false;
			editAlpha.MaximumWidth = 300;
		}

		Layout.Add( new GradientPresets( this ), 1 );
	}

	private void SelectNext( bool forward )
	{
		if ( selectedPoint is null )
			return;

		int currentIdx = selectedPoint.Index;

		if ( IsColorSelection )
		{
			int nextIdx = (currentIdx + (forward ? 1 : -1) + colorBar.Points.Count()) % colorBar.Points.Count();
			UpdateSelection( colorBar.Points[nextIdx], true );
		}
		else
		{
			int nextIdx = (currentIdx + (forward ? 1 : -1) + alphaBar.Points.Count()) % alphaBar.Points.Count();
			UpdateSelection( alphaBar.Points[nextIdx], false );
		}
	}

	private void DoMoreOptionsMenu()
	{
		var menu = new ContextMenu( this );

		menu.AddOption( new Option( "Reverse", "repeat", () =>
		{
			colorBar.Points.Reverse();
			for ( int i = 0; i < colorBar.Points.Count(); i++ )
			{
				colorBar.Points[i].Time = 1.0f - colorBar.Points[i].Time;
				colorBar.Points[i].Index = i;
			}

			alphaBar.Points.Reverse();
			for ( int i = 0; i < alphaBar.Points.Count(); i++ )
			{
				alphaBar.Points[i].Time = 1.0f - alphaBar.Points[i].Time;
				alphaBar.Points[i].Index = i;
			}

			UpdateFromPoints();
		} ) );
		menu.AddOption( new Option( "Distribute Evenly", "balance", () =>
		{
			for ( int i = 0; i < colorBar.Points.Count(); i++ )
			{
				colorBar.Points[i].Time = (float)i / Math.Max( 1, colorBar.Points.Count - 1 );
			}
			for ( int i = 0; i < alphaBar.Points.Count(); i++ )
			{
				alphaBar.Points[i].Time = (float)i / Math.Max( 1, alphaBar.Points.Count - 1 );
			}
			UpdateFromPoints();
		} ) );
		menu.AddOption( new Option( "Remove All", "delete_sweep", () =>
		{
			Value = new Gradient( new Gradient.ColorFrame( 0, Color.White ) );
		} ) );

		menu.OpenAtCursor();
	}

	protected override void OnPaint()
	{
		labelMultiple.Visible = SerializedProperty?.IsMultipleDifferentValues ?? false;
	}

	void OnEdited()
	{
		Update();
	}

	public override void OnDestroyed()
	{
		ValueChanged?.Invoke( _value );

		base.OnDestroyed();
	}

	[Shortcut( "editor.delete", "DEL" )]
	void DeletePoint()
	{
		if ( selectedPoint is null )
			return;

		alphaBar.Points.Remove( selectedPoint );
		colorBar.Points.Remove( selectedPoint );

		UpdateSelection( null, false );
		UpdateFromPoints();
	}

	private void OnColorEdited( SerializedProperty property )
	{
		UpdateFromPoints();
	}

	bool skipUpdatePoints;

	void UpdatePoints()
	{
		if ( skipUpdatePoints ) return;

		alphaBar.Points.Clear();
		colorBar.Points.Clear();

		for ( int i = 0; i < Value.Alphas?.Count; i++ )
		{
			var p = new GradientStopWidget.Point
			{
				Index = i,
				Time = Value.Alphas[i].Time,
				Paint = PaintAlpha,
				Value = new Color( 0, 0, 0, Value.Alphas[i].Value ),
				Moved = ( p ) => UpdateFromPoints(),
				Pressed = p => UpdateSelection( p, false )
			};

			alphaBar.Points.Add( p );
		}

		for ( int i = 0; i < Value.Colors?.Count; i++ )
		{
			var index = i;
			var p = new GradientStopWidget.Point
			{
				Index = i,
				Time = Value.Colors[i].Time,
				Paint = PaintColor,
				Moved = ( time ) => UpdateFromPoints(),
				Value = Value.Colors[i].Value,
				Pressed = p => UpdateSelection( p, true )
			};

			colorBar.Points.Add( p );
		}

		alphaBar.Update();
		colorBar.Update();

		UpdateSelection( null, false );
	}

	private void UpdateFromPoints()
	{
		var val = Value;

		val.Colors = val.Colors?.Clear();
		val.Alphas = val.Alphas?.Clear();

		foreach ( var p in colorBar.Points )
		{
			if ( p.Disabled ) continue;
			val.AddColor( p.Time, p.Value );
		}

		foreach ( var p in alphaBar.Points )
		{
			if ( p.Disabled ) continue;
			val.AddAlpha( p.Time, p.Value.a );
		}

		skipUpdatePoints = true;
		Value = val;
		skipUpdatePoints = false;
	}

	void PaintAlpha( GradientStopWidget.Point p )
	{
		var box = p.Rect.Shrink( 0, 2, 0, p.Rect.Width );

		if ( selectedPoint == p )
		{
			Paint.SetPen( Theme.Blue, 4 );
			Paint.ClearBrush();
			Paint.DrawPolygon( box.TopLeft, box.TopRight, box.BottomRight, p.Rect.BottomLeft + new Vector2( p.Rect.Width * 0.5f, -2 ), box.BottomLeft );
		}

		Paint.SetPen( p.Value.a > .5f ? Color.Black : Color.White, 1 );
		Paint.SetBrush( (Color.White * p.Value.a).WithAlpha( 1 ) );
		Paint.DrawPolygon( box.TopLeft, box.TopRight, box.BottomRight, p.Rect.BottomLeft + new Vector2( p.Rect.Width * 0.5f, -2 ), box.BottomLeft );
	}

	void PaintColor( GradientStopWidget.Point p )
	{
		var box = p.Rect.Shrink( 0, p.Rect.Width, 0, 2 );

		if ( selectedPoint == p )
		{
			Paint.SetPen( Theme.Blue, 4 );
			Paint.ClearBrush();
			Paint.DrawPolygon( box.BottomLeft, box.BottomRight, box.TopRight, p.Rect.TopLeft + new Vector2( (p.Rect.Width * 0.5f), 2 ), box.TopLeft );
		}

		Paint.SetPen( p.Value.Luminance > .5f ? Color.Black : Color.White, 1 );
		Paint.SetBrush( p.Value.WithAlpha( 1 ) );
		Paint.DrawPolygon( box.BottomLeft, box.BottomRight, box.TopRight, p.Rect.TopLeft + new Vector2( (p.Rect.Width * 0.5f), 2 ), box.TopLeft );
	}

	void UpdateSelection( GradientStopWidget.Point p, bool color )
	{
		selectedPoint = p;
		IsColorSelection = color;

		editColor.Enabled = color && p is not null;
		editAlpha.Enabled = !color && p is not null;
		editPosition.Enabled = p is not null;

		Update();
	}

	[WidgetGallery]
	[Title( "Gradient Editor" )]
	[Icon( "gradient" )]
	internal static Widget WidgetGallery()
	{
		var canvas = new Widget( null );
		canvas.Layout = Layout.Column();

		var ged = new GradientEditorWidget( canvas );
		ged.Value = new Gradient( new Gradient.ColorFrame( 0, Color.White ), new Gradient.ColorFrame( 1, Color.White ) );

		canvas.Layout.Add( ged );
		canvas.Layout.AddStretchCell();
		return canvas;
	}

	/// <summary>
	/// Open a gradient editor popup
	/// </summary>
	public static void OpenPopup( GradientControlWidget parent, Gradient input, Action<Gradient> onChange )
	{
		var popup = new Dialog( parent );
		popup.Window.WindowFlags = WindowFlags.Dialog | WindowFlags.Customized | WindowFlags.WindowTitle | WindowFlags.CloseButton | WindowFlags.WindowSystemMenuHint;
		popup.Window.SetWindowIcon( "gradient" );
		popup.Window.WindowTitle = "Gradient Editor";
		popup.Window.Size = new( 500, 350 );
		popup.Layout = Layout.Column();
		popup.Layout.Margin = 8;

		var editor = popup.Layout.Add( new GradientEditorWidget( popup ), 1 );
		editor.SerializedProperty = parent.SerializedProperty;
		editor.Value = input;
		editor.ValueChanged = onChange;

		popup.Show();
	}

}

class GradientAreaWidget : Widget
{
	private GradientEditorWidget _gradientEditor;

	public GradientAreaWidget( GradientEditorWidget gradientEditor )
	{
		_gradientEditor = gradientEditor;

		FixedHeight = 64;
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		_gradientEditor.Value.PaintBlock( LocalRect.Grow( 0, 4 ).Shrink( 8, 0 ) );
	}

	protected override void OnMouseEnter()
	{
		base.OnMouseEnter();

		if ( _gradientEditor.IsColorSelection )
		{
			Cursor = CursorShape.BitmapCursor;
			PixmapCursor = Pixmap.FromFile( "cursors/eyedropper_centered.png" );
		}
		else
			Cursor = CursorShape.Arrow;
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( !_gradientEditor.IsColorSelection )
			return;

		var delta = (e.LocalPosition.x - 8) / (Width - 16.0f);
		_gradientEditor.ColorValue = _gradientEditor.Value.Evaluate( delta );
	}
}

class GradientStopWidget : Widget
{
	public Action<float> OnAddPoint;

	Point Pressed;
	Point Hovered;

	public class Point
	{
		public int Index { get; set; }
		public float Time { get; set; }
		public Action<Point> Paint { get; set; }
		public Action<Point> Pressed { get; set; }
		public Action<Point> Moved { get; set; }
		public Rect Rect { get; set; }
		public Color Value { get; set; }
		public bool Disabled { get; set; }
	}

	public List<Point> Points = new();

	public GradientStopWidget( Widget parent ) : base( parent )
	{
		FixedHeight = 18;
		MouseTracking = true;
	}

	protected override void OnPaint()
	{
		var w = 10;

		Paint.Antialiasing = true;

		foreach ( var p in Points )
		{
			if ( p.Disabled ) continue;

			var x = 8 + p.Time * (LocalRect.Width - 16.0f);
			p.Rect = new Rect( x - (w / 2), 0, w, Height );

			Paint.SetFlags( false, Hovered == p, Pressed == p, false, true );
			p.Paint?.Invoke( p );
		}
	}

	float LocalToTime( float local ) => (local - 8) / (Width - 16.0f);

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( Pressed is not null )
		{
			Pressed.Disabled = !LocalRect.Grow( 256, 16 ).IsInside( e.LocalPosition );
			Pressed.Time = LocalToTime( e.LocalPosition.x ).Clamp( 0, 1 );
			Pressed.Moved?.Invoke( Pressed );
			Cursor = CursorShape.Finger;
			Update();
			return;
		}

		Hovered = null;

		foreach ( var p in Points )
		{
			var x = 8 + p.Time * (LocalRect.Width - 16.0f);
			if ( MathF.Abs( x - e.LocalPosition.x ) < 5.0f )
			{
				Hovered = p;
			}
		}

		Cursor = Hovered == null ? CursorShape.Arrow : CursorShape.Finger;
		Update();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		var delta = (e.LocalPosition.x - 8) / (Width - 16.0f);

		if ( Hovered is not null )
		{
			Pressed = Hovered;
			Pressed?.Pressed?.Invoke( Pressed );
			return;
		}

		OnAddPoint?.Invoke( delta );

		Pressed = Points.FirstOrDefault( x => x.Time == delta );
		Pressed?.Pressed?.Invoke( Pressed );
		Update();
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( Pressed is not null && Pressed.Disabled )
		{
			Points.Remove( Pressed );
		}

		Pressed = null;
		Update();
	}

	protected override void OnMouseLeave()
	{
		Hovered = null;
		Update();
	}
}

public static class GradientExtensions
{
	public static void PaintBlock( this in Gradient gradient, Rect rect )
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

			float normalizedX = (x - rect.Left) / rect.Width;
			var c = gradient.Evaluate( normalizedX );
			Paint.SetBrush( c );
			Paint.DrawRect( new Rect( x, rect.Top, w, rect.Height ) );
		}
	}
}
