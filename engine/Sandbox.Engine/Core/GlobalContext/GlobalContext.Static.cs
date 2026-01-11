using System.Threading;

namespace Sandbox.Engine;

internal partial class GlobalContext
{
	private static readonly AsyncLocal<GlobalContext> _current = new AsyncLocal<GlobalContext>();

	/// <summary>
	/// The context used for the menu system
	/// </summary>
	public static GlobalContext Menu;

	/// <summary>
	/// The context used for the game. This is the default context.
	/// </summary>
	public static GlobalContext Game;

	/// <summary>
	/// The current active context
	/// </summary>
	public static GlobalContext Current
	{
		get => _current.Value ?? Game;
		set => _current.Value = value;
	}

	/// <summary>
	/// The global context for the game, which holds references to various systems and libraries used throughout the game.
	/// </summary>
	static GlobalContext()
	{
		Game = new GlobalContext();
		Menu = new GlobalContext();

		_current.Value = Game;
	}

	/// <summary>
	/// Throws an exception when called from client or server.
	/// </summary>
	public static void AssertMenu( [System.Runtime.CompilerServices.CallerMemberName] string memberName = "" )
	{
		if ( Current != Menu )
			throw new System.Exception( $"{memberName} should only be called in Menu scope!" );
	}
}
