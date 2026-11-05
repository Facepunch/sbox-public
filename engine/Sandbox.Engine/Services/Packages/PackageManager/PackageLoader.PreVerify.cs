using System.Threading;

namespace Sandbox;

internal sealed partial class PackageLoader
{
	// One package's assemblies are checked at a time, the check itself is parallel
	static readonly SemaphoreSlim _verifyLock = new( 1, 1 );

	/// <summary>
	/// Run access control over a remote package's assemblies, and those of the packages it references, on a worker
	/// thread. Checking them is most of what loading them costs and it doesn't need the main thread: loading them after
	/// this finds each one already passed by its hash and only adds it, while the loading screen keeps drawing.
	/// </summary>
	internal static Task PreVerifyAsync( string ident )
	{
		var packages = new List<PackageManager.ActivePackage>();
		Collect( ident, packages, new HashSet<PackageManager.ActivePackage>() );

		return Task.WhenAll( packages.Select( PreVerifyAsync ) );
	}

	/// <summary>
	/// <see cref="PreVerifyAsync(string)"/> for one package, started as soon as it's downloaded so it runs alongside the
	/// rest of the install. Asking again waits for the same check.
	/// </summary>
	internal static Task PreVerifyAsync( PackageManager.ActivePackage package )
	{
		if ( package.Package is LocalPackage || package.AssemblyFileSystem is null ) return Task.CompletedTask;

		return package.Verification ??= Task.Run( async () =>
		{
			await _verifyLock.WaitAsync();
			try
			{
				Verify( package );
			}
			finally
			{
				_verifyLock.Release();
			}
		} );
	}

	static void Verify( PackageManager.ActivePackage package )
	{
		try
		{
			var ordered = new AssemblyOrderer();
			foreach ( var name in package.AssemblyFileSystem.FindFile( "", "*.dll", true ) )
			{
				if ( name.EndsWith( ".editor.dll", StringComparison.OrdinalIgnoreCase ) ) continue;
				ordered.Add( name, package.AssemblyFileSystem.ReadAllBytes( name ).ToArray() );
			}

			foreach ( var (_, bytes) in ordered.GetDependencyOrdered() )
			{
				using var stream = new System.IO.MemoryStream( bytes );
				PackageManager.AccessControl.VerifyAssembly( stream, out var trusted );
				trusted?.Dispose();
			}
		}
		catch ( Exception e )
		{
			// Loading checks them again and reports what's wrong
			Log.Trace( $"PreVerify {package.Package.FullIdent}: {e.Message}" );
		}
	}

	/// <summary>
	/// Remote packages with assemblies, references before the packages that use them.
	/// </summary>
	static void Collect( string ident, List<PackageManager.ActivePackage> packages, HashSet<PackageManager.ActivePackage> seen )
	{
		var ap = PackageManager.Find( ident, true, false );
		if ( ap is null || !seen.Add( ap ) || ap.Package is LocalPackage ) return;

		foreach ( var child in ap.Package.EnumeratePackageReferences() )
			Collect( child, packages, seen );

		if ( ap.AssemblyFileSystem is not null ) packages.Add( ap );
	}
}
