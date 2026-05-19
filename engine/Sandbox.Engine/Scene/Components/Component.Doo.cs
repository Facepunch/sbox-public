namespace Sandbox;

public abstract partial class Component : Doo.IHost
{
	/// <summary>
	/// A list of running doos
	/// </summary>
	internal List<Doo.RunContext> _doos;

	void Doo.IHost.OnStarted( Doo.RunContext ctx )
	{
		_doos ??= new( 8 );
		_doos.Add( ctx );
	}

	void Doo.IHost.OnStopped( Doo.RunContext ctx )
	{
		if ( _doos == null ) return;

		_doos.Remove( ctx );
	}

	/// <summary>
	/// Starts executing the given Doo on this component. Optionally configure initial arguments via the callback.
	/// </summary>
	public void RunDoo( Doo doo, Action<Doo.Configure> c = null )
	{
		if ( doo is null || doo.IsEmpty() ) return;

		DooEngine
			.Get( Scene )
			.Run( this, doo, c );
	}

	/// <summary>
	/// Stop a specific Doo, if it's running
	/// </summary>
	public void StopDoo( Doo doo )
	{
		if ( _doos == null ) return;
		if ( doo is null ) return;

		for ( int i = _doos.Count - 1; i >= 0; i-- )
		{
			if ( _doos[i].Doo != doo ) continue;

			_doos[i].Stopped = true;
		}
	}

	/// <summary>
	/// Stop all running Doos
	/// </summary>
	public void StopAllDoo()
	{
		if ( _doos == null ) return;

		for ( int i = _doos.Count - 1; i >= 0; i-- )
		{
			_doos[i].Stopped = true;
		}
	}

	/// <summary>
	/// Returns true if the given Doo is currently running on this component.
	/// </summary>
	public bool IsRunningDoo( Doo doo )
	{
		if ( _doos == null ) return false;

		for ( int i = _doos.Count - 1; i >= 0; i-- )
		{
			if ( _doos[i].Stopped ) continue;
			if ( _doos[i].Doo == doo ) return true;
		}

		return false;
	}
}

/// <summary>
/// Backward-compatible extension methods telling users to switch from Run/Stop/IsRunning to RunDoo/StopDoo/IsRunningDoo.
/// These are marked as obsolete and hidden from intellisense to encourage migration, and are extensions so you don't get member variable conflicts.
/// </summary>
[System.ComponentModel.EditorBrowsable( System.ComponentModel.EditorBrowsableState.Never )]
public static class ComponentDooExtensions
{
	/// <inheritdoc cref="Component.RunDoo"/>
	[Obsolete( "Use RunDoo instead" )]
	[System.ComponentModel.EditorBrowsable( System.ComponentModel.EditorBrowsableState.Never )]
	public static void Run( this Component self, Doo doo, Action<Doo.Configure> c = null )
	{
		self.RunDoo( doo, c );
	}

	/// <inheritdoc cref="Component.StopDoo"/>
	[Obsolete( "Use StopDoo instead" )]
	[System.ComponentModel.EditorBrowsable( System.ComponentModel.EditorBrowsableState.Never )]
	public static void Stop( this Component self, Doo doo )
	{
		self.StopDoo( doo );
	}

	/// <inheritdoc cref="Component.StopAllDoo"/>
	[Obsolete( "Use StopAllDoo instead" )]
	[System.ComponentModel.EditorBrowsable( System.ComponentModel.EditorBrowsableState.Never )]
	public static void StopAll( this Component self )
	{
		self.StopAllDoo();
	}

	/// <inheritdoc cref="Component.IsRunningDoo"/>
	[Obsolete( "Use IsRunningDoo instead" )]
	[System.ComponentModel.EditorBrowsable( System.ComponentModel.EditorBrowsableState.Never )]
	public static bool IsRunning( this Component self, Doo doo )
	{
		return self.IsRunningDoo( doo );
	}
}
