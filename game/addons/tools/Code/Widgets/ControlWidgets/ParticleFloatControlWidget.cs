namespace Editor;

[CustomEditor( typeof( ParticleFloat ) )]
public class ParticleFloatControlWidget : ControlWidget
{
	public Color HighlightColor { get; set; }
	public string Label { get; set; }

	Layout ControlArea;

	SerializedObject Target;

	Button ModeSwitchButton;

	public ParticleFloatControlWidget( SerializedProperty property ) : this( property, "f", Theme.Green )
	{
	}

	public ParticleFloatControlWidget( SerializedProperty property, string label, Color color ) : base( property )
	{
		SetSizeMode( SizeMode.Ignore, SizeMode.Default );

		if ( !property.TryGetAsObject( out Target ) )
			return;

		Label = label;
		HighlightColor = color;

		Layout = Layout.Row();
		Layout.Spacing = 3;

		Layout.AddStretchCell();

		ModeSwitchButton = new Button();
		ModeSwitchButton.Text = "Mode";
		ModeSwitchButton.OnPaintOverride = PaintButton;
		ModeSwitchButton.Pressed = () => OpenPopup( ModeSwitchButton.ScreenRect );
		ModeSwitchButton.FixedWidth = Theme.RowHeight;

		Layout.Add( ModeSwitchButton );

		ControlArea = Layout.AddRow( 1 );
		ControlArea.Spacing = 2;

		Target.OnPropertyChanged += ( p ) =>
		{
			if ( p.Name != "Type" && p.Name != "Evaluation" ) return;

			Rebuild();
		};

		Rebuild();

	}

	private void OpenPopup( Rect parentRect )
	{
		var popup = new ParticleFloatConfigPopup( Target, this );
		popup.Position = parentRect.BottomRight;
		popup.AdjustSize();
		popup.Position -= new Vector2( popup.Width, 0 );

		popup.Show();
		popup.ConstrainToScreen();
	}

	bool PaintButton()
	{
		Paint.Antialiasing = true;

		if ( Paint.HasPressed )
		{
			Paint.SetBrushAndPen( Theme.ControlBackground.Lighten( 0.3f ) );
			Paint.DrawRect( Paint.LocalRect.Shrink( 0.5f ), Theme.ControlRadius );
			Paint.Pen = Theme.TextControl.Lighten( 0.4f );
		}
		else if ( Paint.HasMouseOver )
		{
			Paint.SetBrushAndPen( Theme.TextControl.Lighten( 0.2f ).WithAlpha( 0.1f ) );
			Paint.DrawRect( Paint.LocalRect.Shrink( 1 ), Theme.ControlRadius );

			Paint.Pen = Theme.TextControl.Lighten( 0.5f );
		}
		else
		{
			Paint.Pen = Theme.TextControl.WithAlpha( 0.5f );
		}

		var type = Target.GetProperty( "Type" ).GetValue<ParticleFloat.ValueType>();
		var eval = Target.GetProperty( "Evaluation" ).GetValue<ParticleFloat.EvaluationType>();

		var icon = "people";
		float iconSize = 15;

		if ( type == ParticleFloat.ValueType.Constant )
		{
			icon = "radio_button_unchecked";
			Paint.Pen = Paint.Pen.WithAlpha( 0.3f );
			iconSize = 11;
		}
		else
		{
			if ( eval == ParticleFloat.EvaluationType.Seed )
			{
				icon = "scatter_plot";
			}

			if ( eval == ParticleFloat.EvaluationType.Life )
			{
				icon = "play_arrow";
			}

			if ( eval == ParticleFloat.EvaluationType.Frame )
			{
				icon = "casino";
			}
		}

		var iconRect = Paint.DrawIcon( Paint.LocalRect, icon, iconSize, TextFlag.Center );

		return true;
	}

	void Rebuild()
	{
		RebuildForType( Target.GetProperty( "Type" ).GetValue<ParticleFloat.ValueType>(), Target.GetProperty( "Evaluation" ).GetValue<ParticleFloat.EvaluationType>() );
	}

	string GetTypeName( ParticleFloat.ValueType type, ParticleFloat.EvaluationType eval )
	{
		switch ( type )
		{
			case ParticleFloat.ValueType.Constant:
				return "Constant value";
			case ParticleFloat.ValueType.Curve:
				{
					switch ( eval )
					{
						case ParticleFloat.EvaluationType.Seed:
							return "Random from curve";
						case ParticleFloat.EvaluationType.Frame:
							return "Random from curve (per frame)";
						case ParticleFloat.EvaluationType.Life:
							return "Curve over lifetime";
						default:
							return "Unknown";
					}
				}
			case ParticleFloat.ValueType.CurveRange:
				switch ( eval )
				{
					case ParticleFloat.EvaluationType.Seed:
						return "Random from range";
					case ParticleFloat.EvaluationType.Frame:
						return "Random from range (per frame)";
					case ParticleFloat.EvaluationType.Life:
						return "Path between curves over lifetime";
					default:
						return "Unknown";
				}
			case ParticleFloat.ValueType.Range:
				{
					switch ( eval )
					{
						case ParticleFloat.EvaluationType.Seed:
							return "Random between range";
						case ParticleFloat.EvaluationType.Frame:
							return "Random between range (per frame)";
						case ParticleFloat.EvaluationType.Life:
							return "Lerp between range over lifetime";
						default:
							return "Unknown";
					}
				}
		}

		return "Unknown Combo";
	}

	void RebuildForType( ParticleFloat.ValueType type, ParticleFloat.EvaluationType eval )
	{
		ControlArea.Clear( true );

		ModeSwitchButton.ToolTip = GetTypeName( type, eval );

		if ( type == ParticleFloat.ValueType.Constant )
		{
			var control = new FloatControlWidget( Target.GetProperty( "ConstantValue" ) ) { HighlightColor = HighlightColor, Label = Label };
			ControlArea.Add( control );
		}

		if ( type == ParticleFloat.ValueType.Range )
		{
			var controlA = new FloatControlWidget( Target.GetProperty( "ConstantA" ) ) { HighlightColor = HighlightColor, Label = Label };
			ControlArea.Add( controlA );

			var controlB = new FloatControlWidget( Target.GetProperty( "ConstantB" ) ) { HighlightColor = HighlightColor, Label = Label };
			ControlArea.Add( controlB );
		}

		if ( type == ParticleFloat.ValueType.Curve )
		{
			var controlA = new CurveControlWidget( Target.GetProperty( "CurveA" ) ) { HighlightColor = HighlightColor };
			ControlArea.Add( controlA );
		}

		if ( type == ParticleFloat.ValueType.CurveRange )
		{
			var controlA = new CurveRangeControlWidget( Target.GetProperty( "CurveRange" ) ) { HighlightColor = HighlightColor };
			ControlArea.Add( controlA );
		}

		Update();
	}

	protected override void OnPaint()
	{

	}

}

/// <summary>
/// A popup that lets you choose between modes for a Particle Float Controller.
/// We can offer more configuration here, eventually.. but for now we're keeping it simple.
/// </summary>
file class ParticleFloatConfigPopup : PopupWidget
{
	SerializedObject SerializedObject;
	SerializedProperty Type;
	SerializedProperty Eval;

	public ParticleFloatConfigPopup( SerializedObject target, Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 8;
		Layout.Margin = 16;

		SerializedObject = target;
		Type = target.GetProperty( "Type" );
		Eval = target.GetProperty( "Evaluation" );

		AddQuickModes( Type.GetValue<ParticleFloat.ValueType>(), Eval.GetValue<ParticleFloat.EvaluationType>() );

		// tODO - make this window fucking awesome

		//	var dropDown = Create( Type );
		//	dropDown.FixedWidth = 100;
		//	Layout.Add( dropDown );

		//	var evalDropDown = Create( Eval );
		//	evalDropDown.FixedWidth = 100;
		//	Layout.Add( evalDropDown );
	}

	void AddQuickModes( ParticleFloat.ValueType type, ParticleFloat.EvaluationType eval )
	{
		var grid = new GridLayout();
		grid.Spacing = 8;

		grid.AddCell( 0, 0, MakeQuickMode( "radio_button_unchecked", "Constant", "Value is constant. It stays the same. It doesn't change", ParticleFloat.ValueType.Constant, ParticleFloat.EvaluationType.Life ) );
		grid.AddCell( 1, 0, MakeQuickMode( "casino", "Random", "Choose a value between two constants every frame", ParticleFloat.ValueType.Range, ParticleFloat.EvaluationType.Frame ) );

		grid.AddCell( 0, 1, MakeQuickMode( "hdr_strong", "Range", "Choose a value between two constants", ParticleFloat.ValueType.Range, ParticleFloat.EvaluationType.Seed ) );
		grid.AddCell( 1, 1, MakeQuickMode( "animation", "Lerp", "Lerp between two values over the lifetime", ParticleFloat.ValueType.Range, ParticleFloat.EvaluationType.Life ) );

		grid.AddCell( 0, 2, MakeQuickMode( "show_chart", "Curve", "Get the value by querying a curve over the lifetime", ParticleFloat.ValueType.Curve, ParticleFloat.EvaluationType.Life ) );
		grid.AddCell( 1, 2, MakeQuickMode( "area_chart", "Curve with Range", "Choose a path between two curves over the lifetime", ParticleFloat.ValueType.CurveRange, ParticleFloat.EvaluationType.Life ) );

		Layout.Add( grid );
	}

	private Widget MakeQuickMode( string icon, string label, string description, ParticleFloat.ValueType t, ParticleFloat.EvaluationType e )
	{
		var b = new Widget();

		b.Cursor = CursorShape.Finger;
		b.Layout = new IconTitleDescriptionLayout( icon, label, description );
		b.Layout.Margin = new Sandbox.UI.Margin( 8, 4 );
		b.SetStyles( "color: #ffffff;" );

		bool isCurrent = t == Type.GetValue<ParticleFloat.ValueType>() && e == Eval.GetValue<ParticleFloat.EvaluationType>();

		b.MouseClick += () =>
		{
			Type.SetValue( t );
			Eval.SetValue( e );
			Close();
		};

		b.OnPaintOverride = () =>
		{
			if ( isCurrent || Paint.HasMouseOver )
			{
				Paint.SetBrushAndPen( Theme.Blue.Darken( 0.5f ).WithAlpha( 0.5f ) );
				Paint.DrawRect( Paint.LocalRect, 4 );
			}

			return true;
		};

		return b;
	}
}

file class IconTitleDescriptionLayout : GridLayout
{
	public IconTitleDescriptionLayout( string icon, string title, string description )
	{
		VerticalSpacing = 0;
		HorizontalSpacing = 8;

		var iconLabel = AddCell( 0, 0, new IconButton( icon ) { Background = Color.Transparent, IconSize = 33, FixedSize = 40, TransparentForMouseEvents = true }, ySpan: 2 );
		var titleLabel = AddCell( 1, 0, new Label( title ) );
		var descLabel = AddCell( 1, 1, new Label( description ) { WordWrap = true } );

		titleLabel.SetStyles( "font-size: 13px; font-family: Poppins; font-weight: bold;" );
		descLabel.SetStyles( "font-size: 9px; font-family: Poppins;" );
		descLabel.SetEffectOpacity( 0.5f );

	}
}
