namespace Editor;

using Sandbox.Engine;

[CustomEditor( typeof( MaterialAccessor ) )]
public class MaterialAccessorControlWidget : ControlWidget
{
	public override bool IncludeLabel => false;

	public MaterialAccessorControlWidget( SerializedProperty property ) : base( property )
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
		Layout.Spacing = 2;

		var accessor = SerializedProperty.GetValue<MaterialAccessor>();

		for ( int i = 0; i < accessor.Count; i++ )
		{
			Layout.Add( new MaterialIndex( SerializedProperty, i ) );
		}
	}

	protected override void OnValueChanged()
	{
		Rebuild();
	}
}


file class MaterialIndex : Widget
{
	SerializedProperty prop;
	int index;

	IconButton revertButton;

	public MaterialIndex( SerializedProperty serialized, int index )
	{
		this.prop = serialized;
		this.index = index;

		FixedHeight = Theme.RowHeight;

		Layout = Layout.Row();
		Layout.Spacing = 2;

		var sp = TypeLibrary.CreateProperty( $"Index {index}", GetMaterialValue, SetMaterialValue, null, serialized.Parent );

		// Sorry design coherence I don't know what to use here

		var resourceWidget = Layout.Add( new ResourceControlWidget( sp ) );
		revertButton = Layout.Add( new IconButton( "replay", Revert ) { ToolTip = "Revert Override" } );
		revertButton.Enabled = HasOverride();
		revertButton.Bind( "Enabled" ).From( HasOverride, null );

		resourceWidget.Tint = Theme.TextLight;
		resourceWidget.Bind( "Tint" ).From( () => HasOverride() ? Theme.Green : Color.Gray, null );
	}

	bool HasOverride()
	{
		var p = prop.GetValue<MaterialAccessor>();
		return p.HasOverride( index );
	}

	void Revert()
	{
		SetMaterialValue( default );
	}

	Material GetMaterialValue()
	{
		var p = prop.GetValue<MaterialAccessor>();

		if ( p.HasOverride( index ) )
			return p.GetOverride( index );

		return p.GetOriginal( index );
	}

	void SetMaterialValue( Material value )
	{
		prop.GetValue<MaterialAccessor>().SetOverride( index, value );
	}
}
