namespace Sandbox.Npcs;

/// <summary>
/// The world stimulus bus. Anything can broadcast a <see cref="Stimulus"/> (a gunshot, a
/// death, a crime) and nearby NPCs sense it through their <see cref="Layers.SensesLayer"/>.
/// This is how the world pushes events at NPCs, rather than NPCs only polling for entities.
/// </summary>
public sealed class NpcStimulusSystem : GameObjectSystem<NpcStimulusSystem>
{
	readonly List<Stimulus> _active = new();

	public NpcStimulusSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 0, Prune, "PruneStimuli" );
	}

	/// <summary>
	/// Broadcast a stimulus into the world. <paramref name="radius"/> is how far it can be
	/// sensed; <paramref name="lifetime"/> is how long it lingers (seconds).
	/// </summary>
	public void Broadcast( string kind, Vector3 position, GameObject source = null, float radius = 1024f, float lifetime = 1f )
	{
		_active.Add( new Stimulus( kind, position, source, radius, Time.Now + lifetime ) );
	}

	/// <summary>
	/// Active stimuli whose range reaches a position.
	/// </summary>
	public IEnumerable<Stimulus> Near( Vector3 position )
	{
		var now = Time.Now;

		foreach ( var stimulus in _active )
		{
			if ( now >= stimulus.Expiry )
				continue;

			if ( (position - stimulus.Position).LengthSquared <= stimulus.Radius * stimulus.Radius )
				yield return stimulus;
		}
	}

	void Prune()
	{
		var now = Time.Now;

		for ( int i = _active.Count - 1; i >= 0; i-- )
		{
			if ( now >= _active[i].Expiry )
				_active.RemoveAt( i );
		}
	}
}
