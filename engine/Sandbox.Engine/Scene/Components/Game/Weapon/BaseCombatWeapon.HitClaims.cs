namespace Sandbox;

public partial class BaseCombatWeapon
{
	//
	// Client-authoritative hit registration, modelled on Rust. The owning client traces its shots
	// against the world it sees - so hits land exactly where the shooter aimed, with no lag
	// compensation - and reports each trigger pull to the host as one claim carrying every pellet
	// that hit. The host applies the damage. The host stays the authority because it decides whether
	// to honour the claim: OnValidateShotClaim is the seam where a game rejects the impossible ones.
	//

	/// <summary>
	/// One pellet's hit inside a <see cref="ShotClaim"/> - where it was fired from, what it hit and
	/// where. Pellets that missed, or hit nothing that can take damage or a push, aren't claimed.
	/// </summary>
	public record struct PelletClaim(
		Vector3 Origin,
		Vector3 Position,
		Vector3 Direction,
		GameObject HitObject,
		TagSet Tags );

	/// <summary>
	/// An owning client's report of one trigger pull - every pellet that hit something, and the
	/// damage and force the weapon says each pellet carries. Everything in it is client-reported.
	/// On claimed hits <see cref="DamageInfo.Hitbox"/> is null - hitgroups arrive as tags on each
	/// pellet (e.g. "head") instead, resolved on the client.
	/// </summary>
	public record struct ShotClaim(
		int Sequence,
		float Damage,
		float Force,
		PelletClaim[] Pellets );

	// Numbers this weapon's claims so the host can tell shots apart - rate and duplicate checks in a
	// validator key on it.
	int _shotSequence;

	/// <summary>
	/// Builds the claim for one traced pellet - false when the hit isn't worth claiming: it missed,
	/// or hit nothing that can take damage or a push. Component presence replicates, so checking here
	/// saves the host a pointless message.
	/// </summary>
	bool TryBuildPelletClaim( in SceneTraceResult tr, TagSet tags, bool hitboxTags, out PelletClaim pellet )
	{
		pellet = default;

		if ( !tr.Hit || !tr.GameObject.IsValid() )
			return false;

		if ( tr.GameObject.GetComponentInParent<IDamageable>() is null && tr.GameObject.GetComponentInParent<Rigidbody>() is null )
			return false;

		// Resolve the hitbox tags here - the host can't reconstruct the hitbox from a claim.
		pellet = new PelletClaim( tr.StartPosition, tr.HitPosition, tr.Direction, tr.GameObject, MergeHitboxTags( tr, tags, hitboxTags ) ?? new TagSet() );
		return true;
	}

	void SendShotClaim( float damage, float force, PelletClaim[] pellets )
	{
		ClaimShot( new ShotClaim( _shotSequence++, damage, force, pellets ) );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void ClaimShot( ShotClaim claim )
	{
		// Basic wire validity - not anticheat, just hygiene no game should have to opt into. A
		// malformed float must never reach the damage or physics systems (a NaN impulse corrupts the
		// body for everyone), and negative damage would heal through IDamageable.
		if ( claim.Pellets is null || claim.Pellets.Length == 0 )
			return;

		if ( !float.IsFinite( claim.Damage ) || claim.Damage < 0 )
			return;

		if ( !float.IsFinite( claim.Force ) || claim.Force < 0 )
			return;

		if ( !OnValidateShotClaim( claim ) )
			return;

		foreach ( var pellet in claim.Pellets )
		{
			if ( !pellet.HitObject.IsValid() )
				continue;

			if ( pellet.Origin.IsNaN || pellet.Origin.IsInfinity )
				continue;

			if ( pellet.Position.IsNaN || pellet.Position.IsInfinity )
				continue;

			if ( pellet.Direction.IsNaN || pellet.Direction.IsInfinity )
				continue;

			// Normalized so a huge-but-finite claimed direction can't multiply the impulse past any
			// force clamp a validator applies.
			ApplyBulletDamage( pellet.HitObject, pellet.Origin, pellet.Position, pellet.Direction.Normal, claim.Damage, claim.Force, Attacker, GameObject, pellet.Tags );
		}
	}

	/// <summary>
	/// The host's chance to reject a shot claim before it deals damage - the anticheat seam. Beyond
	/// basic wire validity (NaN, negative damage - always enforced), a claim is entirely
	/// client-reported: a cheating owner can send any origin, targets, damage or tags that serialize.
	/// Base accepts everything. To harden, override and check the claim against what's possible -
	/// clamp <see cref="ShotClaim.Damage"/>/<see cref="ShotClaim.Force"/> against the weapon's own
	/// config, the pellet count against <see cref="BallisticConfig.Pellets"/>, the claim rate (via
	/// <see cref="ShotClaim.Sequence"/> and arrival times) against the fire rate, each origin against
	/// where the shooter can be, distances against the weapon's range, and line of sight from origin
	/// to hit. Reject the impossible, forgive the borderline - latency makes honest claims imprecise.
	/// </summary>
	protected virtual bool OnValidateShotClaim( in ShotClaim claim ) => true;
}
