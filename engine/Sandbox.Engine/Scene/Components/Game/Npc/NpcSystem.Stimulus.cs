namespace Sandbox.Npcs;

/// <summary>
/// The world stimulus bus. Anything can broadcast a <see cref="Stimulus"/> (a gunshot, a
/// death, a crime) and nearby NPCs sense it through their <see cref="Layers.SensesLayer"/>.
/// This is how the world pushes events at NPCs, rather than NPCs only polling for entities.
/// </summary>
public sealed partial class NpcSystem
{
	private readonly List<Stimulus> _stimuli = new();

	/// <summary>
	/// Broadcast a stimulus into the world. <paramref name="radius"/> is how far it can be
	/// sensed; <paramref name="lifetime"/> is how long it lingers (seconds).
	/// </summary>
	public void EmitStimulus( string kind, Vector3 position, GameObject source = null, float radius = 1024f, float lifetime = 1f )
	{
		_stimuli.Add( new Stimulus( kind, position, source, radius, Time.Now + lifetime ) );
	}

	/// <summary>
	/// The nearest active stimulus whose range reaches a position, excluding the given source.
	/// </summary>
	internal Stimulus? FindNearestStimulus( Vector3 position, GameObject excludeSource )
	{
		var now = Time.Now;
		Stimulus? nearest = null;
		float nearestDistance = float.MaxValue;

		foreach ( var stimulus in _stimuli )
		{
			if ( now >= stimulus.Expiry )
				continue;
			if ( stimulus.Source == excludeSource )
				continue;

			if ( (position - stimulus.Position).LengthSquared <= stimulus.Radius * stimulus.Radius )
			{
				var distance = position.Distance( stimulus.Position );
				if ( distance < nearestDistance )
				{
					nearestDistance = distance;
					nearest = stimulus;
				}
			}
		}
		return nearest;
	}

	private void PruneStimuli()
	{
		var now = Time.Now;

		for ( int i = _stimuli.Count - 1; i >= 0; i-- )
		{
			if ( now >= _stimuli[i].Expiry )
				_stimuli.RemoveAt( i );
		}
	}
}
