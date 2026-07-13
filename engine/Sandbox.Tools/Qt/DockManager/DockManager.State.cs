using System;

namespace Editor;

public partial class DockManager
{
	/// <summary>
	/// Called when the layout state is loaded, e.g. when the default
	/// layout is applied or a saved layout is restored.
	/// </summary>
	public Action OnLayoutLoaded { get; set; }

	/// <summary>
	/// A string representing the entire state of the dock manager (position of all docks, etc).
	/// Setting this restores the layout and invokes <see cref="OnLayoutLoaded"/>.
	/// </summary>
	public string State
	{
		get => _nativeDockManager.saveState( 1 );
		set => RestoreState( value );
	}

	/// <summary>
	/// Restore a layout previously captured from <see cref="State"/>. Returns false if the
	/// state couldn't be restored, e.g. it was saved by an incompatible version.
	/// </summary>
	public bool RestoreState( string state )
	{
		if ( !_nativeDockManager.restoreState( state, 1 ) )
			return false;

		OnLayoutLoaded?.Invoke();
		return true;
	}
}
