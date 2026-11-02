namespace Editor.AnimGraph;

/// <summary>
/// Picker for bone name properties - the skeleton comes from the graph's target model.
/// </summary>
[CustomEditor( typeof( string ), NamedEditor = "animgraph.bone" )]
public class BoneNameControlWidget : BonePickerControlWidget
{
	public BoneNameControlWidget( SerializedProperty property ) : base( property )
	{
	}
}
