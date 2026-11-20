public sealed class BalloonEntity : Component, Component.IDamageable
{
	[Property] public PrefabFile PopEffect { get; set; }
	[Property] public SoundEvent PopSound { get; set; }
	[RequireComponent] public Prop Prop { get; set; }

	void IDamageable.OnDamage( in DamageInfo damage )
	{
		if ( IsProxy ) return;

		damage.Attacker?.GetComponent<Player>()?.PlayerData?.AddStat( "balloon.popped" );

		Pop();
	}

	[Rpc.Host]
	private void Pop()
	{
		if ( PopEffect.IsValid() )
		{
			var effect = GameObject.Clone( PopEffect, new CloneConfig { Transform = WorldTransform, StartEnabled = false } );

			foreach ( var tintable in effect.GetComponentsInChildren<ITintable>( true ) )
			{
				tintable.Color = Prop.Tint;
			}

			// Beef up the gib burst
			foreach ( var gibs in effect.GetComponentsInChildren<ParticleEffect>( true ).Where( x => x.GameObject.Name == "Gibs" ) )
			{
				gibs.MaxParticles = 40;

				foreach ( var emitter in gibs.GetComponents<ParticleConeEmitter>( true ) )
				{
					emitter.Burst = 35;
				}
			}

			effect.NetworkSpawn( true, null );
		}

		if ( PopSound is null )
		{
			PopSound = ResourceLibrary.Get<SoundEvent>( "entities/balloon/sounds/balloon_pop.sound" );
		}

		if ( PopSound is not null )
		{
			Sound.Play( PopSound, WorldPosition );
		}

		GameObject.Destroy();
	}
}
