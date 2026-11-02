using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Sandbox.Generator
{
	internal class DefaultValue
	{
		internal static void WriteAttribute( ref PropertyDeclarationSyntax node )
		{
			var name = ParseName( "DefaultValueAttribute" );
			var arguments = ParseAttributeArgumentList( $"( {node.Initializer.Value} )" );
			var attribute = Attribute( name, arguments );

			var attributeList = new SeparatedSyntaxList<AttributeSyntax>();
			attributeList = attributeList.Add( attribute );

			var list = AttributeList( attributeList );

			var trivia = node.GetLeadingTrivia();

			node = node.WithoutLeadingTrivia().WithAttributeLists( node.AttributeLists.Add( list ) ).WithLeadingTrivia( trivia );
		}

		internal static void VisitProperty( ref PropertyDeclarationSyntax node, IPropertySymbol symbol, Worker master )
		{
			if ( !master.IsFullGeneration )
				return;

			//
			// Can't auto add a DefaultValue if not an auto property (because it won't have a default value)
			//
			if ( !symbol.IsAutoProperty() )
				return;

			// Has it already got a DefaultValue attribute?
			var hasExistingAttribute = symbol.GetAttribute( "DefaultValueAttribute" ) != null;

			// Is this an easy type to have a default value for?
			bool isEasyType = symbol.Type.TypeKind == TypeKind.Enum || symbol.Type.SpecialType == SpecialType.System_String || symbol.Type.SpecialType == SpecialType.System_Int32 || symbol.Type.SpecialType == SpecialType.System_Boolean || symbol.Type.SpecialType == SpecialType.System_Single || symbol.Type.SpecialType == SpecialType.System_Enum;

			// Complain that they shouldn't be using the attribute unless they really need to
			if ( hasExistingAttribute && isEasyType )
			{
				master.AddError( node.GetLocation(), new DiagnosticDescriptor( "SB2000", "Oops", $"Why are you using [DefaultValue] here? Just set the default value.", "generator", DiagnosticSeverity.Warning, true ) );
				return;
			}

			// No initializer - can't have a default type anyway
			if ( node.Initializer == null ) return;

			// It's a class - can't really do a default type here
			if ( symbol.Type.TypeKind == TypeKind.Class && symbol.Type.SpecialType != SpecialType.System_String ) return;

			// See if the initializer looks like a string
			var initializer = node.Initializer.Value;
			var strValue = initializer.ToString();
			var isString = strValue.StartsWith( "\"" ) && initializer.ToString().EndsWith( "\"" );

			// Not a string literal, but property type is a string, BAAAIL. (shit like string.Empty and nulls)
			if ( !isString && symbol.Type.SpecialType == SpecialType.System_String )
			{
				return;
			}

			// See if its a nullable. Ideally we'd extract the underlying type, but how?
			// update: I have no idea what this does
			if ( !isString && symbol.Type.TypeKind == TypeKind.Structure && symbol.NullableAnnotation == NullableAnnotation.Annotated && (int.TryParse( strValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _ ) || float.TryParse( strValue, NumberStyles.Float, CultureInfo.InvariantCulture, out _ )) )
			{
				WriteAttribute( ref node );
				return;
			}

			//
			// If it's a simple looking value, just write the attribute with it
			//
			if ( isEasyType || int.TryParse( strValue, out _ ) || float.TryParse( strValue.Trim( 'f' ), System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out _ ) )
			{
				WriteAttribute( ref node );
				return;
			}

			//
			// We've determined that we can't use a special type for this.
			//
			if ( !isString && !isEasyType )
			{
				// Uncomment this to see all the types we're not converting to a DefaultValue type
				//master.AddError( node.GetLocation(), new DiagnosticDescriptor( "SB2000", "Oops", $"DefaultValue.VisitProperty {symbol.Type.SpecialType} Unsupported type '{node.Type}' for variable '{symbol.Name}' ({initializer})", "generator", DiagnosticSeverity.Warning, true ) );
				return;
			}

			// TODO - if node.Initializer.Value is anything more than a literal we need to bail
			WriteAttribute( ref node );
		}
	}
}
