using System.Reflection;

namespace Sandbox;

public sealed partial class ScriptSystem
{
	private bool _hotloadFailed;

	/// <summary>
	/// Rebuilds declarations against incoming C# dependencies and queues both assemblies for the same migration.
	/// Existing startup statements are not executed again.
	/// </summary>
	internal AssemblyHotload PrepareHotload( IReadOnlyList<Assembly> incoming, Action<Assembly, Assembly> replace )
	{
		CheckAccess();
		// Drain optional compilation before the hotloader walks its closures and CLR metadata,
		// including native updates that do not replace this module's declaration assembly.
		Module.StopWarmup();
		if ( _assembly is null || !incoming.Any( assembly => _assembly.GetReferencedAssemblies()
			.Any( reference => reference.Name == assembly.GetName().Name ) ) )
		{
			return null;
		}

		var hotload = new AssemblyHotload( this );
		try
		{
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
			var previous = _assembly;
			foreach ( var library in _libraries )
			{
				library.RemoveAssembly( previous );
			}
			_assembly = update.Assembly;
			// Register the incoming types before the swap, so a static initializer that migration runs
			// can find its own type in the type library.
			hotload.Register();
			replace( previous, _assembly );
			return hotload;
		}
		catch
		{
			hotload.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Keeps incoming CLR bindings available during migration, then restores ordinary catalog lookup.
	/// A failed dependency update requires a context reset because the C# assemblies have already been replaced.
	/// </summary>
	internal sealed class AssemblyHotload( ScriptSystem owner ) : IDisposable
	{
		private bool _complete;
		private bool _registered;

		/// <summary>
		/// Registers the replacement script assembly before reference migration, and so before
		/// scene and editor hotload notifications. Later calls do nothing.
		/// </summary>
		internal void Register()
		{
			if ( _registered ) return;
			_registered = true;

			foreach ( var library in owner._libraries )
			{
				library.AddAssembly( owner._assembly, true );
			}
		}

		/// <summary>
		/// Records that reference migration and all registration callbacks completed successfully.
		/// </summary>
		internal void Complete() => _complete = true;

		/// <summary>
		/// Clears preparation bindings and prevents further execution after a failed dependency update.
		/// </summary>
		public void Dispose()
		{
			owner._resolver.IncomingAssemblies = [];
			owner._runtime.InvalidateBindings();
			owner._hotloadFailed |= !_complete;
		}
	}
}
