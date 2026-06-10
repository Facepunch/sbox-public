using Mono.Cecil;
using System;
using System.Collections.Concurrent;

namespace Sandbox;

public partial class AccessControl
{
	static ConcurrentDictionary<AssemblyNameReference, AssemblyDefinition> GlobalAssemblyCache = new( AssemblyNameComparer.Instance );

	readonly object packageResolveLock = new();

	/// <summary>
	/// Called when a <c>package.*</c> assembly isn't already in <see cref="Assemblies"/>.
	/// Return the raw DLL bytes so the resolver can build a Cecil definition on demand,
	/// or return null to let the resolve fail normally.
	/// </summary>
	public Func<string, byte[]> PackageAssemblyResolver { get; set; }

	public AssemblyDefinition Resolve( AssemblyNameReference name )
	{
		//
		// Look in the dynamic assemblies first
		//
		if ( TryResolveFromCache( name, out var cached ) )
			return cached;

		//
		// For package.* assemblies, try to resolve on demand from active package filesystems.
		// Serialized: Cecil reads aren't thread-safe and Resolve runs under parallel touch analysis,
		// so without this two threads could build separate definitions for the same name.
		//
		if ( name.Name.StartsWith( "package.", StringComparison.OrdinalIgnoreCase ) && PackageAssemblyResolver != null )
		{
			lock ( packageResolveLock )
			{
				// Another thread may have built it while we were waiting
				if ( TryResolveFromCache( name, out var existing ) )
					return existing;

				var bytes = PackageAssemblyResolver( name.Name );
				if ( bytes != null )
				{
					var ms = new MemoryStream( bytes );
					var options = new ReaderParameters { ReadingMode = ReadingMode.Immediate, InMemory = true, AssemblyResolver = this };
					var assm = AssemblyDefinition.ReadAssembly( ms, options );
					Assemblies[assm.Name] = assm;
					return assm;
				}
			}
		}

		//
		// We only resolve certain named dlls from disk - and certainly not package.
		//
		if ( !name.Name.StartsWith( "Sandbox.", StringComparison.OrdinalIgnoreCase ) &&
			 !name.Name.StartsWith( "System.", StringComparison.OrdinalIgnoreCase ) &&
			 name.Name != "Microsoft.AspNetCore.Components" )
			throw NotResolved( name );

		//
		// Now look at our System. and Sandbox. assemblies
		//
		if ( GlobalAssemblyCache.TryGetValue( name, out var systemAssembly ) )
			return systemAssembly;

		lock ( GlobalAssemblyCache )
		{
			return GlobalAssemblyCache.GetOrAdd( name, FindAssemblyOnDisk );
		}
	}

	bool TryResolveFromCache( AssemblyNameReference name, out AssemblyDefinition assm )
	{
		assm = null;

		if ( Assemblies == null )
			return false;

		if ( Assemblies.TryGetValue( name, out assm ) )
			return true;

		var newestNameMatch = Assemblies
			.Where( x => x.Key.Name.Equals( name.Name, StringComparison.OrdinalIgnoreCase ) )
			//.Where( x => x.Key.Version.CompareTo( name.Version ) >= 0 )
			.OrderByDescending( x => x.Key.Version )
			.FirstOrDefault();

		if ( newestNameMatch.Value != null )
		{
			assm = newestNameMatch.Value;
			return true;
		}

		return false;
	}

	AssemblyDefinition FindAssemblyOnDisk( AssemblyNameReference name )
	{
		var dllName = $"{name.Name}.dll";

		var corePath = System.IO.Path.GetDirectoryName( typeof( Object ).Assembly.Location );
		var testPath = System.IO.Path.Combine( corePath, dllName );

		if ( !System.IO.File.Exists( testPath ) )
		{
			var gamePath = System.IO.Path.GetDirectoryName( GetType().Assembly.Location );
			testPath = System.IO.Path.Combine( gamePath, dllName );
		}

		if ( !System.IO.File.Exists( testPath ) )
			throw NotResolved( name );

		var fileContent = System.IO.File.ReadAllBytes( testPath );
		var stream = new MemoryStream( fileContent );

		var options = new ReaderParameters { ReadingMode = ReadingMode.Immediate, InMemory = true, AssemblyResolver = this };
		var assm = AssemblyDefinition.ReadAssembly( stream, options );

		return assm;
	}

	private Exception NotResolved( AssemblyNameReference name )
	{
		return new System.Exception( $"Couldn't resolve '{name}' [{string.Join( ";", Assemblies.Select( x => $"{x.Key}@{x.Key}" ) )}]" );
	}

	public AssemblyDefinition Resolve( AssemblyNameReference name, ReaderParameters parameters )
	{
		return Resolve( name );
	}
}
