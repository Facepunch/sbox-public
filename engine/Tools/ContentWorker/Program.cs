using Sandbox;
using System;

namespace Facepunch.ContentWorker;

/// <summary>
/// A content build worker (see ContentWorkerAppSystem):
///   contentworker [-compilers folder] -searchpaths ...
/// one asset compiled where it is, as the editor compiles one by itself:
///   contentworker [-compilers folder] -searchpaths ... -compile file
/// or the editor's built-in projects compiled once for a build's workers:
///   contentworker -compileprojects folder
/// </summary>
class Program
{
	[STAThread]
	public static int Main( string[] args )
	{
		var i = Array.IndexOf( args, "-compileprojects" );
		if ( i >= 0 ) return ContentWorkerAppSystem.CompileBuiltInProjects( args[i + 1] ) ? 0 : 1;

		using var app = new ContentWorkerAppSystem();

		i = Array.IndexOf( args, "-compilers" );
		app.LoadCompilers( i >= 0 ? args[i + 1] : null );

		i = Array.IndexOf( args, "-compile" );
		if ( i >= 0 ) return app.Compile( args[i + 1] ) ? 0 : 1;

		return app.Serve();
	}
}
