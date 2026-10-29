using System;

namespace Sandbox.CodeUpgrader;

#nullable enable

static class SymbolHelpers
{
	public static bool IsAutoProperty( ISymbol? symbol )
	{
		if ( symbol is not IPropertySymbol propertySymbol ) return false;

		return propertySymbol.ContainingType
			.GetMembers()
			.OfType<IFieldSymbol>()
			.Any( field => SymbolEqualityComparer.Default.Equals( field.AssociatedSymbol, propertySymbol ) );
	}

	public static bool HasAttribute( ISymbol symbol, params string[] names )
	{
		foreach ( var attribute in symbol.GetAttributes() )
		{
			var name = attribute.AttributeClass?.ToDisplayString();
			if ( name is not null && Array.IndexOf( names, name ) >= 0 ) return true;
		}

		return false;
	}

	public static bool DerivesFrom( ITypeSymbol? type, string fullName )
	{
		for ( var t = type; t is not null; t = t.BaseType )
		{
			if ( t.ToDisplayString() == fullName ) return true;
		}

		return false;
	}

	/// <summary>
	/// Whether <paramref name="type"/> is, or implements, any of the named types (generic definitions match any construction).
	/// </summary>
	public static bool Implements( ITypeSymbol type, Compilation compilation, params string[] metadataNames )
	{
		foreach ( var name in metadataNames )
		{
			if ( FindInterface( type, compilation, name ) is not null ) return true;
		}
		return false;
	}

	/// <summary>
	/// <paramref name="type"/> itself, or the interface it implements, whose definition is the named type.
	/// </summary>
	public static INamedTypeSymbol? FindInterface( ITypeSymbol type, Compilation compilation, string metadataName )
	{
		var contract = compilation.GetTypeByMetadataName( metadataName );
		if ( contract is null ) return null;

		if ( type is INamedTypeSymbol named && SymbolEqualityComparer.Default.Equals( named.OriginalDefinition, contract ) )
			return named;

		return type.AllInterfaces.FirstOrDefault( x => SymbolEqualityComparer.Default.Equals( x.OriginalDefinition, contract ) );
	}
}
