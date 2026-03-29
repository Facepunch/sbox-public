namespace Sandbox;

public static partial class ConsoleSystem
{
	/// <summary>
	/// Run this command. This should be a single command.
	/// </summary>
	public static void Run( string command )
	{
		if ( command.Contains( ' ' ) )
		{
			var parts = command.SplitQuotesStrings();
			if ( parts.Length == 0 ) return;
			if ( parts.Length == 1 )
			{
				RunInternal( new ConsoleCommand { Name = command } );
				return;
			}

			RunInternal( new ConsoleCommand( parts[0], parts.Skip( 1 ).ToArray() ) );
			return;
		}

		RunInternal( new ConsoleCommand { Name = command } );
	}

	/// <summary>
	/// Run this command, along with the arguments. We'll automatically convert them to strings and handle quoting.
	/// </summary>
	public static void Run( string command, params object[] arguments )
	{
		//Log.Info( $"Run: {command} {arguments}" );

		if ( arguments == null || arguments.Length == 0 )
		{
			Run( command );
			return;
		}

		// TODO - we should serialize better
		RunInternal( new ConsoleCommand { Name = command, Arguments = arguments.Select( x => $"{x}" ).ToArray() } );
	}

	static bool CanRunCommand( string name )
	{
		// Menu can do whatever the fuck it wants
		if ( Game.IsMenu )
			return true;

		var command = ConVarSystem.Find( name );

		//
		// If we can't find the command in our managed library, we can't run it
		// Are there any exceptions here?
		//
		if ( command is null )
			return false;

		//
		// Game code can't run protected commands/convars
		//
		if ( command.IsProtected )
			return false;

		// Maybe we can any command that is managed based?
		return true;
	}

	/// <summary>
	/// Actually do the business of trying to run a command. Will return (not throw) an exception
	/// object if an exception is thrown of command isn't found.
	/// </summary>
	private static void RunInternal( ConsoleCommand command )
	{
		ThreadSafe.AssertIsMainThread();

		if ( !CanRunCommand( command.Name ) )
		{
			Log.Info( $"Can't run command {command.Name}" );
			throw new System.Exception( $"Can't run '{command.Name}'" );
		}

		var commandString = command.ToStringCommand();
		ConVarSystem.RunSingle( commandString, allowProtected: Game.IsMenu );
	}

	private struct ConsoleCommand
	{
		public string Name;
		public string[] Arguments;

		internal ConsoleCommand( string name, string[] arguments )
		{
			Name = name;
			Arguments = arguments;
		}

		internal string ToStringCommand()
		{
			if ( Arguments == null ) return Name;
			return $"{Name} {string.Join( " ", Arguments.Select( x => $"{x}".QuoteSafe() ) )}";
		}
	}
}
