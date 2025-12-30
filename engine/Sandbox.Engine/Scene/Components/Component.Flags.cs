namespace Sandbox;

[Flags]
public enum ComponentFlags
{
	None = 0,

	/// <summary>
	/// Hide this component in component inspector
	/// </summary>
	Hidden = 1,

	/// <summary>
	/// Don't save this component to disk
	/// </summary>
	NotSaved = 2,

	/// <summary>
	/// There's something wrong with this
	/// </summary>
	Error = 4,

	/// <summary>
	/// Loading something
	/// </summary>
	Loading = 8,// not implemented

	/// <summary>
	/// Is in the process of deserializing
	/// </summary>
	Deserializing = 16,

	/// <summary>
	/// Cannot be edited in the component inspector
	/// </summary>
	NotEditable = 32, // not implemented

	/// <summary>
	/// Keep local - don't network this component as part of the scene snapshot
	/// </summary>
	NotNetworked = 64,

	/// <summary>
	/// In the process of refreshing from the network
	/// </summary>
	[Obsolete]
	Refreshing = 128,

	/// <summary>
	/// Don't serialize this component when cloning
	/// </summary>
	NotCloned = 256,

	/// <summary>
	/// Can edit advanced properties in the component inspector
	/// </summary>
	ShowAdvancedProperties = 512,
}

public partial class Component
{
	[ActionGraphInclude]
	public ComponentFlags Flags { get; set; } = ComponentFlags.None;
}
