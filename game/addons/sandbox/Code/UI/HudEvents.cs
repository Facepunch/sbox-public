/// <summary>
/// The pieces of the HUD that can be hidden independently. Flags, so a listener can hide several at
/// once; <see cref="All"/> hides everything, which is what the camera weapon and freecam do.
/// </summary>
[Flags]
public enum HudElement
{
	None = 0,

	/// <summary>
	/// Health and armour.
	/// </summary>
	Vitals = 1 << 0,

	/// <summary>
	/// The active weapon's clip and reserve.
	/// </summary>
	Ammo = 1 << 1,

	/// <summary>
	/// The weapon hotbar.
	/// </summary>
	Inventory = 1 << 2,

	/// <summary>
	/// The kill feed.
	/// </summary>
	KillFeed = 1 << 3,

	/// <summary>
	/// The scoreboard (held on the Score bind).
	/// </summary>
	Scoreboard = 1 << 4,

	/// <summary>
	/// The active tool's name, description and action hints.
	/// </summary>
	ToolInfo = 1 << 5,

	/// <summary>
	/// The weapon's crosshair and the cloud-loading ring around it. Use prompts are <see cref="InteractionHints"/>.
	/// </summary>
	Crosshair = 1 << 6,

	/// <summary>
	/// Name and avatar floating over other players.
	/// </summary>
	Nameplates = 1 << 7,

	/// <summary>
	/// The "Owned by" label for the object you're looking at.
	/// </summary>
	OwnerLabel = 1 << 8,

	/// <summary>
	/// Toast notices in the corner.
	/// </summary>
	Notices = 1 << 9,

	/// <summary>
	/// Who is talking.
	/// </summary>
	Voices = 1 << 10,

	/// <summary>
	/// Recent ammo pickups above the ammo counter.
	/// </summary>
	AmmoPickups = 1 << 11,

	/// <summary>
	/// The chat box and the recent messages.
	/// </summary>
	Chat = 1 << 12,

	/// <summary>
	/// The use key prompt for what you're looking at.
	/// </summary>
	InteractionHints = 1 << 13,

	/// <summary>
	/// A tool's radial selection menu, like the Builder's shape wheel.
	/// </summary>
	ToolMenu = 1 << 14,

	All = ~0,
}

/// <summary>
/// Lets any component in the scene hide parts of the HUD. Asked once a frame by <see cref="Hud"/>
/// whenever a HUD panel wants to know if it may draw. Implement this on an addon component and add
/// the elements you want off screen: <c>hidden |= HudElement.Inventory | HudElement.Ammo</c> during a
/// build phase, or <c>hidden = HudElement.All</c> for a cutscene. Anything nobody hides stays visible.
/// <see cref="Player.WantsHideHud"/> (the camera weapon, freecam) hides everything through this same event.
/// </summary>
public interface IHudEvents : ISceneEvent<IHudEvents>
{
	void OnHudVisibility( ref HudElement hidden ) { }
}
