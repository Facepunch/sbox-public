/// <summary>
/// Answers "which player caused this?" while entity code is running.
///
/// When a signal input fires or a seat pushes key states, the system notes who did it here.
/// If your code needs to blame someone — kill credit, undo — just read
/// <see cref="Player"/>. It's null when nobody is responsible (e.g. map logic).
/// </summary>
public static class ControlContext
{
	internal readonly record struct State( Player Player, Connection Connection );

	[ThreadStatic]
	private static State _current;

	/// <summary>
	/// The player who caused whatever is currently running, or null.
	/// </summary>
	public static Player Player => _current.Player;

	internal static Connection Connection => _current.Connection;

	/// <summary>
	/// Restores whoever was responsible before when disposed, so nested pushes unwind cleanly.
	/// </summary>
	internal readonly struct Scope : IDisposable
	{
		private readonly State _previous;

		internal Scope( State state )
		{
			_previous = _current;
			_current = state;
		}

		public void Dispose()
		{
			_current = _previous;
		}
	}

	/// <summary>
	/// Mark a player as responsible for the code that runs until the scope is disposed:
	/// <code>using var scope = ControlContext.Push( player );</code>
	/// </summary>
	internal static Scope Push( Player player, Connection connection = null ) => new( new State( player, connection ) );
}
