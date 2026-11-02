namespace Editor.AnimGraph;

/// <summary>
/// Dropdown for ID parameter values backed by an enum - the options come from
/// the parameter's <see cref="Parameter.Options"/>.
/// </summary>
[CustomEditor( typeof( string ), NamedEditor = "animgraph.options" )]
public class IDOptionsControlWidget : DropdownControlWidget<string>
{
	public IDOptionsControlWidget( SerializedProperty property ) : base( property )
	{
	}

	protected override IEnumerable<object> GetDropdownValues()
		=> SerializedProperty.Parent?.Targets?.OfType<Parameter>().FirstOrDefault()?.Options ?? [];
}
