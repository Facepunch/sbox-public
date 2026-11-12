namespace Editor;

[CustomEditor( typeof( ulong ), WithAllAttributes = new[] { typeof( Model.BodyGroupMaskAttribute ) } )]
public class BodyGroupsControlWidget : ControlWidget
{
	// garry: I hate this, the way it looks, the way it works.
	//		  We should make it a single informational line, that pops up
	//		  a selector with more details and edits when you click it.

	public override bool SupportsMultiEdit => true;

	public BodyGroupsControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Grid();
		Layout.Spacing = 2;

		AcceptDrops = true;

		Rebuild();
	}

	protected override void OnPaint()
	{

	}

	Model Model
	{
		get
		{
			if ( !SerializedProperty.TryGetAttribute<Model.BodyGroupMaskAttribute>( out var attr ) )
				return null;

			var modelProperty = SerializedProperty.Parent.GetProperty( attr.ModelParameter );
			if ( modelProperty is null )
				return null;

			if ( modelProperty.PropertyType == typeof( string ) )
			{
				var str = modelProperty.GetValue<string>();
				if ( string.IsNullOrWhiteSpace( str ) ) return default;
				return Model.Load( str );
			}

			return modelProperty.GetValue<Model>( null );
		}
	}

	/// <summary>
	/// Gets common body parts from multiple selected objects
	/// </summary>
	private HashSet<Model.BodyPart> GetCommonBodyParts()
	{
		HashSet<Model.BodyPart> commonBodyParts = null;

		foreach ( var property in SerializedProperty.MultipleProperties )
		{
			if ( !property.TryGetAttribute<Model.BodyGroupMaskAttribute>( out var attr ) )
				continue;

			var prop = property.Parent.GetProperty( attr.ModelParameter );
			if ( prop is null )
				continue;

			var model = prop.GetValue<Model>( null );

			//
			// Has no body parts, return empty set
			//
			if ( model is null || model.Parts.All.Count() == 0 )
				return new();

			var modelBodyParts = new HashSet<Model.BodyPart>( model.Parts.All.Where( x => x.Choices.Count > 1 ) );

			if ( commonBodyParts == null )
			{
				commonBodyParts = modelBodyParts;
			}
			else
			{
				commonBodyParts.IntersectWith( modelBodyParts );
			}

			if ( commonBodyParts.Count == 0 )
				break;
		}

		return commonBodyParts ?? new();
	}

	public void Rebuild()
	{
		Layout.Clear( true );

		IEnumerable<Model.BodyPart> bodyParts;

		if ( SerializedProperty.IsMultipleValues )
		{
			bodyParts = GetCommonBodyParts();
		}
		else
		{
			//
			// Single selection - get body parts directly from the model
			//
			var model = Model;
			if ( model is null )
			{
				Layout.Add( new Label( "No Model" ) );
				return;
			}

			bodyParts = model.Parts.All.Where( x => x.Choices.Count > 1 );
		}

		var totalParts = bodyParts.Sum( x => x.Choices.Count );
		if ( totalParts <= 1 )
		{
			Layout.Add( new Label( "None" ) );
			return;
		}

		var v = SerializedProperty.GetValue<ulong?>() ?? Model?.Parts.DefaultMask ?? 0;

		var grid = Layout as GridLayout;
		grid.HorizontalSpacing = 8;
		grid.Spacing = 2;

		int i = 0;
		foreach ( var part in bodyParts )
		{
			if ( part.Choices.Count <= 1 )
				continue;

			grid.AddCell( 0, i, new Label( $"{part.Name}:", this ) );
			var select = grid.AddCell( 1, i, new ComboBox( this ) );

			foreach ( var c in part.Choices )
			{
				select.AddItem( $"{c.Name}", onSelected: () => SetModelMask( part, c ), selected: (v & part.Mask) == c.Mask && !SerializedProperty.IsMultipleDifferentValues );
			}

			i++;
		}

		grid.SetColumnStretch( 0, 1 );

		FixedHeight = (Theme.RowHeight + grid.Spacing) * i;
		grid.Margin = new( 0, 0, 0, 2 );
	}

	protected override void OnValueChanged()
	{
		Rebuild();
	}

	private void SetModelMask( Model.BodyPart part, Model.BodyPart.Choice c )
	{
		PropertyStartEdit();

		var v = SerializedProperty.GetValue<ulong?>() ?? Model?.Parts.DefaultMask ?? 0;

		v = v & ~part.Mask;
		v = v | c.Mask;

		SerializedProperty.SetValue( v );

		PropertyFinishEdit();
	}

	protected override int ValueHash
	{
		get
		{
			var hc = new HashCode();
			hc.Add( base.ValueHash );
			hc.Add( Model );

			if ( Model is not null )
			{
				foreach ( var val in Model.Parts.All )
				{
					hc.Add( val );
				}
			}

			return hc.ToHashCode();
		}
	}
}
