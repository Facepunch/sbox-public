namespace Facepunch.Native;

/// <summary>
/// Generates the native build files from the module definitions in src/**/*.build.cs. What comes out is
/// the target platform's business: projects and solutions for Windows, a makefile for Linux.
/// </summary>
public static class Generate
{
	public static bool Run( Options options, string only = null )
	{
		var platform = NativePlatform.Find( options.Platform );
		if ( platform is null )
		{
			Log.Error( $"No native platform named '{options.Platform}'. Known: {string.Join( ", ", NativePlatform.All.Select( p => p.Name ) )}." );
			return false;
		}

		NativePlatform.Current = platform;
		Module.Retail = options.Retail;

		var modules = Registry.Load( options );
		if ( modules.Count == 0 )
		{
			Log.Error( "No native modules found. Expected src/**/*.build.cs files compiled into SboxBuild." );
			return false;
		}

		var silent = modules.Where( m => !m.HasSources ).Select( m => m.Name ).ToList();
		if ( silent.Count > 0 )
		{
			Log.Error( $"Nothing to compile in: {string.Join( ", ", silent )}. Every module states its sources, e.g. Compile( \"*.cpp\" )." );
			return false;
		}

		if ( only is null )
		{
			NativePlatform.Current.Generate( modules, options );
			return true;
		}

		var module = modules.FirstOrDefault( m => m.Name.Equals( only, StringComparison.OrdinalIgnoreCase ) );
		if ( module is null )
		{
			Log.Error( $"No native module named '{only}'." );
			return false;
		}

		NativePlatform.Current.Generate( module, options );
		return true;
	}
}
