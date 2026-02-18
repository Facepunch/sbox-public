using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
using System.Text;

namespace Sandbox.Generator
{
	public static class RoslynExtensions
	{
		/// <summary>
		/// Return true if we have the named attribute
		/// </summary>
		public static bool HasAttribute( this MemberDeclarationSyntax symbol, string name )
		{
			return symbol.GetAttribute( name ) != null;
		}

		public static AttributeSyntax GetAttribute( this MemberDeclarationSyntax symbol, string name )
		{
			return symbol.AttributeLists.SelectMany( x => x.Attributes )
								.FirstOrDefault( y => y.Name.ToString() == name || y.Name.ToString() == name + "Attribute" );
		}

		public static bool HasAttribute( this IMethodSymbol symbol, string name )
		{
			return symbol.GetAttribute( name ) != null;
		}

		public static AttributeData GetAttribute( this IMethodSymbol symbol, string name )
		{
			return symbol.GetAttributes().FirstOrDefault( y => y.AttributeClass.Name == name || y.AttributeClass.Name == name + "Attribute" || y.AttributeClass.FullName() == name );
		}

		public static AttributeData GetAttribute( this INamedTypeSymbol symbol, string name )
		{
			return symbol.GetAttributes().FirstOrDefault( y => y.AttributeClass.Name == name || y.AttributeClass.Name == name + "Attribute" || y.AttributeClass.FullName() == name );
		}

		public static bool HasAttribute( this IPropertySymbol symbol, string name )
		{
			return symbol.GetAttribute( name ) != null;
		}

		public static AttributeData GetAttribute( this ISymbol symbol, string name )
		{
			return symbol.GetAttributes().FirstOrDefault( y => y.AttributeClass.Name == name || y.AttributeClass.Name == name + "Attribute" || y.AttributeClass.FullName() == name );
		}

		public static AttributeData GetAttribute( this IPropertySymbol symbol, string parentName, string name )
		{
			return symbol.GetAttributes().FirstOrDefault( y => y.AttributeClass.ContainingType != null && y.AttributeClass.ContainingType.Name == parentName && (y.AttributeClass.Name == name || y.AttributeClass.Name == name + "Attribute") );
		}

		/// <summary>
		/// Return true if we have the named attribute
		/// </summary>
		public static AttributeArgumentSyntax GetArgument( this AttributeArgumentListSyntax list, int position, string name )
		{
			if ( list == null )
				return null;

			var args = list.Arguments;

			var arg = args.FirstOrDefault( x => x.NameColon != null && x.NameColon.Name.ToString() == name );
			if ( arg != null ) return arg;

			arg = args.FirstOrDefault( x => x.NameEquals != null && x.NameEquals.Name.ToString() == name );
			if ( arg != null ) return arg;

			if ( args.Count <= position )
				return null;

			return args[position];
		}

		/// <summary>
		/// Return true if we have the named attribute
		/// </summary>
		public static string GetArgumentValue( this AttributeData ad, int position, string name, string defaultValue = default )
		{
			if ( ad == null )
				return defaultValue;

			var named = ad.NamedArguments.FirstOrDefault( x => x.Key == name );
			if ( named.Key == name ) return named.Value.Value.ToString();

			if ( position < 0 || ad.ConstructorArguments.Count() <= position )
				return defaultValue;

			return ad.ConstructorArguments[position].Value?.ToString() ?? defaultValue;
		}

		/// <summary>
		/// Return true if we have the named attribute
		/// </summary>
		public static string GetArgumentValue( this AttributeSyntax ad, int position, string name, string defaultValue = default )
		{
			if ( ad == null || ad.ArgumentList == null )
				return defaultValue;

			int i = 0;
			foreach ( var arg in ad.ArgumentList.Arguments )
			{
				if ( arg.NameColon != null && arg.NameColon.Name.ToString() == name )
					return arg.Expression.ToString();

				if ( arg.NameEquals != null && arg.NameEquals.Name.ToString() == name )
					return arg.Expression.ToString();

				if ( arg.NameColon == null )
				{
					if ( i == position )
						return arg.Expression.ToString();

					i++;
				}
			}

			return defaultValue;
		}

		/// <summary>
		/// Add a statement to the front of the method body
		/// </summary>
		public static MethodDeclarationSyntax AddStatementToFront( this MethodDeclarationSyntax node, string statement )
		{
			if ( node.Body != null )
			{
				var body = node.Body;

				var statements = body.Statements;
				statements = statements.Insert( 0, SyntaxFactory.ParseStatement( statement ) );

				body = body.WithStatements( statements );

				return node.WithBody( body );
			}
			else
			{
				// Recreate the method declaration without an arrow expression
				var declaration = SyntaxFactory.MethodDeclaration( node.AttributeLists, node.Modifiers, node.ReturnType, node.ExplicitInterfaceSpecifier, node.Identifier, node.TypeParameterList, node.ParameterList, node.ConstraintClauses, null, node.SemicolonToken );

				StatementSyntax callStatement;
				if ( !node.ReturnType.IsKind( SyntaxKind.PredefinedType ) || (node.ReturnType as PredefinedTypeSyntax).Keyword.Text != "void" )
				{
					callStatement = SyntaxFactory.ParseStatement( $"return {node.ExpressionBody.Expression.ToFullString()};" );
				}
				else
				{
					callStatement = SyntaxFactory.ParseStatement( $"{node.ExpressionBody.Expression.ToFullString()};" );
				}

				return declaration.AddBodyStatements( SyntaxFactory.ParseStatement( statement ), callStatement );
			}
		}

		/// <summary>
		/// Accessibility to string
		/// </summary>
		public static string ToDisplayString( this Accessibility accessibility )
		{
			switch ( accessibility )
			{
				case Accessibility.NotApplicable:
					return null;

				case Accessibility.Private:
					return "private";

				case Accessibility.ProtectedAndInternal:
					return "private protected";

				case Accessibility.Protected:
					return "protected";

				case Accessibility.Internal:
					return "internal";

				case Accessibility.ProtectedOrInternal:
					return "protected internal";

				case Accessibility.Public:
					return "public";

				default:
					throw new System.NotSupportedException( accessibility.ToString() );
			}
		}

		/// <summary>
		/// Returns true if this property implements Get or Set - meaning it doesn't have an automatic
		/// backing field
		/// </summary>
		public static bool ImplementsGetOrSet( this PropertyDeclarationSyntax prop )
		{
			var get = prop.AccessorList.Accessors.FirstOrDefault( x => x.Keyword.IsKind( SyntaxKind.GetKeyword ) );
			var set = prop.AccessorList.Accessors.FirstOrDefault( x => x.Keyword.IsKind( SyntaxKind.SetKeyword ) );

			// We need Get and Set to access shit!
			if ( get == null || set == null )
				return false;

			// If they have custom bodies they should have a backing field anyway
			if ( get.Body != null || set.Body != null )
				return false;

			return true;
		}

		public static INamedTypeSymbol GetType( this SemanticModel model, string name )
		{
			var type = model.Compilation.GetTypeByMetadataName( name );

			if ( ReferenceEquals( type, null ) )
				throw new InvalidOperationException( $"Type '{name}' could not be found in the compilation." );

			return type;
		}

		public static ITypeSymbol GetElementType( this ITypeSymbol symbol )
		{
			if ( !(symbol is IArrayTypeSymbol array) )
				throw new InvalidOperationException( $"Cannot get the array element type from type '{symbol.ToDisplayString()}'." );

			return array.ElementType;
		}

		public static bool DerivesFrom( this ITypeSymbol symbol, ITypeSymbol search )
		{
			if ( symbol == null ) return false;
			if ( symbol.MetadataName == search.MetadataName ) return true;
			return symbol.BaseType.DerivesFrom( search );
		}

		public static string PrintableWithPeriod( this INamespaceSymbol containingNamespace )
		{
			if ( containingNamespace.IsGlobalNamespace ) return "global::";
			return $"{containingNamespace}.";
		}

		public static string FullName( this ITypeSymbol type )
		{
			return type.ToDisplayString( SymbolDisplayFormat.FullyQualifiedFormat );
		}

		public static string GetFullMetadataName( this ISymbol s )
		{
			if ( s == null || IsRootNamespace( s ) )
			{
				return string.Empty;
			}

			var sb = new StringBuilder( s.MetadataName );
			var last = s;

			s = s.ContainingSymbol;

			while ( !IsRootNamespace( s ) )
			{
				if ( s is ITypeSymbol && last is ITypeSymbol )
				{
					sb.Insert( 0, '+' );
				}
				else
				{
					sb.Insert( 0, '.' );
				}

				sb.Insert( 0, s.OriginalDefinition.ToDisplayString( SymbolDisplayFormat.MinimallyQualifiedFormat ) );
				s = s.ContainingSymbol;
			}

			return sb.ToString();
		}

		private static bool IsRootNamespace( ISymbol symbol )
		{
			INamespaceSymbol s;
			return ((s = symbol as INamespaceSymbol) != null) && s.IsGlobalNamespace;
		}

		public static bool DerivesFrom( this ITypeSymbol symbol, string name, bool exact = false )
		{
			if ( symbol == null ) return false;
			if ( exact && symbol.FullName() == name ) return true;
			if ( !exact && symbol.FullName().StartsWith( name ) ) return true;

			foreach ( var i in symbol.AllInterfaces )
			{
				if ( exact && i.FullName() == name ) return true;
				if ( !exact && i.FullName().StartsWith( name ) ) return true;
			}

			return symbol.BaseType.DerivesFrom( name, exact );
		}

		public static bool Implements( this ITypeSymbol symbol, string name, bool exact = false )
		{
			if ( symbol == null ) return false;
			return symbol.AllInterfaces.Any( x => exact ? x.FullName() == name : x.FullName().EndsWith( name ) );
		}

		/// <summary>
		/// Returns a value indicating whether <paramref name="type"/> derives from, or implements
		/// any generic construction of, the type defined by <paramref name="parentType"/>.
		/// </summary>
		/// <remarks>
		/// This method only works when <paramref name="parentType"/> is a definition,
		/// not a constructed type.
		/// </remarks>
		/// <example>
		/// <para>
		/// If <paramref name="parentType"/> is the class <see cref="Stack{T}"/>, then this
		/// method will return <see langword="true"/> when called on <c>Stack&gt;int></c>
		/// or any type derived it, because <c>Stack&gt;int></c> is constructed from
		/// <see cref="Stack{T}"/>.
		/// </para>
		/// <para>
		/// Similarly, if <paramref name="parentType"/> is the interface <see cref="IList{T}"/>,
		/// then this method will return <see langword="true"/> for <c>List&gt;int></c>
		/// or any other class that extends <see cref="IList{T}"/> or an class that implements it,
		/// because <c>IList&gt;int></c> is constructed from <see cref="IList{T}"/>.
		/// </para>
		/// </example>
		public static bool DerivesFromOrImplementsAnyConstructionOf( this INamedTypeSymbol type, INamedTypeSymbol parentType )
		{
			if ( !parentType.IsDefinition )
			{
				throw new ArgumentException( $"The type {nameof( parentType )} is not a definition; it is a constructed type", nameof( parentType ) );
			}

			for ( var baseType = type.OriginalDefinition;
				baseType != null;
				baseType = baseType.BaseType?.OriginalDefinition )
			{
				if ( baseType.Equals( parentType, SymbolEqualityComparer.Default ) )
				{
					return true;
				}
			}

			if ( type.OriginalDefinition.AllInterfaces.Any( baseInterface => baseInterface.OriginalDefinition.Equals( parentType, SymbolEqualityComparer.Default ) ) )
			{
				return true;
			}

			return false;
		}

		public static bool IsAutoProperty( this IPropertySymbol propertySymbol )
		{
			var fields = propertySymbol.ContainingType.GetMembers().OfType<IFieldSymbol>();
			return fields.Any( field => SymbolEqualityComparer.Default.Equals( field.AssociatedSymbol, propertySymbol ) );
		}

	}
}
