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
	public void Run( Doo doo, Action<Doo.Configure> c = null )
	{
		if ( doo is null || doo.IsEmpty() ) return;

		DooEngine
			.Get( Scene )
			.Run( this, doo, c );
	}

	/// <summary>
	/// Stop a specific Doo, if it's running
	/// </summary>
	public void Stop( Doo doo )
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
	public void StopAll()
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
	public bool IsRunning( Doo doo )
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
