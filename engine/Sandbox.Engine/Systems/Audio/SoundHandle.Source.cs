using Sandbox.Audio;

namespace Sandbox;

partial class SoundHandle
{
	private SteamAudioSource _audioSource;
	private Dictionary<Listener, SteamAudioSource> _audioSources;

	internal SteamAudioSource GetSource( Listener listener )
	{
		// Find listener source
		if ( _audioSources?.TryGetValue( listener, out var source ) == true )
		{
			return source;
		}

		// Local audio source
		return _audioSource;
	}

	private void UpdateSources()
	{
		// Only need a single audio source if listen local
		if ( ListenLocal )
		{
			_audioSource ??= new SteamAudioSource();
			_audioSource.UpdateFrom( this );

			// Dispose listener sources if there's any
			if ( _audioSources is not null )
			{
				foreach ( var source in _audioSources.Values )
				{
					source.Dispose();
				}

				_audioSources.Clear();
				_audioSources = default;
			}

			return;
		}

		// Dispose local audio source if we're not listen local
		if ( _audioSource is not null )
		{
			_audioSource?.Dispose();
			_audioSource = null;
		}

		// Remove stale sources - only if we have any
		if ( _audioSources is { Count: > 0 } )
		{
			foreach ( var removed in Listener.RemovedList )
			{
				if ( _audioSources.Remove( removed, out var source ) )
				{
					source.Dispose();
				}
			}
		}

		// Find listeners of this scene and update sources
		var scene = Scene;

		foreach ( var listener in Listener.ActiveList )
		{
			if ( listener.Scene != scene ) continue;

			_audioSources ??= new();

			if ( !_audioSources.TryGetValue( listener, out var source ) )
			{
				source = new SteamAudioSource();
				_audioSources[listener] = source;
			}

			source.UpdateFrom( this, scene?.PhysicsWorld, listener.Position );
		}
	}

	private void DisposeSources()
	{
		// Dispose local source
		MainThread.QueueDispose( _audioSource );
		_audioSource = default;

		// Dispose listener sources if there's any
		if ( _audioSources is not null )
		{
			foreach ( var source in _audioSources.Values )
			{
				MainThread.QueueDispose( source );
			}

			_audioSources.Clear();
			_audioSources = default;
		}
	}
}
