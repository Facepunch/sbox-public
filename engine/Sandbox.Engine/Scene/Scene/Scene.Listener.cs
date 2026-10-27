namespace Sandbox;

partial class Scene
{
	/// <summary>
	/// Default scene sound listener, at the camera.
	/// </summary>
	internal Audio.Listener Listener { get; set; }

	/// <summary>
	/// Multiple scene sound listeners, created by audio listener components.
	/// </summary>
	internal HashSet<Audio.Listener> Listeners { get; private set; }

	/// <summary>
	/// Update default sound listener to camera.
	/// </summary>
	private void UpdateDefaultListener()
	{
		// default sound listener, it might get overriden anyway
		Listener ??= new( this );
		Listener.Transform = new( Camera.WorldPosition, Camera.WorldRotation );
	}

	/// <summary>
	/// Dispose any sound listeners we created.
	/// </summary>
	private void DisposeListeners()
	{
		Listener?.Dispose();
		Listener = default;

		if ( Listeners is not null )
		{
			foreach ( var listener in Listeners )
			{
				listener?.Dispose();
			}

			Listeners.Clear();
			Listeners = default;
		}
	}

	/// <summary>
	/// Add a new sound listener and return it.
	/// </summary>
	internal Audio.Listener AddListener()
	{
		Listeners ??= new();
		var listener = new Audio.Listener( this );
		Listeners.Add( listener );
		return listener;
	}

	/// <summary>
	/// Remove and dispose a scene sound listener.
	/// </summary>
	internal void RemoveListener( Audio.Listener listener )
	{
		if ( listener is null )
			return;

		if ( Listeners.Remove( listener ) )
		{
			listener?.Dispose();
		}
	}

	/// <summary>
	/// The listener transform scene-wide audio (like soundscapes) should use. This is an
	/// <see cref="AudioListener"/> if one exists, otherwise the camera.
	/// </summary>
	internal Transform GetPrimaryListenerTransform()
	{
		if ( Listeners is { Count: > 0 } )
		{
			foreach ( var listener in Listeners )
			{
				if ( listener.IsValid() )
					return listener.Transform;
			}
		}

		return Listener?.Transform ?? global::Transform.Zero;
	}

	/// <summary>
	/// Find the closest sound listener at this point.
	/// </summary>
	internal Audio.Listener FindClosestListener( Vector3 point )
	{
		if ( Listeners is null || Listeners.Count == 0 )
			return Listener;

		var result = Listener;
		var bestDistance = result is null ? float.MaxValue : Vector3.DistanceBetweenSquared( result.Position, point );

		foreach ( var listener in Listeners )
		{
			var distance = Vector3.DistanceBetweenSquared( listener.Position, point );

			if ( distance < bestDistance )
			{
				bestDistance = distance;
				result = listener;
			}
		}

		return result;
	}
}
