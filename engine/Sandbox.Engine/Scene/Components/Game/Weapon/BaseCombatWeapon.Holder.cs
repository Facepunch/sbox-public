namespace Sandbox;

public partial class BaseCombatWeapon
{
	/// <summary>
	/// Something that holds and fires weapons. A component above the weapon in the hierarchy implements it,
	/// as <see cref="PlayerController"/> does, to give the weapon a body, an aim and someone to credit.
	/// </summary>
	/// <remarks>
	/// The holder keeps its weapons in a <see cref="BaseInventoryComponent"/>, which seeds their magazines, holds
	/// their reserve and deploys and drops them. The view model, the HUD and the camera hooks are for a holding
	/// player only.
	/// </remarks>
	public interface IHolder
	{
		/// <summary>
		/// The GameObject credited for the weapon's damage and ignored by its traces.
		/// </summary>
		GameObject GameObject { get; }

		/// <summary>
		/// The body renderer the weapon's world model attaches to.
		/// </summary>
		SkinnedModelRenderer Renderer { get; }

		/// <summary>
		/// Where the holder is aiming <paramref name="weapon"/>.
		/// </summary>
		Ray CalculateAimRay( BaseCombatWeapon weapon );
	}
}
