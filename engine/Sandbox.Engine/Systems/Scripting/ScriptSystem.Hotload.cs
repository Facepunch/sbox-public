using System.Reflection;

namespace Sandbox;

public sealed partial class ScriptSystem
{
	/// <summary>
	/// Rebuilds declarations against incoming C# dependencies and queues both assemblies for the same migration.
	/// Existing startup statements are not executed again.
	/// </summary>
	internal IDisposable PrepareHotload( IReadOnlyList<Assembly> incoming, HotloadManager manager )
	{
		var bindings = new Sandbox.Utility.DisposeAction( () =>
		{
			manager.Hotload.RetainedAssembly = null;
			_resolver.IncomingAssemblies = [];
			_runtime.InvalidateBindings();
		} );

		Assembly rebuilt;
		try
		{
			CheckContextAccess();
			// Finish background compilation before migration walks its closures and CLR metadata.
			Module.StopWarmup();
			if ( _assembly is null || !incoming.Any( assembly => _assembly.GetReferencedAssemblies()
				.Any( reference => reference.Name == assembly.GetName().Name ) ) )
			{
				return null;
			}

			_resolver.IncomingAssemblies = incoming;
			_runtime.InvalidateBindings();
			// Retain file ownership and captured state while rebuilding against incoming C# types.
			using var update = Module.PrepareRebind();
			ValidateTypes( update.Types );

			// Staged publication does not invoke the convenience API's attribute validation.
			foreach ( var type in update.Types )
			{
				_ = type.GetCustomAttributes( false );
				foreach ( var member in type.GetMembers( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly ) )
				{
					_ = member.GetCustomAttributes( false );
				}
			}

			Module.Publish( update );
			rebuilt = update.Assembly;
		}
		catch ( Exception e ) when ( e is not OutOfMemoryException )
		{
			// A live script can still use its old C# base until a compatible edit lets us rebuild it.
			manager.Hotload.RetainedAssembly = _assembly;
			Log.Warning( e, "Couldn't rebuild scripts against the incoming package. Keeping the previous script types." );
			return bindings;
		}

		var previous = _assembly;
		_assembly = rebuilt;
		manager.Replace( _watchedAssembly, rebuilt );
		_watchedAssembly = rebuilt;

		// Publish to each catalog before migration runs script static initializers.
		foreach ( var library in _libraries )
		{
			try
			{
				library.RemoveAssembly( previous );
				library.AddAssembly( rebuilt, true );
			}
			catch ( Exception e ) when ( e is not OutOfMemoryException )
			{
				Log.Warning( e, "Some rebuilt script types could not be registered." );
			}
		}

		return bindings;
	}
}
