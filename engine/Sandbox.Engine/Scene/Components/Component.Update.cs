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

	bool _startCalled;

	internal void InternalOnStart()
	{
		if ( !Enabled ) return;
		if ( !ShouldExecute ) return;

		if ( _startCalled ) return;

		// Disable any interpolation during OnStart. We might be created in a Fixed Update context.
		using ( GameTransform.DisableInterpolation() )
		{
			Scene.pendingStartComponents.Remove( this );
			_startCalled = true;

			try { OnStart(); }
			catch ( System.Exception e ) { Log.Error( e, $"Exception when calling 'Start' on {this}" ); }

			if ( Scene is not null && !Scene.IsEditor )
			{
				try { OnComponentStart?.Invoke(); }
				catch ( System.Exception e ) { Log.Error( e, $"Exception when calling 'Start' on {this}" ); }
			}
		}
	}

	internal virtual void InternalUpdate()
	{
		if ( !Enabled ) return;
		if ( !ShouldExecute ) return;

		InternalOnStart();

		try { OnUpdate(); }
		catch ( System.Exception e ) { Log.Error( e, $"Exception when calling 'Update' on {this}" ); }

		if ( Scene is not null && !Scene.IsEditor )
		{
			try { OnComponentUpdate?.Invoke(); }
			catch ( System.Exception e ) { Log.Error( e, $"Exception when calling 'Update' on {this}" ); }
		}
	}

	internal virtual void InternalFixedUpdate()
	{
		if ( !Enabled ) return;
		if ( !ShouldExecute ) return;

		InternalOnStart();

		try { OnFixedUpdate(); }
		catch ( System.Exception e ) { Log.Error( e, $"Exception when calling 'FixedUpdate' on {this}" ); }

		if ( Scene is not null && !Scene.IsEditor )
		{
			try { OnComponentFixedUpdate?.Invoke(); }
			catch ( System.Exception e ) { Log.Error( e, $"Exception when calling 'FixedUpdate' on {this}" ); }
		}
	}
}
