using Editor.AnimGraph.Nodes;

namespace Editor.AnimGraph;

/// <summary>
/// ComboBox control widget listing the graph's parameters of the node's type.
/// Used via [Editor( "AnimGraphParameterName" )] on string properties.
/// </summary>
[CustomEditor( typeof( string ), NamedEditor = "AnimGraphParameterName" )]
public class ParameterNameWidget : ControlWidget
{
	public ParameterNameWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();

		var comboBox = new ComboBox( this );
		var current = property.GetValue<string>() ?? "";
		var target = property.Parent?.Targets?.FirstOrDefault();
		var parameters = target switch
		{
			ParameterNode node => node.Root?.AllParameters.Where( parameter => parameter.Type == node.ParameterType ) ?? [],
			IDBasedSelectorNode idSelector => idSelector.GetAvailableParameters(),
			_ => []
		};

		var found = string.IsNullOrWhiteSpace( current );

		if ( target is IDBasedSelectorNode currentSelector )
		{
			comboBox.AddItem( "Select parameter...", "arrow_drop_down", () =>
			{
				currentSelector.ClearParameter();
				property.Parent.NoteChanged( property );
				property.Parent.NoteFinishEdit( property );
			}, selected: found );
		}

		foreach ( var parameter in parameters )
		{
			var name = parameter.Name;
			var selected = string.Equals( current, name, StringComparison.OrdinalIgnoreCase );
			found |= selected;

			comboBox.AddItem( name, AnimParameterUI.Icon( parameter.Type ), () =>
			{
				switch ( target )
				{
					case ParameterNode node:
						node.ParameterIdentifier = parameter.Identifier;
						property.SetValue( name );
						break;
					case IDBasedSelectorNode selectedSelector when parameter is Parameter controlParameter:
						selectedSelector.SelectParameter( controlParameter );
						property.Parent.NoteChanged( property );
						property.Parent.NoteFinishEdit( property );
						break;
				}
			}, selected: selected );
		}

		if ( !found && !string.IsNullOrEmpty( current ) )
			comboBox.AddItem( $"{current} (missing)", "error", () => property.SetValue( current ), selected: true );

		Layout.Add( comboBox, 1 );
	}
}
