using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// Data being dragged. Carries enough info for both the visual and the drop action.
/// </summary>
public class DragData
{
	/// <summary>
	/// The spawner type, e.g. "prop", "entity", "dupe".
	/// </summary>
	public string Type { get; set; }

	/// <summary>
	/// The cloud ident or resource path.
	/// </summary>
	public string Path { get; set; }

	/// <summary>
	/// The icon URL to display while dragging.
	/// </summary>
	public string Icon { get; set; }

	/// <summary>
	/// Optional display title.
	/// </summary>
	public string Title { get; set; }

	/// <summary>
	/// The panel that initiated the drag, so we can ignore it as a drop target.
	/// </summary>
	public Panel Source { get; set; }

	public object Data { get; set; }

	/// <summary>
	/// Stable spawnlist entry ID when reordering an editable spawnlist.
	/// </summary>
	public Guid SpawnlistItemId { get; set; }

	/// <summary>
	/// Set to true when a drop target has already handled this drag (e.g. slot-to-slot swap),
	/// so the source panel's OnDragEnd can skip secondary actions like dropping to the world.
	/// </summary>
	public bool Handled { get; set; }
}
