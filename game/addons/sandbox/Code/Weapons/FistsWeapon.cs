[Title( "Fists" )]
public sealed class FistsWeapon : MeleeWeapon
{
	private bool _rightHand;

	public FistsWeapon()
	{
		DisplayName = "Fists";
		HoldType = WeaponHolds.Fists;
		Handedness = WeaponHandedness.Both;
		Range = 64f;
		Damage = 12f;
		SwingForce = 500f;
		SwingDelay = 0.4f;
		MissSwingDelay = 0.4f;
		PrimaryAutomatic = false;
		SecondaryAutomatic = false;
		DeployTime = 0.3f;
	}

	public override bool CanSecondaryAttack() => NextSecondaryFire <= 0 && CanPrimaryAttack();

	public override void PrimaryAttack() => Punch( false );
	public override void SecondaryAttack() => Punch( true );

	private void Punch( bool rightHand )
	{
		_rightHand = rightHand;
		base.PrimaryAttack();
		// The melee trace chooses the hit/miss recovery. Both hands share that recovery.
		SetNextSecondaryFire( NextPrimaryFire );
	}

	protected override void SwingEffects( ShotEffect shot )
	{
		PlayPunchEffects( shot, _rightHand );

		if ( Networking.IsHost )
		{
			using ( Rpc.FilterExclude( c => c == Network.Owner || c == Connection.Local ) )
				BroadcastPunchEffects( shot, _rightHand );
		}
		else if ( !IsProxy )
		{
			RelayPunchEffects( shot, _rightHand );
		}
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RelayPunchEffects( ShotEffect shot, bool rightHand )
	{
		PlayPunchEffects( shot, rightHand );
		using ( Rpc.FilterExclude( c => c == Network.Owner || c == Connection.Local ) )
			BroadcastPunchEffects( shot, rightHand );
	}

	[Rpc.Broadcast]
	private void BroadcastPunchEffects( ShotEffect shot, bool rightHand ) => PlayPunchEffects( shot, rightHand );

	private void PlayPunchEffects( ShotEffect shot, bool rightHand )
	{
		if ( Application.IsDedicatedServer ) return;

		// Send the hand with the swing, rather than racing a synced property against the effects RPC.
		var attack = rightHand ? 1f : 0f;
		HolderRenderer?.Animation.Parameters.TrySet( "holdtype_attack", attack );

		ViewModel?.RunEvent<ViewModel>( model => model.Renderer?.Animation.Parameters.TrySet( "holdtype_attack", attack ) );

		base.OnShootEffects( shot );
	}

	// Fists have no physical pickup to throw into the world.
	protected override bool OnDrop() => false;
}
