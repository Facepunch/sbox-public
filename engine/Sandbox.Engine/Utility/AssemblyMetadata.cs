using Mono.Cecil;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sandbox;

public static class AssemblyMetadata
{
	static readonly ConditionalWeakTable<Assembly, PackageIdentity> PackageIdentities = new();

	sealed record PackageIdentity( string Ident );

	/// <summary>
	/// Read the package identity written by the project compiler, or null for assemblies without one.
	/// </summary>
	internal static string GetPackageIdent( Assembly assembly )
	{
		if ( assembly is null )
		{
			return null;
		}

		// Cache reflection results without keeping unloaded package assemblies alive.
		return PackageIdentities.GetValue( assembly, static assembly =>
		{
			var ident = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
				.FirstOrDefault( attribute => attribute.Key == "Ident" )?.Value;

			return new PackageIdentity( string.IsNullOrWhiteSpace( ident ) ? null : ident );
		} ).Ident;
	}

	public struct Attribute
	{
		public string AttributeType { get; }
		public string AttributeFullName { get; }
		public object[] Arguments { get; }

		public Attribute( CustomAttribute x )
		{
			AttributeType = x.AttributeType.Name;
			AttributeFullName = x.AttributeType.FullName;
			Arguments = x.ConstructorArguments.Select( a => a.Value ).ToArray();
		}
	}

	public static Attribute[] GetCustomAttributes( byte[] assemblyData )
	{
		using var ms = new MemoryStream( assemblyData );
		using var assembly = AssemblyDefinition.ReadAssembly( ms );

		return assembly.CustomAttributes.Select( x => new Attribute( x ) ).ToArray();
	}
}
