namespace Sandbox;

/// <summary>
/// Put this on a panel implementing <see cref="IInspectorEditor"/> to register it with the inspector.
/// </summary>
[AttributeUsage( AttributeTargets.Class )]
public class InspectorEditorAttribute : Attribute
{
	public Type Type { get; }

	public InspectorEditorAttribute( Type type = null )
	{
		Type = type;
	}
}
