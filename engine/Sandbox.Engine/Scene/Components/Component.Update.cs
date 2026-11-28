namespace Sandbox;

public abstract partial class Component
{
	/// <summary>
	/// Called once before the first Update - when enabled.
	/// </summary>
	protected virtual void OnStart() { }

	/// <summary>
	/// When enabled, called every frame
	/// </summary>
	protected virtual void OnUpdate() { }

	/// <summary>
	/// When enabled, called on a fixed interval that is determined by the Scene. This
	/// is also the fixed interval in which the physics are ticked. Time.Delta is that
	/// fixed interval.
	/// </summary>
	protected virtual void OnFixedUpdate() { }

	internal bool StartCalled;

	internal void InternalOnStart()
	{
		if ( !Enabled ) return;
		if ( !ShouldExecute ) return;

		if ( StartCalled ) return;
		using var cpu = Profiler.ScopesActive ? ProfileCallbackScopes.ForType( GetType(), "OnStart" ).Start() : default;

		// Disable any interpolation during OnStart. We might be created in a Fixed Update context.
		using ( GameTransform.DisableInterpolation() )
		{
			Scene.pendingStartComponents.Remove( this );
			StartCalled = true;

			try { OnStart(); }
			catch ( System.Exception e ) { Log.Error( e, $"Exception when calling 'Start' on {this}" ); }

			if ( Scene is not null && !Scene.IsEditor )
			{
				try { OnComponentStart?.Invoke(); }
				catch ( System.Exception e ) { Log.Error( e, $"Exception when calling 'Start' on {this}" ); }
			}
		}
	}

	/// <summary>Whether this component goes on <paramref name="scene"/>'s update list; read once as it joins.</summary>
	internal virtual bool UpdatesIn( Scene scene ) => true;

	internal virtual void InternalUpdate()
	{
		if ( !Enabled ) return;
		if ( !ShouldExecute ) return;

		InternalOnStart();

		try { OnUpdate(); }
		catch ( System.Exception e ) { LogFrameException( e, "Update" ); }

		if ( Scene is not null && !Scene.IsEditor )
		{
			try { OnComponentUpdate?.Invoke(); }
			catch ( System.Exception e ) { LogFrameException( e, "Update" ); }
		}
	}

	internal virtual void InternalFixedUpdate()
	{
		if ( !Enabled ) return;
		if ( !ShouldExecute ) return;

		InternalOnStart();

		try { OnFixedUpdate(); }
		catch ( System.Exception e ) { LogFrameException( e, "FixedUpdate" ); }

		if ( Scene is not null && !Scene.IsEditor )
		{
			try { OnComponentFixedUpdate?.Invoke(); }
			catch ( System.Exception e ) { LogFrameException( e, "FixedUpdate" ); }
		}
	}

	/// <summary>
	/// Logs an exception from a callback that runs every frame, throttled so it can't flood the log.
	/// </summary>
	void LogFrameException( System.Exception e, string call )
	{
		if ( ExceptionThrottle.ShouldLog( e, GetType(), call, out var suffix ) )
			Log.Error( e, $"Exception when calling '{call}' on {this}{suffix}" );
	}
}
