namespace Sandbox.Internal;

/// <summary>
/// Globals shared by compiled game code and scripts in the current game context.
/// </summary>
[Expose]
public static class GlobalGameNamespace
{
	/// <summary>
	/// Log information to console for debugging purposes.
	/// </summary>
	public static Diagnostics.Logger Log { get; internal set; } = new( "Generic" );

	/// <summary>
	/// Data storage that persists across sessions.
	/// </summary>
	[Obsolete( "Use Game.Cookies" )]
	public static CookieContainer Cookie => Game.Cookies;

	/// <summary>
	/// Allows access to all available types, reflection style.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown when accessed during static constructors.
	/// </exception>
	public static TypeLibrary TypeLibrary => Game.TypeLibrary;
}
