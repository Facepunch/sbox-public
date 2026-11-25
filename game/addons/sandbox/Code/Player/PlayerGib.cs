/// <summary>
/// Describes a player gib.
/// </summary>
public sealed class PlayerGib : Component
{
	[Property] public TagSet GibTags { get; set; }
	[Property] public GameObject Bone { get; set; }
	[Property] public GameObject Effect { get; set; }

	[Button]
	public void Gib( Vector3 origin, Vector3 hitPos, bool noShrink = false )
	{
		if ( !Game.IsPlaying ) return;

		GameObject.Enabled = true;
		GameObject.Tags.Add( "effect" );

		if ( !noShrink && Bone.IsValid() )
		{
			Bone.Flags = GameObject.Flags.WithFlag( GameObjectFlags.ProceduralBone, true );
			Bone.WorldScale = 0;
			WorldPosition = Bone.WorldPosition + Vector3.Down * 64f;
		}


		// Unparent
		GameObject.SetParent( null, true );

		var effect = GameObject.AddComponent<TemporaryEffect>();
		effect.DestroyAfterSeconds = 10;

		var rb = GetComponent<Rigidbody>( true );
		rb.Enabled = true;

		var force = (origin - hitPos).Normal * 4096f;
		rb.ApplyForce( force * 100f );

		if ( Effect.IsValid() )
		{
			Effect?.Clone( hitPos );
		}
	}
}
