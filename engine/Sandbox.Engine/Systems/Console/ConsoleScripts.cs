using System.IO;

namespace Sandbox.Engine;

internal static class ConsoleScripts
{
	const int MaxExecDepth = 8;
	static int _execDepth;

	[ConCmd( "exec", ConVarFlags.Protected, Help = "Execute a cfg file." )]
	internal static void Exec( params string[] args )
	{
		if ( args.Length == 0 )
		{
			Log.Info( "exec <filename>" );
			return;
		}
		if ( _execDepth >= MaxExecDepth )
		{
			Log.Warning( $"exec: maximum script nesting depth ({MaxExecDepth}) reached" );
			return;
		}

		try
		{
			_execDepth++;
			var text = Read( EngineFileSystem.Mounted, EngineFileSystem.CoreContent, args[0] );
			if ( text is not null ) ConVarSystem.Run( text );
		}
		finally
		{
			_execDepth--;
		}
	}

	internal static string Read( BaseFileSystem mounted, BaseFileSystem core, string file )
	{
		if ( string.IsNullOrWhiteSpace( file ) || file.Contains( ".." ) || file.Contains( ':' ) || file.Contains( "\\\\" ) || Path.IsPathRooted( file ) )
		{
			Log.Warning( $"exec: invalid script path '{file}'" );
			return null;
		}

		if ( !Path.HasExtension( file ) ) file += ".cfg";
		if ( !Path.GetExtension( file ).Equals( ".cfg", StringComparison.OrdinalIgnoreCase ) )
		{
			Log.Warning( $"exec: invalid script file type '{file}'" );
			return null;
		}

		var path = $"cfg/{file}";
		try
		{
			var fs = mounted?.FileExists( path ) == true ? mounted : core;
			if ( fs?.FileExists( path ) != true )
			{
				if ( !file.Equals( "autoexec.cfg", StringComparison.OrdinalIgnoreCase ) )
					Log.Warning( $"exec: couldn't find '{file}'" );
				return null;
			}
			if ( fs.FileSize( path ) > 1024 * 1024 )
			{
				Log.Warning( $"exec: '{file}' is larger than 1 MB" );
				return null;
			}
			return fs.ReadAllText( path );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			Log.Warning( $"exec: couldn't read '{file}': {e.Message}" );
			return null;
		}
	}
}
