namespace Sandbox.Audio;

/// <summary>
/// Audio processor that allows per listener state.
/// </summary>
public abstract class AudioProcessor<T> : AudioProcessor where T : AudioProcessor.ListenerState, new()
{
	/// <summary>
	/// Per listener states.
	/// </summary>
	private Dictionary<Listener, T> _states;

	/// <summary>
	/// Current state for listener.
	/// </summary>
	protected T CurrentState { get; private set; }

	/// <summary>
	/// Set current state for listener.
	/// </summary>
	internal override void SetListener( Listener listener )
	{
		// Try get state and set it as current state.
		if ( _states?.TryGetValue( listener, out T state ) == true )
		{
			CurrentState = state;

			return;
		}

		// New current state.
		CurrentState = new T();

		_states ??= new( ReferenceEqualityComparer.Instance );
		_states[listener] = CurrentState;
	}

	/// <summary>
	/// Remove any states associated with these listeners.
	/// </summary>
	internal override void RemoveListeners( IReadOnlyList<Listener> listeners )
	{
		if ( _states is null )
			return;

		foreach ( var listener in listeners )
		{
			// Remove and destroy state if it exists.
			if ( _states.Remove( listener, out var data ) )
			{
				data.Destroy();
			}
		}
	}

	/// <summary>
	/// Destroy and clear all states.
	/// </summary>
	internal override void OnRemovedInternal()
	{
		base.OnRemovedInternal();

		if ( _states is null )
			return;

		// Destroy all states.
		foreach ( var data in _states.Values )
		{
			data.Destroy();
		}

		_states.Clear();
		_states = default;
	}
}

partial class AudioProcessor
{
	/// <summary>
	/// One of these is created for every listener that uses an audio processor.
	/// </summary>
	public abstract class ListenerState
	{
		internal void Destroy()
		{
			OnDestroy();
		}

		/// <summary>
		/// Called when audio processor or the listener is removed.
		/// </summary>
		protected virtual void OnDestroy()
		{
		}
	}

	/// <summary>
	/// Optionally target a listener, this processor will only run for a specific listener.
	/// </summary>
	internal Listener TargetListener { get; set; }

	/// <summary>
	/// Set current state for listener.
	/// </summary>
	internal virtual void SetListener( Listener listener )
	{
	}

	/// <summary>
	/// Remove any states associated with these listeners.
	/// </summary>
	internal virtual void RemoveListeners( IReadOnlyList<Listener> listeners )
	{
	}
}
