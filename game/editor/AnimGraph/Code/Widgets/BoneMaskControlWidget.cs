namespace Editor.AnimGraph;

/// <summary>
/// ComboBox control widget listing the skeleton's bone masks. Nothing else resolves to a mask at
/// runtime, so nothing else is offered. Used via [Editor( "animgraph.bonemask" )] on strings.
/// </summary>
[CustomEditor( typeof( string ), NamedEditor = "animgraph.bonemask" )]
public class BoneMaskControlWidget : ControlWidget
{
	public BoneMaskControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();

		var comboBox = new ComboBox( this );
		var current = property.GetValue<string>() ?? "";
		var found = string.IsNullOrWhiteSpace( current );

		comboBox.AddItem( "None", "layers_clear", () => Pick( "" ), selected: found );

		foreach ( var mask in Masks( property ) )
		{
			var selected = string.Equals( current, mask, StringComparison.OrdinalIgnoreCase );
			found |= selected;

			comboBox.AddItem( mask, "layers", () => Pick( mask ), selected: selected );
		}

		if ( !found )
			comboBox.AddItem( $"{current} (missing)", "error", () => Pick( current ), selected: true );

		Layout.Add( comboBox, 1 );
	}

	/// <summary>
	/// Nothing recompiles the graph until the edit is finished, and a combo box has no focus to
	/// lose the way the pickers do, so say so as soon as something is picked.
	/// </summary>
	private void Pick( string mask )
	{
		SerializedProperty.SetValue( mask );
		PropertyFinishEdit();
	}

	/// <summary>
	/// The masks authored on the skeleton the node's graph animates. Empty until it has one.
	/// </summary>
	private static IEnumerable<string> Masks( SerializedProperty property )
	{
		var owners = property.Parent?.Targets;
		var node = owners?.OfType<Node>().FirstOrDefault()
			?? owners?.OfType<VariationDataNode.VariationData>().FirstOrDefault()?.Owner;

		return node?.Root?.TargetSkeleton is { Length: > 0 } path && Skeleton.Load( path ) is { } skeleton
			? skeleton.MaskNames
			: [];
	}
}
