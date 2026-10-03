using Microsoft.CodeAnalysis;
using Mono.Cecil;
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;

namespace Sandbox;

/// <summary>
/// Test for access rules compliance. Can be shared to prevent unnecessary re-checking or resolving.
/// </summary>
[SkipHotload]
public partial class AccessControl : IAssemblyResolver
{
	internal ConcurrentDictionary<string, string> SafeAssemblies = new();
	internal ConcurrentDictionary<AssemblyNameReference, AssemblyDefinition> Assemblies = new( AssemblyNameComparer.Instance );
	internal AccessRules Rules;

	public AccessControl()
	{
		Rules = new AccessRules();
	}

	public void Dispose()
	{
		foreach ( var assm in Assemblies.Values )
		{
			assm.Dispose();
		}

		Assemblies = null;
	}

	/// <summary>
	/// Dangerous! Create a <see cref="TrustedBinaryStream"/> of the given stream
	/// without actually passing it through access control.
	/// </summary>
	public TrustedBinaryStream TrustUnsafe( byte[] dll )
	{
		// Reading the whole assembly with Cecil here only served the resolver, which needs the
		// definition when some other assembly references this one - and for package.* names
		// it already builds that on demand from the package's bytes. So just make sure a
		// definition of an older build isn't left in the cache to answer for this one. Reading
		// the name alone is cheap; the full read was over a second of editor startup.
		var name = ReadAssemblyName( dll );

		if ( name is not null && name.StartsWith( "package.", StringComparison.OrdinalIgnoreCase ) && PackageAssemblyResolver != null )
		{
			ForgetAssembly( name );
		}
		else
		{
			var instance = new AssemblyAccess( this, dll );
		}

		return TrustedBinaryStream.CreateInternal( dll );
	}

	/// <summary>
	/// The simple name of the assembly in these bytes, without building a Cecil definition of it.
	/// </summary>
	static string ReadAssemblyName( byte[] dll )
	{
		try
		{
			using var pe = new System.Reflection.PortableExecutable.PEReader( new MemoryStream( dll ) );
			var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader( pe );
			return metadata.GetString( metadata.GetAssemblyDefinition().Name );
		}
		catch ( Exception )
		{
			return null;
		}
	}

	/// <summary>
	/// Checks if an assembly passes whitelist checks.
	/// </summary>
	/// <param name="dll"></param>
	/// <param name="outStream"><see cref="TrustedBinaryStream"/> wrapper around <paramref name="dll"/>.
	/// Will close if <paramref name="dll"/> gets closed.</param>
	/// <param name="addToWhitelist"></param>
	public AccessControlResult VerifyAssembly( Stream dll, out TrustedBinaryStream outStream, bool addToWhitelist = true )
	{
		var ms = new MemoryStream();
		dll.CopyTo( ms );
		var bytes = ms.ToArray();
		ms.Dispose();

		var instance = new AssemblyAccess( this, bytes );
		instance.Verify( out outStream );

		if ( addToWhitelist && instance.Result.Success )
		{
			AddSafeAssembly( instance.Assembly.Name.Name, bytes );
		}

		return instance.Result;
	}

	/// <summary>
	/// If we're definitely never going to see this assembly again (because it's being unloaded for instance)
	/// We can totally get rid of it and free all that lovely memory.
	/// </summary>
	public void ForgetAssembly( string name )
	{
		RemoveSafeAssembly( name );

		var matches = Assemblies
			.Where( x => x.Key.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) )
			.ToArray();

		foreach ( var match in matches )
		{
			if ( Assemblies.Remove( match.Key, out var assembly ) )
			{
				assembly.Dispose();
			}
		}
	}

	/// <summary>
	/// Forget all versions of the named assembly strictly older than this one.
	/// </summary>
	public void ForgetOlderAssemblyDefinitions( AssemblyName name )
	{
		ForgetOlderAssemblyDefinitions( new AssemblyNameReference( name.Name, name.Version ) );
	}

	/// <summary>
	/// Forget all versions of the named assembly strictly older than this one.
	/// </summary>
	public void ForgetOlderAssemblyDefinitions( AssemblyNameReference name )
	{
		var matches = Assemblies
			.Where( x => x.Key.Name.Equals( name.Name, StringComparison.OrdinalIgnoreCase ) )
			.Where( x => x.Key.Version.CompareTo( name.Version ) < 0 )
			.ToArray();

		foreach ( var match in matches )
		{
			if ( Assemblies.Remove( match.Key, out var assembly ) )
			{
				assembly.Dispose();
			}
		}
	}

	internal void AddSafeAssembly( string name, byte[] data )
	{
		using var sha256 = SHA256.Create();
		var hash = Convert.ToBase64String( sha256.ComputeHash( data ) );
		SafeAssemblies[name] = hash;
	}
	internal bool CheckSafeAssembly( string name, byte[] data )
	{
		using var sha256 = SHA256.Create();
		var hash = Convert.ToBase64String( sha256.ComputeHash( data ) );
		return SafeAssemblies.TryGetValue( name, out var existing ) && existing == hash;
	}
	internal bool RemoveSafeAssembly( string name ) => SafeAssemblies.Remove( name, out _ );

	public struct CodeLocation
	{
		public string Text;
		public Location RoslynLocation = null;

		public CodeLocation( string text )
		{
			// most definitions will not have accurate symbols/locations, this is the best we can get
			Text = text;
		}

		public CodeLocation( Location location )
		{
			Text = location.ToString();
			RoslynLocation = location;
		}
	}
}
