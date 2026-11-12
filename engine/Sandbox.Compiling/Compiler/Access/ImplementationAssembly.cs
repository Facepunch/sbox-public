using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace Sandbox;

/// <summary>
/// Rules name the runtime assembly that defines a type (System.Private.CoreLib, System.ComponentModel.TypeConverter),
/// but compilations bind against reference assemblies (System.Runtime, System.ComponentModel.Primitives).
/// Follows the type forward through the loaded runtime so both sides agree on the assembly.
/// </summary>
static class ImplementationAssembly
{
	static readonly ConcurrentDictionary<string, string> _cache = new();

	public static string Of( INamedTypeSymbol type )
	{
		var reference = type.ContainingAssembly?.Name;
		if ( reference is null )
			return null;

		var metadataName = MetadataName( type.OriginalDefinition );
		return _cache.GetOrAdd( reference + "/" + metadataName, _ => Resolve( reference, metadataName ) );
	}

	static string Resolve( string reference, string metadataName )
	{
		try
		{
			var runtimeType = System.Reflection.Assembly.Load( reference ).GetType( metadataName, throwOnError: false );
			return runtimeType?.Assembly.GetName().Name ?? reference;
		}
		catch ( Exception )
		{
			return reference;
		}
	}

	static string MetadataName( INamedTypeSymbol type )
	{
		if ( type.ContainingType is { } outer )
			return MetadataName( outer ) + "+" + type.MetadataName;

		if ( type.ContainingNamespace is { IsGlobalNamespace: false } ns )
			return ns.ToDisplayString() + "." + type.MetadataName;

		return type.MetadataName;
	}
}
