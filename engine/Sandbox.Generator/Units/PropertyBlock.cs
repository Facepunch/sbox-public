using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Generator
{
	internal static class PropertyBlock
	{
		/// <summary>
		/// Scans a class's members in source order and returns a map of PropertyItem nodes
		/// to the attribute lists they should inherit from the preceding PropertyBlock.
		/// </summary>
		internal static Dictionary<SyntaxNode, SyntaxList<AttributeListSyntax>> ScanClass( ClassDeclarationSyntax classNode )
		{
			var map = new Dictionary<SyntaxNode, SyntaxList<AttributeListSyntax>>();
			SyntaxList<AttributeListSyntax> currentBlock = default;
			bool hasBlock = false;

			foreach ( var member in classNode.Members )
			{
				if ( HasAttr( member.AttributeLists, "PropertyBlock" ) )
				{
					currentBlock = BuildInherited( member.AttributeLists );
					hasBlock = true;
				}

				if ( hasBlock && HasAttr( member.AttributeLists, "PropertyItem" ) )
					map[member] = currentBlock;
			}

			return map;
		}

		internal static void VisitProperty( ref PropertyDeclarationSyntax node, PropertyDeclarationSyntax original, Dictionary<SyntaxNode, SyntaxList<AttributeListSyntax>> map )
		{
			node = (PropertyDeclarationSyntax)Apply( node, original, map );
		}

		internal static void VisitField( ref FieldDeclarationSyntax node, FieldDeclarationSyntax original, Dictionary<SyntaxNode, SyntaxList<AttributeListSyntax>> map )
		{
			node = (FieldDeclarationSyntax)Apply( node, original, map );
		}

		static MemberDeclarationSyntax Apply( MemberDeclarationSyntax node, MemberDeclarationSyntax original, Dictionary<SyntaxNode, SyntaxList<AttributeListSyntax>> map )
		{
			bool hasMarkers = HasAttr( node.AttributeLists, "PropertyBlock" ) || HasAttr( node.AttributeLists, "PropertyItem" );
			bool hasInherited = map.TryGetValue( original, out var inherited );

			if ( !hasMarkers && !hasInherited ) return node;

			if ( hasMarkers ) node = Strip( node );
			if ( hasInherited ) node = node.WithAttributeLists( node.AttributeLists.AddRange( inherited ) );

			return node;
		}

		// Removes [PropertyBlock] and [PropertyItem] markers from the compiled output.
		// If the member is the PropertyBlock source, all attributes are cleared since
		// Apply will re-add them correctly via the inherited path.
		static MemberDeclarationSyntax Strip( MemberDeclarationSyntax node )
		{
			if ( HasAttr( node.AttributeLists, "PropertyBlock" ) )
				return node.WithAttributeLists( SyntaxFactory.List<AttributeListSyntax>() );

			var newLists = new List<AttributeListSyntax>();

			foreach ( var list in node.AttributeLists )
			{
				var kept = list.Attributes.Where( a => !IsMarker( a ) ).ToArray();
				if ( kept.Length == 0 ) continue;
				newLists.Add( kept.Length == list.Attributes.Count
					? list
					: list.WithAttributes( SyntaxFactory.SeparatedList( kept ) ) );
			}

			return node.WithAttributeLists( SyntaxFactory.List( newLists ) );
		}

		// Builds the attribute lists to inject into PropertyItem members:
		// replaces PropertyBlock with Property, drops PropertyItem
		static SyntaxList<AttributeListSyntax> BuildInherited( SyntaxList<AttributeListSyntax> source )
		{
			var result = new List<AttributeListSyntax>();

			foreach ( var list in source )
			{
				var attrs = list.Attributes
					.Where( a => !IsPropertyItem( a ) )
					.Select( a => IsPropertyBlock( a ) ? a.WithName( SyntaxFactory.ParseName( "Property" ) ) : a )
					.ToArray();

				if ( attrs.Length > 0 )
					result.Add( list.WithAttributes( SyntaxFactory.SeparatedList( attrs ) ) );
			}

			return SyntaxFactory.List( result );
		}

		static bool HasAttr( SyntaxList<AttributeListSyntax> lists, string name )
			=> lists.SelectMany( l => l.Attributes ).Any( a => Matches( a, name ) );

		static bool IsMarker( AttributeSyntax a ) => IsPropertyBlock( a ) || IsPropertyItem( a );
		static bool IsPropertyBlock( AttributeSyntax a ) => Matches( a, "PropertyBlock" );
		static bool IsPropertyItem( AttributeSyntax a ) => Matches( a, "PropertyItem" );

		static bool Matches( AttributeSyntax attr, string name )
		{
			var n = attr.Name.ToString();
			return n == name || n == name + "Attribute";
		}
	}
}
