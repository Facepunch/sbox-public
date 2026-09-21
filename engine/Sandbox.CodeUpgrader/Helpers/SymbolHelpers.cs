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
}
