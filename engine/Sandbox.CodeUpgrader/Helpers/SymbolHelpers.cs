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
			var contract = compilation.GetTypeByMetadataName( name );
			if ( contract is null ) continue;
			if ( SymbolEqualityComparer.Default.Equals( type.OriginalDefinition, contract )
				|| type.AllInterfaces.Any( x => SymbolEqualityComparer.Default.Equals( x.OriginalDefinition, contract ) ) )
				return true;
		}
		return false;
	}
}
