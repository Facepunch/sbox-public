
namespace Editor;

[CustomEditor( typeof( SkinnedModelRenderer.MorphAccessor ) )]
public class MorphCollectionControlWidget : ControlWidget
{
	public override bool IncludeLabel => false;

	bool sortAlphabetically = EditorCookie.Get( "morph_sort_alphabetically", false );

	public MorphCollectionControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Column();

		Rebuild();
	}

	protected override void OnPaint()
	{
	}

	public void Rebuild()
	{
		Layout.Clear( true );

		var v = SerializedProperty.GetValue<SkinnedModelRenderer.MorphAccessor>();

		if ( v.Names is null ) return;
		if ( v.Names.Length == 0 ) return;

		if ( !SerializedProperty.TryGetAsObject( out var so ) )
			return;

		var headerButtons = Layout.AddRow();
		headerButtons.Spacing = 8;

		var grid = Layout.AddColumn();

		var unprocessed = v.Names.ToList();

		List<MorphRowWidget> rows = new List<MorphRowWidget>();

		foreach ( var group in unprocessed.GroupBy( x => x[..^1] ).ToArray() )
		{
			if ( group.Count() != 2 ) continue;
			if ( !group.Any( x => x.ToLower().EndsWith( 'l' ) ) && !group.Any( x => x.ToLower().EndsWith( 'r' ) ) ) continue;

			// this is a left and right

			unprocessed.RemoveAll( x => group.Contains( x ) );

			var ctrl = new MorphRowWidget( SerializedProperty, group.Order().ToArray(), group.Key );
			rows.Add( ctrl );
		}

		foreach ( var name in unprocessed )
		{
			var ctrl = new MorphRowWidget( SerializedProperty, new[] { name }, name );
			rows.Add( ctrl );
		}

		var groups = rows.GroupBy( x => GetGroup( x.Title ) );

		if ( sortAlphabetically )
			groups = groups.OrderBy( x => x.Key );

		foreach ( var rowGroup in groups )
		{
			grid.AddSpacingCell( 8 );
			grid.Add( new Label( rowGroup.Key ) );

			IEnumerable<MorphRowWidget> groupRows = sortAlphabetically
				? rowGroup.OrderBy( x => x.Title )
				: rowGroup;

			foreach ( var row in groupRows )
			{
				grid.Add( row );
			}
		}

		grid.Margin = new( 0, 0, 0, 2 );

		var clearBtn = headerButtons.Add( new IconButton( "replay", () => rows.ForEach( x => x.Clear() ) ) );
		clearBtn.ToolTip = "Clear All";

		var randomBtn = headerButtons.Add( new IconButton( "casino", () => rows.ForEach( x => x.SetValues( Random.Shared.Float( 0, 1 ), Random.Shared.Float( 0, 1 ) ) ) ) );
		randomBtn.ToolTip = "Randomize";

		headerButtons.AddStretchCell();

		var sortBtn = headerButtons.Add( new IconButton( "sort_by_alpha" ) );
		sortBtn.ToolTip = "Sort Alphabetically";
		sortBtn.IsToggle = true;
		sortBtn.IsActive = sortAlphabetically;
		sortBtn.OnToggled = ( active ) =>
		{
			sortAlphabetically = active;
			EditorCookie.Set( "morph_sort_alphabetically", sortAlphabetically );
			Rebuild();
		};
	}

	protected override void OnValueChanged()
	{
		Rebuild();
	}

	string GetGroup( string title )
	{
		if ( title.Contains( "brow", StringComparison.OrdinalIgnoreCase ) ) return "Eyes";
		if ( title.Contains( "eye", StringComparison.OrdinalIgnoreCase ) ) return "Eyes";
		if ( title.Contains( "cheek", StringComparison.OrdinalIgnoreCase ) ) return "Face";
		if ( title.Contains( "nose", StringComparison.OrdinalIgnoreCase ) ) return "Face";
		if ( title.Contains( "nostril", StringComparison.OrdinalIgnoreCase ) ) return "Face";
		if ( title.Contains( "chin", StringComparison.OrdinalIgnoreCase ) ) return "Face";
		if ( title.Contains( "jaw", StringComparison.OrdinalIgnoreCase ) ) return "Mouth";
		if ( title.Contains( "lip", StringComparison.OrdinalIgnoreCase ) ) return "Mouth";
		if ( title.Contains( "mouth", StringComparison.OrdinalIgnoreCase ) ) return "Mouth";

		return "Misc";
	}
}

class MorphRowWidget : Widget
{
	string[] keyNames;

	SkinnedModelRenderer.MorphAccessor accessor; // todo - support multiple?

	FloatSlider slider;
	FloatSlider sliderSide;

	public string Title;

	private SerializedProperty[] _properties;

	public MorphRowWidget( SerializedProperty serializedProperty, string[] names, string title )
	{
		accessor = serializedProperty.GetValue<SkinnedModelRenderer.MorphAccessor>();
		keyNames = names;

		var so = accessor.GetSerialized();

		so.ParentProperty = serializedProperty;

		_properties = names
			.Select( x => TypeLibrary.CreateProperty<float>( x, () => accessor.Get( x ), value => accessor.Set( x, value ), null, so ) )
			.ToArray();

		title = title.Replace( "lower", "Lower" );
		title = title.Replace( "upper", "Upper" );
		title = title.Replace( "raiser", "Raiser" );
		title = title.Replace( "inflate", "Inflate" );
		title = title.Replace( "bulge", "Bulge" );
		title = title.Replace( "suck", "Suck" );
		title = title.Replace( "thrust", "Thrust" );
		title = title.Replace( "sideways", "Sideways" );
		title = title.Replace( "depressor", "Depressor" );
		title = title.Replace( "corner", "Corner" );
		title = title.Replace( "puller", "Puller" );
		title = title.Replace( "pucker", "Pucker" );
		title = title.Replace( "wrinkle", "Wrinkle" );
		title = title.Replace( "jaw", "Jaw" );
		title = title.Replace( "mouth", "Mouth" );

		Title = title.ToTitleCase();
		FixedHeight = 20;

		Layout = Layout.Row();
		Layout.AddSpacingCell( 150 );

		slider = Layout.Add( new FloatSlider( this ), 3 );
		slider.HighlightColor = Theme.Green;
		slider.OnValueEdited += OnSliderEdited;
		slider.Minimum = 0;
		slider.Maximum = 1;
		slider.ToolTip = "Strength";

		if ( names.Length > 1 )
		{
			Layout.AddSpacingCell( 8 );
			sliderSide = Layout.Add( new FloatSlider( this ), 1 );
			sliderSide.HighlightColor = Theme.Yellow;
			sliderSide.Value = 0;
			sliderSide.OnValueEdited += OnSliderEdited;
			sliderSide.Minimum = -1;
			sliderSide.Maximum = 1;
			sliderSide.ToolTip = "Left/Right";
		}

		UpdateValueFromSlider();
	}

	private void DispatchPreEdited()
	{
		for ( var i = 0; i < keyNames.Length; i++ )
		{
			_properties[i].DispatchPreEdited();
		}
	}

	private void DispatchEdited()
	{
		for ( var i = 0; i < keyNames.Length; i++ )
		{
			_properties[i].DispatchEdited();
		}
	}

	private void OnSliderEdited()
	{
		DispatchPreEdited();

		if ( keyNames.Length > 1 )
		{
			accessor.Set( keyNames[0], slider.Value * sliderSide.Value.Remap( 0, -1, 1, 0 ) );
			accessor.Set( keyNames[1], slider.Value * sliderSide.Value.Remap( 0, 1, 1, 0 ) );
		}
		else
		{
			foreach ( var key in keyNames )
			{
				accessor.Set( key, slider.Value );
			}
		}

		DispatchEdited();

		Update();
	}

	private float GetOverrideValue( string name )
	{
		if ( !accessor.ContainsOverride( name ) )
			return 0;

		return accessor.Get( name );
	}

	private void UpdateValueFromSlider()
	{
		if ( keyNames.Length > 1 )
		{
			var l = GetOverrideValue( keyNames[0] );
			var r = GetOverrideValue( keyNames[1] );

			slider.Value = MathF.Max( l, r );

			if ( r > l ) sliderSide.Value = l.Remap( 0, r, -1, 0 );
			else if ( l > r ) sliderSide.Value = r.Remap( 0, l, 1, 0 );
			else sliderSide.Value = 0;
		}
		else
		{
			slider.Value = GetOverrideValue( keyNames[0] );
		}
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		bool active = accessor.ContainsOverride( keyNames[0] );
		if ( active )
		{
			Clear();
		}

		e.Accepted = true;
	}

	public void Clear()
	{
		DispatchPreEdited();

		foreach ( var key in keyNames )
			accessor.Clear( key );

		DispatchEdited();

		UpdateValueFromSlider();
		Update();
	}

	public void SetValue( float value )
	{
		DispatchPreEdited();

		foreach ( var key in keyNames )
			accessor.Set( key, value );

		DispatchEdited();

		UpdateValueFromSlider();
		Update();
	}

	public void SetValues( params float[] values )
	{
		DispatchPreEdited();

		for ( var i = 0; i < keyNames.Length; ++i )
		{
			if ( i < values.Length )
			{
				accessor.Set( keyNames[i], values[i] );
			}
			else
			{
				accessor.Clear( keyNames[i] );
			}
		}

		DispatchEdited();

		UpdateValueFromSlider();
		Update();
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		bool active = accessor.ContainsOverride( keyNames[0] );

		Paint.Pen = active ? Theme.Text : Theme.Text.WithAlpha( 0.2f );
		Paint.DrawText( LocalRect, Title, TextFlag.LeftCenter );
	}
}
