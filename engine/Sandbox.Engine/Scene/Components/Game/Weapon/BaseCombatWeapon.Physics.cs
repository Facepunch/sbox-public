namespace Sandbox;

public partial class BaseCombatWeapon
{
	// Whether the weapon's own physics was last switched off for being held. One that was never held stays as authored.
	bool _heldPhysics;
	bool _worldPhysicsChanged;

	// Retained on the host so late-joining peers can restore the pickup's original settings.
	[Sync( SyncFlags.FromHost ), Change( nameof( OnWorldPhysicsChanged ) ), Expose] private bool WorldInterpolation { get; set; } = true;
	[Sync( SyncFlags.FromHost ), Change( nameof( OnWorldPhysicsChanged ) ), Expose] private Guid[] DisabledWorldPhysics { get; set; }

	[Expose]
	private void OnWorldPhysicsChanged()
	{
		_worldPhysicsChanged = true;
		QueueHeldPhysics();
	}

	internal override void OnEnabledInternal()
	{
		base.OnEnabledInternal();
		QueueHeldPhysics();
	}

	internal override void OnParentChangedInternal( GameObject oldParent, GameObject newParent )
	{
		base.OnParentChangedInternal( oldParent, newParent );
		QueueHeldPhysics();
	}

	/// <summary>
	/// Run <see cref="UpdateHeldPhysics"/> once whatever is being enabled around us has had its turn. A body or a
	/// collider switched off part way through the pass that enables it stays half set up: a body left in the world,
	/// a collider left on a body, its own or its holder's.
	/// </summary>
	void QueueHeldPhysics()
	{
		using ( CallbackBatch.Batch() )
		{
			CallbackBatch.Add( CommonCallback.Dirty, UpdateHeldPhysics, this, nameof( UpdateHeldPhysics ) );
		}
	}

	internal override void SlotChanged( int oldSlot, int newSlot )
	{
		// Taken out of an inventory with its physics off: a holder had it, also where this peer never saw it
		// deployed, having joined late or loaded it that way
		if ( oldSlot >= 0 && newSlot < 0 && IsPhysicsOff() )
			_heldPhysics = true;

		QueueHeldPhysics();
	}

	// True when the weapon has a body or colliders of its own and none of them is on
	bool IsPhysicsOff()
	{
		var any = false;

		if ( GetComponent<Rigidbody>( true ) is { } body )
		{
			if ( body.Enabled )
				return false;

			any = true;
		}

		foreach ( var collider in GetComponents<Collider>( true ) )
		{
			if ( collider.Enabled )
				return false;

			any = true;
		}

		return any;
	}

	/// <summary>
	/// A held weapon follows its holder, so the body and colliders on its own object are off and its transform is
	/// not interpolated. Dropping restores their previous settings on every peer.
	/// </summary>
	void UpdateHeldPhysics()
	{
		if ( GameObject is null || Scene is null || Scene.IsEditor )
			return;

		var held = IsHeld;
		var restore = _worldPhysicsChanged && !held;
		_worldPhysicsChanged = false;
		if ( held == _heldPhysics && !restore )
			return;

		_heldPhysics = held;

		var body = GetComponent<Rigidbody>( true );
		var colliders = GetComponents<Collider>( true ).ToArray();

		if ( held && Networking.IsHost )
		{
			WorldInterpolation = Network.Interpolation;
			List<Guid> disabled = null;
			if ( body is { Enabled: false } )
				(disabled ??= []).Add( body.Id );
			foreach ( var collider in colliders )
			{
				if ( !collider.Enabled )
					(disabled ??= []).Add( collider.Id );
			}
			DisabledWorldPhysics = disabled?.ToArray();
		}

		// Restore the body before colliders, and disable it after them.
		if ( !held && body is not null )
			body.Enabled = WasEnabled( body );

		foreach ( var collider in colliders )
			collider.Enabled = !held && WasEnabled( collider );

		if ( held && body is not null )
			body.Enabled = false;

		Network.Interpolation = !held && WorldInterpolation;
	}

	bool WasEnabled( Component component ) => DisabledWorldPhysics is null || Array.IndexOf( DisabledWorldPhysics, component.Id ) < 0;
}
