using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Sandbox;

static class SymbolSignature
{
	public static string Type( ITypeSymbol t ) => TypeName( t, false );

	static string TypeName( ITypeSymbol t, bool withArgs )
	{
		switch ( t )
		{
			case IArrayTypeSymbol a: return TypeName( a.ElementType, withArgs ) + "[]";
			case IPointerTypeSymbol p: return TypeName( p.PointedAtType, withArgs ) + "*";
			case ITypeParameterSymbol tp: return tp.Name;
			case INamedTypeSymbol n: return Named( n, withArgs );
			default: return t.ToDisplayString();
		}
	}

	static string Named( INamedTypeSymbol n, bool withArgs )
	{
		var name = n.Name;
		if ( n.Arity > 0 )
			name += "`" + n.Arity;

		string full;
		if ( n.ContainingType is { } outer )
			full = Named( outer, false ) + "/" + name;
		else if ( n.ContainingNamespace is { IsGlobalNamespace: false } ns )
			full = ns.ToDisplayString() + "." + name;
		else
			full = name;

		if ( withArgs && n.Arity > 0 && !n.IsUnboundGenericType )
			full += "<" + string.Join( ",", n.TypeArguments.Select( a => TypeName( a, true ) ) ) + ">";

		return full;
	}

	public static string Method( IMethodSymbol m )
	{
		m = m.OriginalDefinition;

		var sb = new StringBuilder();
		sb.Append( Type( m.ContainingType.OriginalDefinition ) ).Append( '.' ).Append( m.Name );

		if ( m.TypeParameters.Length > 0 )
			sb.Append( '<' ).Append( string.Join( ",", m.TypeParameters.Select( p => p.Name ) ) ).Append( '>' );

		if ( m.Parameters.Length > 0 )
			sb.Append( "( " ).Append( string.Join( ", ", m.Parameters.Select( Param ) ) ).Append( " )" );
		else
			sb.Append( "()" );

		return sb.ToString();
	}

	static string Param( IParameterSymbol p )
	{
		var s = TypeName( p.Type, true );
		if ( p.RefKind != RefKind.None )
			s += "&";
		return s;
	}
}
