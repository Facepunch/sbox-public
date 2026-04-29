namespace Sandbox;

/// <summary>
/// Stores editor selection sets on the scene so they serialize into SceneProperties/GameObjectSystems.
/// </summary>
[Expose]
public sealed class SelectionSetsSystem : GameObjectSystem
{
	[Property]
	public SelectionSetsData Data { get; set; } = new();

	public SelectionSetsSystem( Scene scene ) : base( scene )
	{
	}

	public sealed class SelectionSetsData
	{
		public List<SelectionSetEntry> SelectionSets { get; set; } = [];
	}

	public sealed class SelectionSetEntry
	{
		public string Name { get; set; } = string.Empty;
		public List<Guid> ObjectIds { get; set; } = [];
		public bool Enabled { get; set; } = true;
	}
}
