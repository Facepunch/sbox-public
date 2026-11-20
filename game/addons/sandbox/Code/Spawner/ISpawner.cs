/// <summary>
/// Describes something that can be spawned into the world
/// Implementations handle their own preview rendering and spawn logic.
/// </summary>
public interface ISpawner
{
	/// <summary>
	/// Display name shown in the HUD while holding this payload.
	/// </summary>
	string DisplayName { get; }

	/// <summary>
	/// A full ident if it's a sbox.game thing
	/// </summary>
	string FullIdent => null;

	/// <summary>
	/// Icon path for this payload, used for inventory display via <c>thumb:path</c>.
	/// </summary>
	string Icon { get; }

	/// <summary>
	/// The local-space bounds of the thing being spawned, used to offset placement so it sits on surfaces.
	/// </summary>
	BBox Bounds { get; }

	/// <summary>
	/// Whether all required resources (packages, models, etc.) are loaded and ready to place.
	/// </summary>
	bool IsReady { get; }

	/// <summary>
	/// A task that completes with true when loading succeeds, or false if it fails.
	/// </summary>
	Task<bool> Loading { get; }

	/// <summary>
	/// The raw data needed to reconstruct this spawner (e.g. a cloud ident, or JSON).
	/// </summary>
	string Data { get; }

	/// <summary>
	/// Extra data resolved on the requesting client that the host might not be able to work out itself
	/// (e.g. bounds and game title of mounted content). Passed alongside <see cref="Data"/> when networking.
	/// </summary>
	string Metadata => null;

	/// <summary>
	/// The unspawned prefab GameObject, if available. Allows inspecting components before spawning.
	/// Returns null for spawners that don't use prefabs (e.g. props, duplicator).
	/// </summary>
	GameObject Prefab => null;

	/// <summary>
	/// Populate a right-click context menu with spawner-specific options.
	/// Override in spawner implementations to add custom menu items.
	/// </summary>
	void PopulateContextMenu( Sandbox.UI.Menu menu, string ident ) { }

	/// <summary>
	/// Draw a ghost preview at the given world transform.
	/// </summary>
	void DrawPreview( Transform transform, Material overrideMaterial );

	/// <summary>
	/// Actually spawn the thing at the given transform. Called on the host.
	/// Returns the root GameObject(s) that were spawned so they can be added to undo.
	/// </summary>
	Task<List<GameObject>> Spawn( Transform transform, Player player );
}
