using Sandbox.Audio;

namespace Sandbox;

/// <summary>
/// Apply DSP to mixer when listener is inside a DspVolume
/// </summary>
[Expose]
sealed class DspVolumeGameSystem : GameObjectSystem<DspVolumeGameSystem>
{
	public DspVolumeGameSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 0, Update, "Dsp Update" );
	}

	public override void Dispose()
	{
		base.Dispose();

		var gameMixer = Mixer.FindMixerByName( "Game" );
		if ( gameMixer is null ) return;

		foreach ( var processor in _entries.Values )
		{
			gameMixer.RemoveProcessor( processor.processor );
		}
	}

	HashSet<string> _active { get; set; }

	record class Entry( DspProcessor processor, bool active );
	Dictionary<string, Entry> _entries = new();

	void Update()
	{
		using var _ = PerformanceStats.Timings.Audio.Scope();

		if ( Scene.IsEditor )
			return;

		var gameMixer = Mixer.FindMixerByName( "Game" );
		if ( gameMixer is null ) return;

		int lastPriority = int.MinValue;
		string found = default;

		foreach ( var volume in Scene.Volumes.FindAll<DspVolume>( Sound.Listener.Position ) )
		{
			int priority = volume.Priority;

			if ( priority < lastPriority )
				continue;

			lastPriority = priority;
			found = volume.Dsp.Name;
		}

		if ( !string.IsNullOrWhiteSpace( found ) && !_entries.ContainsKey( found ) )
		{
			var processor = new DspProcessor();

			processor.Effect = found;
			processor.Mix = 0;

			gameMixer.AddProcessor( processor );
			_entries[found] = new Entry( processor, true );
		}

		foreach ( var entry in _entries )
		{
			var mixTarget = found == entry.Key ? 1 : 0;

			entry.Value.processor.Mix = entry.Value.processor.Mix.Approach( mixTarget, Time.Delta );
		}

		foreach ( var entry in _entries.Where( x => x.Value.processor.Mix <= 0 ).ToArray() )
		{
			gameMixer.RemoveProcessor( entry.Value.processor );
			_entries.Remove( entry.Key );
		}

	}

	private void TryAdd( string name )
	{
		if ( _active.Contains( name ) ) return;
		_active.Add( name );
	}

	private void TryRemove( string name )
	{
		if ( !_active.Contains( name ) ) return;
		_active.Remove( name );
	}
}
