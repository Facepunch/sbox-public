/// <summary>
/// An explosive that can be activated by damage, player input, or a signal.
/// </summary>
[Alias( "dynamite" ), Title( "#entity.name.dynamite" )]
public sealed class DynamiteEntity : Component, Component.IDamageable, IPlayerControllable
{
	/// <summary>
	/// The damage dealt by the explosion.
	/// </summary>
	[Property, Title( "#entity.property.dynamite_damage" ), Description( "#entity.description.dynamite_damage" ), Range( 1, 500 ), Step( 1 ), ClientEditable]
	public float Damage { get; set; } = 128;

	/// <summary>
	/// The radius affected by the explosion.
	/// </summary>
	[Property, Title( "#entity.property.dynamite_radius" ), Description( "#entity.description.dynamite_radius" ), Range( 16, 4096 ), Step( 16 ), ClientEditable]
	public float Radius { get; set; } = 1024f;

	/// <summary>
	/// The strength of the explosion's physical force.
	/// </summary>
	[Property, Title( "#entity.property.dynamite_force" ), Description( "#entity.description.dynamite_force" ), Range( 1, 100 ), Step( 1 ), ClientEditable]
	public float Force { get; set; } = 1;

	/// <summary>
	/// The input that detonates the explosive.
	/// </summary>
	[Property, Title( "#entity.property.dynamite_activate" ), Description( "#entity.description.dynamite_activate" ), ClientEditable]
	public ClientInput Activate { get; set; }

	bool _isDead = false;

	/// <summary>
	/// Creates the explosion and removes this explosive.
	/// </summary>
	[Rpc.Host]
	public void Explode()
	{
		_isDead = true;

		var explosion = Explosion.Spawn( new ExplosionEvent
		{
			Source = GameObject,
			Position = WorldPosition,
			Rotation = WorldRotation,
			Radius = Radius,
			Damage = Damage,
			ForceScale = Force
		} );

		if ( explosion.IsValid() )
			CreateScorchDecal();

		GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void CreateScorchDecal()
	{
		var tr = Scene.Trace.Ray( new Ray( WorldPosition + new Vector3( 0, 0, 8 ), Vector3.Down ), 40 )
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

		var go = scorchPrefab.Clone( tr.HitPosition + tr.Normal, Rotation.LookAt( -tr.Normal, Vector3.Up ) );
		go.SetParent( tr.GameObject, true );
		go.Enabled = true;
	}

	void IDamageable.OnDamage( in DamageInfo damage )
	{
		if ( _isDead ) return;
		if ( IsProxy ) return;

		Explode();
	}

	/// <summary>
	/// Detonates the explosive when its signal input is triggered.
	/// </summary>
	[SignalInput( Id = nameof( Activate ), Default = true ), Title( "#entity.property.dynamite_activate" )]
	public void ActivateSignal()
	{
		if ( !_isDead ) Explode();
	}

	/// <summary>
	/// Detonates the explosive when the owning player presses its activation input.
	/// </summary>
	public void OnControl()
	{
		if ( !_isDead && Activate.Pressed() ) Explode();
	}
}
