/// <summary>
/// A projectile. It explodes when it hits something.
/// </summary>
public class ProjectileEntity : Component, Component.ICollisionListener
{
	[RequireComponent] public Rigidbody Rigidbody { get; set; }

	[Sync( SyncFlags.FromHost )] public Player Instigator { get; set; }

	protected TimeSince TimeSinceCreated;

	protected override void OnStart()
	{
		Tags.Add( "projectile" );
	}

	protected override void OnEnabled()
	{
		TimeSinceCreated = 0;
	}

	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( IsProxy )
			return;

		var player = collision.Other.GameObject.GetComponentInParent<Player>();
		if ( Instigator.IsValid() && player.IsValid() && player == Instigator )
		{
			return;
		}

		OnHit( collision );
	}

	protected virtual void OnHit( Collision collision = default )
	{
		GameObject.Destroy();
	}

	/// <summary>
	/// Tell the instigator their projectile hit, so they can do things like show a hitmarker
	/// </summary>
	protected void NotifyHit( IDamageable target, Vector3 point )
	{
		if ( !Instigator.IsValid() ) return;

		var connection = Instigator.Network.Owner;
		if ( connection is null ) return;

		// TODO: implement me
	}

	/// <summary>
	/// Spawns the medium explosion here, credited to the <see cref="Instigator"/>, and destroys the
	/// projectile. Host only - the explosion is network spawned.
	/// </summary>
	protected void Explode( float radius, float damage, float forceScale )
	{
		var explosion = Explosion.Spawn( new ExplosionEvent
		{
			Source = GameObject,
			Attacker = Instigator?.GameObject,
			Position = WorldPosition,
			Rotation = WorldRotation,
			Radius = radius,
			Damage = damage,
			ForceScale = forceScale
		} );

		if ( explosion.IsValid() )
			CreateScorchDecal();

		GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void CreateScorchDecal()
	{
		var tr = Scene.Trace.Ray( new Ray( WorldPosition + ( WorldRotation.Backward * 8 ), WorldRotation.Forward ), 40 )
			.IgnoreDynamic()
			.IgnoreGameObjectHierarchy( GameObject )
			.UseHitPosition( true )
			.Run();

		if ( !tr.Hit ) return;

		var scorchPrefab = GameObject.GetPrefab( "/prefabs/engine/scorch.prefab" );
		if ( scorchPrefab == null )
		{
			Log.Warning( "Can't find /prefabs/engine/scorch.prefab" );
			return;
		}

		var go = scorchPrefab.Clone( tr.HitPosition + tr.Normal, Rotation.LookAt( -tr.Normal ) );
		go.SetParent( tr.GameObject, true );
		go.Enabled = true;
	}

	/// <summary>
	/// Updates the current direction of the projectile
	/// </summary>
	/// <param name="direction"></param>
	/// <param name="speed"></param>
	public void UpdateDirection( Vector3 direction, float speed )
	{
		WorldRotation = Rotation.LookAt( direction, Vector3.Up );
		Rigidbody.Velocity = WorldRotation.Forward * speed;
	}
}
