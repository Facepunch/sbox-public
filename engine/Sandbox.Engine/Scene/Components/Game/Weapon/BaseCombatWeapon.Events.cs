namespace Sandbox;

/// <summary>
/// Well-known names of <see cref="BaseCombatWeapon.IEvents"/> events. Games may post their own names too.
/// </summary>
public static class WeaponEvents
{
	/// <summary>The weapon was taken in hand. Posted once its models exist and its deploy cooldown is set.</summary>
	public const string Deploy = "deploy";

	/// <summary>The weapon fired. Posted once per volley.</summary>
	public const string Attack = "attack";

	/// <summary>A reload started.</summary>
	public const string Reload = "reload";

	/// <summary>A reload was cancelled part-way.</summary>
	public const string ReloadCancel = "reload_cancel";

	/// <summary>The weapon was put away. Posted once its reload is cancelled and its models are gone.</summary>
	public const string Holster = "holster";
}

public partial class BaseCombatWeapon
{
	/// <summary>
	/// Weapon events, posted to the weapon's GameObject and its ancestors on every peer, dedicated servers
	/// included. The holder's animator implements this to react to what its weapon does.
	/// </summary>
	/// <remarks>
	/// A weapon that was just put away can still post, so check the weapon is the one you hold. Listeners that
	/// are presentation only should ignore events on a dedicated server themselves.
	/// </remarks>
	public interface IEvents : ISceneEvent<IEvents>
	{
		/// <summary>
		/// The weapon did something its holder may animate, named by <paramref name="name"/>, usually one of <see cref="WeaponEvents"/>.
		/// </summary>
		void OnWeaponEvent( BaseCombatWeapon weapon, string name ) { }
	}

	/// <summary>
	/// Post a <see cref="IEvents"/> event to the components on this weapon's GameObject and its ancestors.
	/// A listener may switch away from the weapon or destroy it, so check it is still valid afterwards.
	/// </summary>
	protected void PostWeaponEvent( string name )
	{
		NotePendingGesture( name );

		// Not enabled-only: a holstered weapon's GameObject is already disabled when it posts Holster.
		IEvents.PostToGameObject( GameObject, x => x.OnWeaponEvent( this, name ), FindMode.InSelf | FindMode.InAncestors );
	}

	// The gestures waiting for UpdateBodyAnimations on a body that is not a player's. A player keeps its own.
	[Flags]
	private enum PendingGestures
	{
		None = 0,
		Attack = 1,
		Reload = 2,
		Deploy = 4,
		ReloadCancel = 8
	}

	PendingGestures _pendingGestures;

	void ClearPendingGestures() => _pendingGestures = PendingGestures.None;

	void NotePendingGesture( string name )
	{
		if ( IsEvent( name, WeaponEvents.Deploy ) )
		{
			_pendingGestures = PendingGestures.Deploy;
		}
		else if ( IsEvent( name, WeaponEvents.Holster ) )
		{
			ClearPendingGestures();
		}
		else if ( IsEvent( name, WeaponEvents.Attack ) )
		{
			_pendingGestures = Application.IsDedicatedServer
				? _pendingGestures & ~PendingGestures.Attack
				: _pendingGestures | PendingGestures.Attack;
		}
		else if ( IsEvent( name, WeaponEvents.Reload ) )
		{
			_pendingGestures |= PendingGestures.Reload;
		}
		else if ( IsEvent( name, WeaponEvents.ReloadCancel ) )
		{
			_pendingGestures = (_pendingGestures & ~PendingGestures.Reload) | PendingGestures.ReloadCancel;
		}
	}

	static bool IsEvent( string name, string expected ) => string.Equals( name, expected, StringComparison.OrdinalIgnoreCase );
}
