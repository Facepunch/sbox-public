/// <summary>
/// Data passed to <see cref="Global.IExplosionEvents.OnExplode"/>. Change the fields to reshape the blast,
/// or set <see cref="Cancelled"/> to true to stop it. A cancelled explosive is still used up.
/// </summary>
public class ExplosionEvent
{
	/// <summary>
	/// The thing that's exploding - dynamite, a grenade, a rocket, or the grenade weapon when it cooks off in a hand.
	/// </summary>
	public GameObject Source { get; init; }

	/// <summary>
	/// The object credited with the damage.
	/// </summary>
	public GameObject Attacker { get; set; }

	public Vector3 Position { get; set; }
	public Rotation Rotation { get; set; }
	public float Radius { get; set; }
	public float Damage { get; set; }
	public float ForceScale { get; set; }

	/// <summary>
	/// The prefab spawned for the blast. It carries the effects, sound and <see cref="RadiusDamage"/>.
	/// </summary>
	public PrefabFile Prefab { get; set; }

	public bool Cancelled { get; set; }
}

/// <summary>
/// Allows listening to and changing explosions. Implement this on a <see cref="Component"/> to make blasts bigger,
/// keep them out of a safe zone, or react to them going off.
/// </summary>
public static partial class Global
{
	public interface IExplosionEvents : ISceneEvent<IExplosionEvents>
	{
		/// <summary>
		/// Called on the host before an explosion is spawned.
		/// </summary>
		void OnExplode( ExplosionEvent e ) { }

		/// <summary>
		/// Called on the host after the explosion has been spawned.
		/// </summary>
		void OnExploded( ExplosionEvent e, GameObject explosion ) { }
	}
}

public static class Explosion
{
	/// <summary>
	/// The explosion every explosive spawns unless a listener swaps it.
	/// </summary>
	public const string DefaultPrefab = "/prefabs/engine/explosion_med.prefab";

	/// <summary>
	/// Fire <see cref="Global.IExplosionEvents"/>, then spawn and network the explosion. Host only.
	/// Returns null if a listener cancelled it or it couldn't be spawned.
	/// </summary>
	public static GameObject Spawn( ExplosionEvent e )
	{
		var scene = e.Source.IsValid() ? e.Source.Scene : Game.ActiveScene;
		if ( !scene.IsValid() ) return null;

		e.Prefab ??= ResourceLibrary.Get<PrefabFile>( DefaultPrefab );

		scene.RunEvent<Global.IExplosionEvents>( x => x.OnExplode( e ) );
		if ( e.Cancelled ) return null;

		if ( e.Prefab is null )
		{
			Log.Warning( $"Can't find {DefaultPrefab}" );
			return null;
		}

		var go = GameObject.Clone( e.Prefab, new CloneConfig { Transform = new Transform( e.Position, e.Rotation ), StartEnabled = false } );
		if ( !go.IsValid() ) return null;

		go.RunEvent<RadiusDamage>( x =>
		{
			x.Radius = e.Radius;
			x.PhysicsForceScale = e.ForceScale;
			x.DamageAmount = e.Damage;
			x.Attacker = e.Attacker.IsValid() ? e.Attacker : go;
			x.DamageTags ??= new();
			x.DamageTags.Add( DamageTags.Explosion );
		}, FindMode.EverythingInSelfAndDescendants );

		go.Enabled = true;
		go.NetworkSpawn( true, null );

		scene.RunEvent<Global.IExplosionEvents>( x => x.OnExploded( e, go ) );

		return go;
	}
}
