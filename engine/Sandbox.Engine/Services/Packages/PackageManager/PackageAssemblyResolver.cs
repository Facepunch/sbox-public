using Mono.Cecil;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sandbox;

sealed class PackageAssemblyResolver : IAssemblyResolver
{
	static readonly ConcurrentDictionary<AssemblyNameReference, AssemblyDefinition> GlobalAssemblyCache = new( AssemblyNameComparer.Instance );

	ConcurrentDictionary<AssemblyNameReference, AssemblyDefinition> Assemblies = new( AssemblyNameComparer.Instance );
	readonly object packageResolveLock = new();

	// Added assemblies are only read by hotload, so they're parsed when it first asks rather than on load
	readonly ConcurrentDictionary<string, byte[]> Unparsed = new( StringComparer.OrdinalIgnoreCase );

	public Func<string, byte[]> PackageBytes { get; init; }

	public void Dispose()
	{
		if ( Assemblies is null )
			return;

		foreach ( var assm in Assemblies.Values )
			assm.Dispose();

		Assemblies = null;
	}

	// Streamed assemblies may not exist in an active package filesystem, but hotload still needs
	// their Cecil definitions to resolve dependencies. Package assemblies are always named after
	// their file, and only package.* names are ungated by the compiler, so anything else is rejected.
	internal void AddAssembly( byte[] bytes, string expectedName )
	{
		ObjectDisposedException.ThrowIf( Assemblies is null, this );

		string name;
		using ( var pe = new System.Reflection.PortableExecutable.PEReader( new MemoryStream( bytes ) ) )
		{
			var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader( pe );
			name = metadata.GetString( metadata.GetAssemblyDefinition().Name );
		}

		if ( !name.StartsWith( "package.", StringComparison.OrdinalIgnoreCase ) ||
			 !name.Equals( expectedName, StringComparison.OrdinalIgnoreCase ) )
		{
			throw new InvalidDataException( $"Assembly '{name}' does not match package '{expectedName}'" );
		}

		Unparsed[name] = bytes;
	}

	void ParseUnparsed( string name )
	{
		if ( !Unparsed.TryRemove( name, out var bytes ) )
			return;

		var stream = new MemoryStream( bytes );
		var options = new ReaderParameters { ReadingMode = ReadingMode.Immediate, InMemory = true, AssemblyResolver = this };
		var assembly = AssemblyDefinition.ReadAssembly( stream, options );

		var older = Assemblies
			.Where( x => x.Key.Name.Equals( assembly.Name.Name, StringComparison.OrdinalIgnoreCase ) )
			.Where( x => x.Key.Version.CompareTo( assembly.Name.Version ) < 0 )
			.ToArray();

		foreach ( var entry in older )
		{
			if ( Assemblies.TryRemove( entry.Key, out var removed ) )
				removed.Dispose();
		}

		if ( Assemblies.TryGetValue( assembly.Name, out var replaced ) )
			replaced.Dispose();

		Assemblies[assembly.Name] = assembly;
	}

	public AssemblyDefinition Resolve( AssemblyNameReference name )
	{
		if ( Unparsed.ContainsKey( name.Name ) )
		{
			lock ( packageResolveLock )
				ParseUnparsed( name.Name );
		}

		if ( TryResolveFromCache( name, out var cached ) )
			return cached;

		if ( name.Name.StartsWith( "package.", StringComparison.OrdinalIgnoreCase ) && PackageBytes != null )
		{
			lock ( packageResolveLock )
			{
				if ( TryResolveFromCache( name, out var existing ) )
					return existing;

				var bytes = PackageBytes( name.Name );
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

		if ( !name.Name.StartsWith( "Sandbox.", StringComparison.OrdinalIgnoreCase ) &&
			 !name.Name.StartsWith( "System.", StringComparison.OrdinalIgnoreCase ) &&
			 name.Name != "Microsoft.AspNetCore.Components" )
			throw NotResolved( name );

		if ( GlobalAssemblyCache.TryGetValue( name, out var systemAssembly ) )
			return systemAssembly;

		lock ( GlobalAssemblyCache )
		{
			return GlobalAssemblyCache.GetOrAdd( name, FindAssemblyOnDisk );
		}
	}

	public AssemblyDefinition Resolve( AssemblyNameReference name, ReaderParameters parameters ) => Resolve( name );

	bool TryResolveFromCache( AssemblyNameReference name, out AssemblyDefinition assm )
	{
		assm = null;

		if ( Assemblies is null )
			return false;

		if ( Assemblies.TryGetValue( name, out assm ) )
			return true;

		var newestNameMatch = Assemblies
			.Where( x => x.Key.Name.Equals( name.Name, StringComparison.OrdinalIgnoreCase ) )
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
		var corePath = Path.GetDirectoryName( typeof( object ).Assembly.Location );
		var testPath = Path.Combine( corePath, dllName );

		if ( !File.Exists( testPath ) )
		{
			var gamePath = Path.GetDirectoryName( GetType().Assembly.Location );
			testPath = Path.Combine( gamePath, dllName );
		}

		if ( !File.Exists( testPath ) )
			throw NotResolved( name );

		var stream = new MemoryStream( File.ReadAllBytes( testPath ) );
		var options = new ReaderParameters { ReadingMode = ReadingMode.Immediate, InMemory = true, AssemblyResolver = this };
		return AssemblyDefinition.ReadAssembly( stream, options );
	}

	static Exception NotResolved( AssemblyNameReference name ) => new( $"Couldn't resolve '{name}'" );

	class AssemblyNameComparer : IEqualityComparer<AssemblyNameReference>
	{
		public static AssemblyNameComparer Instance { get; } = new();

		public bool Equals( AssemblyNameReference x, AssemblyNameReference y )
		{
			if ( ReferenceEquals( x, y ) )
				return true;
			if ( x is null || y is null )
				return false;
			return string.Equals( x.Name, y.Name, StringComparison.OrdinalIgnoreCase ) && x.Version.Equals( y.Version );
		}

		public int GetHashCode( AssemblyNameReference obj )
		{
			var hashCode = new HashCode();
			hashCode.Add( obj.Name, StringComparer.OrdinalIgnoreCase );
			hashCode.Add( obj.Version );
			return hashCode.ToHashCode();
		}
	}
}
