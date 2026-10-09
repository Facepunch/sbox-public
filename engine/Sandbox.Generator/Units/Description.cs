using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Sandbox.Generator;

/// <summary>
/// Parses docstrings and puts them in a Description attribute for reflection
/// </summary>
internal static class Description
{
	public class MemberDocumentation
	{
		public string Summary { get; }
		public string Returns { get; }

		public IReadOnlyDictionary<string, string> Parameters { get; }

		public static MemberDocumentation FromSymbol( ISymbol symbol, Worker master )
		{
			var xml = symbol.GetDocumentationCommentXml();
			if ( string.IsNullOrEmpty( xml ) ) return Empty;

			try
			{
				return Parse( xml, master );
			}
			catch
			{
				return Empty;
			}
		}

		public static MemberDocumentation Parse( string xml, Worker master )
		{
			var doc = XDocument.Parse( xml );
			if ( doc is null ) return Empty;

			string summary = null;
			string returns = null;
			Dictionary<string, string> parameters = null;

			if ( doc.Descendants( "summary" ).FirstOrDefault() is { } summaryElem )
			{
				summary = FormatElementContents( summaryElem, master ).Trim();
			}

			if ( doc.Descendants( "returns" ).FirstOrDefault() is { } returnsElem )
			{
				returns = FormatElementContents( returnsElem, master ).Trim();
			}

			foreach ( var paramElem in doc.Descendants( "param" ) )
			{
				if ( paramElem.Attribute( "name" ) is { Value: { } paramName } )
				{
					parameters ??= new();
					parameters[paramName] = FormatElementContents( paramElem, master ).Trim();
				}
			}

			return new MemberDocumentation( summary, returns, parameters );
		}

		private static string FormatNode( XNode node, Worker master )
		{
			return node switch
			{
				XElement element => FormatElement( element, master ),
				XText text => FormatText( text ),
				_ => ""
			};
		}

		private static string FormatElement( XElement element, Worker master )
		{
			var name = element.Name.LocalName;

			switch ( name )
			{
				//
				// garry: this is fucking disgusting
				//

				//
				// ziks: yep. I don't like how we have to embed styles instead of using classes, but
				//       I don't think we can apply style sheets to tool tip text. Also, we wouldn't
				//       have symbol info if we parsed this stuff at runtime
				//

				case "para":
					return $"<br/>{FormatElementContents( element, master ).Trim()}<br/>";

				case "c":
					return $"<code style=\"color: #DCDCDC; background-color: #111111; padding: 4px;\">{EscapeAngleBrackets( element.Value )}</code>";

				case "code":
					return $"<pre style=\"color: #DCDCDC; background-color: #111111;\"><code>{EscapeAngleBrackets( element.Value )}</code></pre>";

				case "see":
					return FormatSeeElement( element, master );

				case "list":
					return FormatList( element, master );

				case "paramref":
				case "typeparamref":
					return FormatParamRef( element.Attribute( "name" )?.Value );

				default:
					return $"<{name}>{FormatElementContents( element, master )}</{name}>";
			}
		}

		private static string FormatSeeElement( XElement element, Worker master )
		{
			if ( element.Attribute( "langword" )?.Value is { } langWord )
			{
				return WithColor( langWord, "#569CD6" );
			}

			if ( element.Attribute( "cref" )?.Value is { } cref )
			{
				return FormatCodeRef( cref, master );
			}

			if ( element.Attribute( "href" )?.Value is { } href )
			{
				return $"<a href=\"{href}\">{(string.IsNullOrWhiteSpace( element.Value ) ? href : element.Value)}</a>";
			}

			return element.Value;
		}

		private static string FormatCodeRef( string value, Worker master )
		{
			if ( value.Split( ':' ) is not { Length: 2 } split ) return value;

			var kind = split[0];
			value = split[1];

			string typeName;
			string memberSig;

			if ( kind == "T" )
			{
				typeName = value;
				memberSig = null;
			}
			else
			{
				(typeName, memberSig) = SplitSymbolName( value );
			}

			var type = master.GetOrCreateTypeByMetadataName( typeName );

			if ( type is null )
			{
				return value;
			}

			ISymbol member = kind switch
			{
				"T" => type,
				"F" => type.GetMembers( memberSig! ).OfType<IFieldSymbol>().FirstOrDefault(),
				"P" => type.GetMembers( memberSig! ).OfType<IPropertySymbol>().FirstOrDefault(),
				"M" => GetMethod( type, memberSig ),
				_ => null
			};

			return member is null ? value : FormatMemberRef( member );
		}

		private static (string TypeName, string MemberSig) SplitSymbolName( string value )
		{
			var endIndex = value.Length;

			if ( value.IndexOf( '(' ) is var bracketIndex and > 0 )
			{
				endIndex = bracketIndex;
			}

			return value.LastIndexOf( '.', endIndex - 1 ) is var periodIndex and > 0
				? (value.Substring( 0, periodIndex ), value.Substring( periodIndex + 1 ))
				: (value, null);
		}

		private static IMethodSymbol GetMethod( INamedTypeSymbol type, string methodSig )
		{
			var name = methodSig.Substring( 0, methodSig.IndexOf( '(' ) );

			// TODO: check parameters

			return type.GetMembers( name ).OfType<IMethodSymbol>().FirstOrDefault();
		}

		private static string FormatMemberRef( ISymbol symbol )
		{
			var prefix = symbol.ContainingType is { } parentType
				? $"{FormatMemberRef( parentType )}."
				: "";

			if ( symbol is INamedTypeSymbol typeSymbol )
			{
				var displayName = symbol.ToDisplayString( SymbolDisplayFormat.MinimallyQualifiedFormat );

				return typeSymbol switch
				{
					{ IsValueType: true } => $"{prefix}{WithColor( displayName, "#86C691" )}",
					_ => $"{prefix}{WithColor( displayName, "#4EC9B0" )}"
				};
			}

			return symbol switch
			{
				IMethodSymbol method => $"{prefix}{FormatMethodRef( method )}",
				ITypeParameterSymbol => $"{prefix}{WithColor( symbol.Name, "#B8D7A3" )}",
				_ => $"{prefix}{WithColor( symbol.Name, "#DCDCDC" )}"
			};
		}

		private static string WithColor( string value, string color )
		{
			return $"<span style=\"color: {color};\">{value}</span>";
		}

		private static string FormatMethodRef( IMethodSymbol method )
		{
			var result = WithColor( method.Name, "#DCDCAA" );

			if ( method.TypeParameters is { Length: > 0 } typeParams )
			{
				result += "&lt;";
				result += string.Join( ", ", typeParams.Select( FormatMemberRef ) );
				result += "&gt;";
			}

			result += "(";
			result += string.Join( ",", method.Parameters.Select( x => FormatMemberRef( x.Type ) ) );
			result += ")";

			return result;
		}

		private static string FormatParamRef( string value )
		{
			return WithColor( value, "#9CDCFE" );
		}

		private static string FormatList( XElement element, Worker master )
		{
			var type = element.Attribute( "type" )?.Value.ToLower();
			var items = element.Elements( "item" )
				.Select( x => (
					Term: FormatElementContents( x.Element( "term" ), master ),
					Desc: FormatElementContents( x.Element( "description" ), master )) );

			switch ( type )
			{
				case "table":
					var header = element.Element( "listheader" ) is { } headerElement
						? $"<tr style=\"background-color: #1C1C1C\">" +
							$"<th>{FormatElementContents( headerElement.Element( "term" ), master )}</th>" +
							$"<th>{FormatElementContents( headerElement.Element( "description" ), master )}</th>" +
						$"</tr>"
						: "";

					return $"<br/><table cellpadding=\"4\" width=\"100%\" style=\"background-color: #111111\">{header}{string.Join( "", items.Select( x => $"<tr style=\"background-color: #161616\"><td>{x.Term}</td><td>{x.Desc}</td></tr>" ) )}</table><br/>";

				case "number":
					return $"<ol style=\"margin-left: 0; -qt-block-indent: 0;\">{string.Join( "", items.Select( x => $"<li>{x.Desc}</li>" ) )}</ol>";

				default:
					return $"<ul style=\"margin-left: 0; -qt-block-indent: 0;\">{string.Join( "", items.Select( x => $"<li>{x.Desc}</li>" ) )}</ul>";
			}
		}

		private static string FormatElementContents( XElement element, Worker master )
		{
			if ( element is null ) return "";
			return string.Join( "", element.Nodes().Select( x => FormatNode( x, master ) ) );
		}

		private static Regex Whitespace { get; } = new Regex( @"\s+" );

		private static string CollapseWhitespace( string value )
		{
			return Whitespace.Replace( value, " " );
		}

		private static string FormatText( XText text )
		{
			return EscapeAngleBrackets( CollapseWhitespace( text.Value ) );
		}

		private static string EscapeAngleBrackets( string value )
		{
			return value.Replace( "<", "&lt;" ).Replace( ">", "&gt;" );
		}

		public static MemberDocumentation Empty { get; } = new();

		private MemberDocumentation(
			string summary = null,
			string returns = null,
			IReadOnlyDictionary<string, string> parameters = null )
		{
			Summary = summary;
			Returns = returns;
			Parameters = parameters ?? ImmutableDictionary<string, string>.Empty;
		}
	}

	internal static void VisitProperty( ref PropertyDeclarationSyntax node, IPropertySymbol symbol, Worker master )
	{
		if ( !master.IsFullGeneration ) return;

		var memberDoc = MemberDocumentation.FromSymbol( symbol, master );
		node = AppendDescriptionAttribute( node, null, memberDoc.Summary, symbol, master );
	}

	internal static void VisitEnumMember( ref EnumMemberDeclarationSyntax node, IFieldSymbol symbol, Worker master )
	{
		if ( !master.IsFullGeneration ) return;

		var memberDoc = MemberDocumentation.FromSymbol( symbol, master );
		node = AppendDescriptionAttribute( node, null, memberDoc.Summary, symbol, master );
	}

	internal static void VisitMethod( ref MethodDeclarationSyntax node, IMethodSymbol symbol, Worker master )
	{
		if ( !master.IsFullGeneration ) return;

		var memberDoc = MemberDocumentation.FromSymbol( symbol, master );
		node = AppendDescriptionAttribute( node, null, memberDoc.Summary, symbol, master );
		node = AppendDescriptionAttribute( node, SyntaxKind.ReturnKeyword, memberDoc.Returns, symbol, master );

		// Sadly we can't do this by visiting parameters: their symbols don't contain their comments

		try
		{
			if ( memberDoc.Parameters.Count == 0 ) return;

			var parameters = node.ParameterList.Parameters;

			foreach ( var paramDoc in memberDoc.Parameters )
			{
				var match = parameters.FirstOrDefault( x => (string)x.Identifier.Value == paramDoc.Key );
				if ( match is null ) continue;

				var paramSymbol = symbol.Parameters.FirstOrDefault( x => x.Name == paramDoc.Key );
				if ( paramSymbol is null ) continue;

				parameters = parameters.Replace( match,
					AppendDescriptionAttribute( match, null, paramDoc.Value, paramSymbol, master ) );
			}

			node = node.WithParameterList( node.ParameterList.WithParameters( parameters ) );
		}
		catch ( Exception e )
		{
			System.Diagnostics.Debug.WriteLine( $"Failed to process parameter comments for {symbol.Name}: {e}" );
		}
	}

	internal static void VisitType<T>( ref T node, T original, INamedTypeSymbol symbol, Worker master )
		where T : MemberDeclarationSyntax
	{
		if ( !master.IsFullGeneration ) return;

		// All parts share the symbol's documentation. Emit once, on its first declaration,
		// regardless of which file contains the summary or which worker runs first.
		var declaration = symbol.DeclaringSyntaxReferences.FirstOrDefault();
		if ( declaration is null || declaration.SyntaxTree != original.SyntaxTree || declaration.Span != original.Span ) return;

		var memberDoc = MemberDocumentation.FromSymbol( symbol, master );
		node = AppendDescriptionAttribute( node, null, memberDoc.Summary, symbol, master );
	}

	private static T AppendDescriptionAttribute<T>( T node, SyntaxKind? target, string description, ISymbol symbol, Worker master )
		where T : CSharpSyntaxNode
	{
		// Imagine if C# had duck typing

		switch ( node )
		{
			case MemberDeclarationSyntax:
				return AppendDescriptionAttribute( node, target, description, symbol, master,
					( nd, list ) => (T)(CSharpSyntaxNode)((MemberDeclarationSyntax)(CSharpSyntaxNode)nd).AddAttributeLists( list ) );
			case BaseParameterSyntax:
				return AppendDescriptionAttribute( node, target, description, symbol, master,
					( nd, list ) => (T)(CSharpSyntaxNode)((BaseParameterSyntax)(CSharpSyntaxNode)nd).AddAttributeLists( list ) );
			default:
				throw new NotImplementedException();
		}
	}

	private static T AppendDescriptionAttribute<T>( T node, SyntaxKind? target, string description, ISymbol symbol, Worker master, Func<T, AttributeListSyntax, T> addAttribLists )
		where T : CSharpSyntaxNode
	{
		if ( string.IsNullOrEmpty( description ) ) return node;

		if ( symbol.GetAttribute( "DescriptionAttribute" ) != null )
		{
			master.AddError( node.GetLocation(), new DiagnosticDescriptor( "SB2000", "Unneeded [Description]", $"Why are you using [Description] here? We'll automatically add one using the comment above.", "generator", DiagnosticSeverity.Warning, true ) );
			return node;
		}

		//master.AddError( node.GetLocation(), $"{comment}" );

		var name = ParseName( "DescriptionAttribute" );
		var arguments = ParseAttributeArgumentList( $"( {description.QuoteSafe()} )" );
		var attribute = Attribute( name, arguments );

		var attributeList = new SeparatedSyntaxList<AttributeSyntax>();
		attributeList = attributeList.Add( attribute );

		var targetSyntax = target is null ? null : AttributeTargetSpecifier( Token( SyntaxKind.ReturnKeyword ) );
		var list = AttributeList( targetSyntax, attributeList );
		var trivia = node.GetLeadingTrivia();

		return addAttribLists( node.WithoutLeadingTrivia(), list ).WithLeadingTrivia( trivia );
	}
}
