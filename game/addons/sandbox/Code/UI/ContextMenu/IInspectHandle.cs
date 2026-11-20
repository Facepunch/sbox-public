/// <summary>
/// For components that have nothing in the world to click on (no model, no collider), like a hidden speaker.
/// While the inspect menu is open (hold C) they get a clickable icon at their position instead, which selects them
/// and right-clicks into the usual context menu (wiring, delete...).
/// Implementers register themselves with <see cref="InspectHandles"/> while enabled.
/// </summary>
public interface IInspectHandle
{
	/// <summary>
	/// Whether to show the icon right now. Lets a component that's sometimes visible only ask for one when it isn't.
	/// </summary>
	bool ShowInspectHandle => true;

	/// <summary>
	/// Material icon name to show. Null uses the component type's <see cref="IconAttribute"/>.
	/// </summary>
	string InspectHandleIcon => null;
}

/// <summary>
/// Every live <see cref="IInspectHandle"/>, so the inspect menu doesn't have to search the scene for them.
/// </summary>
public static class InspectHandles
{
	private static readonly HashSet<Component> _all = new();

	public static void Register( Component component )
	{
		if ( component is IInspectHandle )
			_all.Add( component );
	}

	public static void Unregister( Component component ) => _all.Remove( component );

	/// <summary>
	/// The registered handles in this scene that want an icon shown.
	/// </summary>
	public static IEnumerable<Component> GetVisible( Scene scene )
	{
		_all.RemoveWhere( x => !x.IsValid() );

		foreach ( var component in _all )
		{
			if ( component.Scene != scene ) continue;
			if ( component is IInspectHandle { ShowInspectHandle: true } )
				yield return component;
		}
	}
}
