namespace Sandbox;

public partial class BaseInventoryComponent
{
	/// <summary>
	/// By default the inventory pumps the active item's control every frame in its own update. Turn
	/// this on to take over that timing yourself - the inventory will stop pumping and you call
	/// <see cref="Pump"/> from wherever you want (e.g. alongside your player's own input handling).
	/// </summary>
	[Property, Advanced] public bool ManualPumping { get; set; }

	protected override void OnUpdate()
	{
		if ( ManualPumping )
			return;

		Pump();
	}

	/// <summary>
	/// Drives the active item's per-frame control hook. The inventory calls this itself every frame
	/// unless <see cref="ManualPumping"/> is set, in which case you call it yourself. Only does
	/// anything on the client that owns the inventory - control is input, which is owner only.
	/// </summary>
	public void Pump()
	{
		if ( !ActiveItem.IsValid() )
			return;

		if ( ActiveItem.IsProxy )
			return;

		ActiveItem.Control();
	}
}
