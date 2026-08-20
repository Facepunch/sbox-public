using System.Reflection;

namespace Facepunch.Native;

/// <summary>
/// Finds every module (any <see cref="Module"/> subclass, declared in src/&lt;module&gt;/*.build.cs and
/// compiled into this tool) and resolves defaults, libraries and dependencies.
/// </summary>
public static class Registry
{
	public static List<Module> Load( Options options )
	{
		var modules = Assembly.GetExecutingAssembly()
			.GetTypes()
			.Where( t => t.IsSubclassOf( typeof( Module ) ) && !t.IsAbstract && t.GetConstructor( [] ) is not null )
			.Select( t => (Module)Activator.CreateInstance( t ) )
			.OrderBy( m => m.Name, StringComparer.OrdinalIgnoreCase )
			.ToList();

		modules.RemoveAll( NativePlatform.Current.Skips );

		var byName = modules.ToDictionary( m => m.Name, StringComparer.OrdinalIgnoreCase );

		foreach ( var module in modules )
		{
			module.Finish();
			NativePlatform.Current.Apply( module, options );
			Conventions.Standard( module );
			Resolve( module, byName );
			Conventions.Schema( module );
			SchemaCompiler.Apply( module, options );
			Qt.Apply( module );
			Unity.Apply( module );
		}

		// Public includes travel with the dependency, so consumers only name the module.
		foreach ( var module in modules )
		{
			// After the platform has applied its own, so this writes to the configs rather than the list.
			foreach ( var dependency in module.Dependencies )
				foreach ( var include in dependency.PublicIncludes )
					foreach ( var config in module.Configs )
						config.Include( Paths.Resolve( dependency.Dir, include ) is var resolved && resolved.Length > 0 ? $"/{resolved}" : include );

			foreach ( var config in module.Configs ) module.Configure( config );
		}

		// After the includes above, so waiting for a tool does not also inherit its public includes.
		SchemaCompiler.Order( modules );

		// A module shipping a launcher copies one, so it waits for whoever builds it.
		var binlaunch = modules.FirstOrDefault( m => m.Name.Equals( "binlaunch", StringComparison.OrdinalIgnoreCase ) );
		if ( binlaunch is not null )
			foreach ( var module in modules )
				if ( module.Launcher && module != binlaunch && !module.Dependencies.Contains( binlaunch ) )
					module.Dependencies.Add( binlaunch );

		return modules;
	}

	private static void Resolve( Module module, Dictionary<string, Module> byName )
	{
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var queue = new Queue<string>( module.LinkLibraries.Concat( module.InterfaceLinkLibraries ) );

		while ( queue.Count > 0 )
		{
			var name = queue.Dequeue();
			if ( !seen.Add( name ) ) continue;

			module.Libraries.Add( LibraryPath( name ) );

			var producer = Producer( name, byName );
			if ( producer is null || producer == module ) continue;

			module.Dependencies.Add( producer );
			foreach ( var propagated in producer.PublicLibs ) queue.Enqueue( propagated );
		}

		module.Libraries = [.. module.Libraries.Distinct( StringComparer.OrdinalIgnoreCase )];
		module.Dependencies = [.. module.Dependencies.Distinct()];
	}

	/// <summary>A bare lib name lives in lib/public; anything with a slash is a path relative to src/.</summary>
	public static string LibraryPath( string name )
	{
		var path = name.Replace( '\\', '/' ).TrimStart( '/' );
		if ( !path.Contains( '/' ) ) path = $"{Paths.LibPublic}/{path}";

		string[] known = [".lib", ".a", ".so"];
		if ( !known.Any( e => path.EndsWith( e, StringComparison.OrdinalIgnoreCase ) ) ) path += ".lib";

		return path;
	}

	private static Module Producer( string name, Dictionary<string, Module> byName )
	{
		var bare = Path.GetFileNameWithoutExtension( name.Replace( '\\', '/' ) );
		return byName.GetValueOrDefault( bare );
	}

	/// <summary>Written when the project is generated, so the stale check passes until it changes.</summary>
	public static string Sentinel( Module module ) => $@"obj\{module.Name}.generated";

	/// <summary>The .build.cs that declared a module, if it is still on disk.</summary>
	public static string Definition( Module module )
	{
		var files = Paths.Glob( module.Dir, "*.build.cs" ).ToList();
		return files.FirstOrDefault( f => Path.GetFileName( f ).StartsWith( module.Name, StringComparison.OrdinalIgnoreCase ) )
			?? files.FirstOrDefault();
	}
}
