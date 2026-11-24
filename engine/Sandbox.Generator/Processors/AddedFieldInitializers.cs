using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sandbox.Generator;

/// <summary>
/// <para>
/// When a hotload adds an instance field, existing instances need the value of its initializer. For each field
/// added since <see cref="Processor.LastSuccessfulCompilation"/>, we generate a static method in the same type that
/// returns that value, marked with <c>[AddedFieldInitializer]</c> so hotload can find it.
/// </para>
/// <para>
/// The method goes after the field's declaration, with <c>#line</c> directives so the copied initializer maps to
/// its original lines and the rest of the file keeps its line numbers. Warnings are disabled around it, since the
/// original initializer already reports them, and the copy can report different ones. The methods are only for
/// the compile that added their fields: the compiler keeps the trees from before we added them for the next compile
/// (see <see cref="Processor.HotloadOriginalTrees"/>).
/// </para>
/// </summary>
partial class HotloadProcessor
{
	private const string AddedFieldInitializerPrefix = "__AddedFieldInitializer_";
	private const string AddedFieldInitializerAttribute = "global::Sandbox.AddedFieldInitializer";

	private static readonly SymbolDisplayFormat FieldTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
		.AddMiscellaneousOptions( SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier );

	private static readonly DiagnosticDescriptor InheritedFieldsNotInitialized = new(
		DiagnosticIds.HotloadInheritedFieldsNotInitialized,
		"Inherited fields not initialized by hotload",
		"'{0}' now inherits from '{1}', which is in another assembly. Hotload can't run its field initializers, so existing instances of '{0}' will have default values for its fields.",
		"Hotload", DiagnosticSeverity.Warning, true );

	/// <summary>
	/// Generates a method for each instance field added since <see cref="Processor.LastSuccessfulCompilation"/>
	/// that has an initializer. That includes fields whose type changed, and fields of base types newly in a type's
	/// hierarchy, since hotload treats those as new too. Base types newly in the hierarchy from another assembly
	/// get a warning instead.
	/// </summary>
	/// <remarks>
	/// <para>
	/// TODO: This assumes existing instances were created from the last successful compile, so fields are only
	/// new to them for one compile. That isn't always true, and fields added in an earlier compile then have no
	/// initializer method and keep their default value:
	/// </para>
	/// <list type="bullet">
	/// <item>A compile that succeeds is never hotloaded, such as when another assembly in its group fails.
	/// Its added fields are compared against it in the next compile instead.</item>
	/// <item>A script assembly that failed to rebuild is retained (see <c>Hotload.RetainedAssembly</c>) while
	/// its package base types keep hotloading. When it finally rebuilds, its instances skip from the original
	/// base types to the latest ones, so fields added to them in any compile but the latest are missed.</item>
	/// </list>
	/// </remarks>
	private static void AddFieldInitializers( Processor processor )
	{
		var compilation = processor.Compilation;
		var oldCompilation = processor.LastSuccessfulCompilation;
		var oldTrees = GetSyntaxTreeDict( oldCompilation.SyntaxTrees );

		var visited = new HashSet<INamedTypeSymbol>( SymbolEqualityComparer.Default );
		var addedFields = new HashSet<IFieldSymbol>( SymbolEqualityComparer.Default );

		foreach ( var tree in compilation.SyntaxTrees )
		{
			if ( tree.FilePath.Equals( Processor.CompilerExtraPath, StringComparison.OrdinalIgnoreCase ) ) continue;

			// Fields can only be added by a file that changed, and unchanged files keep the same tree

			if ( oldTrees.TryGetValue( tree.FilePath, out var oldTree ) )
			{
				if ( oldTree == tree ) continue;
				if ( oldTree.GetText().ContentEquals( tree.GetText() ) ) continue;
			}

			var model = compilation.GetSemanticModel( tree );

			foreach ( var typeDecl in tree.GetRoot().DescendantNodes( IsTypeContainer ).OfType<TypeDeclarationSyntax>() )
			{
				if ( model.GetDeclaredSymbol( typeDecl ) is not { } type || !visited.Add( type ) ) continue;

				FindAddedFields( processor, type, addedFields );
			}
		}

		var insertions = new Dictionary<SyntaxTree, SortedDictionary<int, Insertion>>();

		foreach ( var field in addedFields )
		{
			AddFieldInitializer( compilation, field, insertions );
		}

		foreach ( var pair in insertions )
		{
			var tree = pair.Key;
			var changes = pair.Value.Select( x => new TextChange( new TextSpan( x.Key, 0 ), x.Value.ToString() ) );
			var newTree = tree.WithChangedText( tree.GetText().WithChanges( changes ) );

			processor.ReplaceSyntaxTree( tree, newTree );
			processor.HotloadOriginalTrees[newTree] = tree;
		}
	}

	private static bool IsTypeContainer( SyntaxNode node )
	{
		return node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax;
	}

	/// <summary>
	/// Finds fields that hotload will treat as new when upgrading existing instances of <paramref name="type"/>,
	/// matching how <c>DefaultUpgrader</c> compares each level of the type hierarchy.
	/// </summary>
	private static void FindAddedFields( Processor processor, INamedTypeSymbol type, HashSet<IFieldSymbol> addedFields )
	{
		var compilation = processor.Compilation;
		var oldCompilation = processor.LastSuccessfulCompilation;

		// New types have no existing instances to initialize

		if ( oldCompilation.Assembly.GetTypeByMetadataName( GetFullMetadataName( type ) ) is not { } oldType ) return;

		// Base types as constructed in the old hierarchy, like B<int>, so their fields' types have the old type arguments

		var oldHierarchy = new Dictionary<string, INamedTypeSymbol>();

		for ( var oldBase = oldType; oldBase is not null; oldBase = oldBase.BaseType )
		{
			oldHierarchy[GetFullMetadataName( oldBase )] = oldBase;
		}

		// Fields added to base types from other assemblies are found by those assemblies' own compiles

		var baseType = type;

		for ( ; baseType is not null && SymbolEqualityComparer.Default.Equals( baseType.ContainingAssembly, compilation.Assembly ); baseType = baseType.BaseType )
		{
			// A base type that wasn't in the old hierarchy has no old fields to copy from

			var oldFields = oldHierarchy.TryGetValue( GetFullMetadataName( baseType ), out var oldBase )
				? GetInstanceFields( oldBase ).ToDictionary( x => x.Name, x => x.Type.ToDisplayString( FieldTypeFormat ) )
				: new Dictionary<string, string>();

			foreach ( var field in GetInstanceFields( baseType ) )
			{
				// A field that changed type is treated as new too, including a field of type T in a generic base type
				// whose type argument changed

				if ( oldFields.TryGetValue( field.Name, out var oldFieldType ) && oldFieldType == field.Type.ToDisplayString( FieldTypeFormat ) ) continue;

				// Generate into the generic definition, where one method serves every construction of it

				addedFields.Add( field.OriginalDefinition );
			}
		}

		// A base type from another assembly that wasn't in the old hierarchy has fields that hotload treats as new,
		// but their initializer methods would have to go in that type, and it might not even be our source

		if ( baseType is not null && !oldHierarchy.ContainsKey( GetFullMetadataName( baseType ) ) )
		{
			var location = type.DeclaringSyntaxReferences
				.Select( x => x.GetSyntax() )
				.OfType<TypeDeclarationSyntax>()
				.Select( x => x.BaseList?.Types.FirstOrDefault()?.GetLocation() )
				.FirstOrDefault( x => x is not null ) ?? type.Locations.FirstOrDefault();

			processor.Diagnostics.Add( Diagnostic.Create( InheritedFieldsNotInitialized, location, type.Name, baseType.ToDisplayString() ) );
		}
	}

	/// <summary>
	/// Instance fields with storage, including the backing fields of auto-properties and field-like events.
	/// </summary>
	private static IEnumerable<IFieldSymbol> GetInstanceFields( INamedTypeSymbol type )
	{
		return type.GetMembers()
			.OfType<IFieldSymbol>()
			.Where( x => !x.IsStatic && !x.IsConst );
	}

	private static string GetFullMetadataName( INamedTypeSymbol type )
	{
		if ( type.ContainingType is { } containingType )
		{
			return $"{GetFullMetadataName( containingType )}+{type.MetadataName}";
		}

		return type.ContainingNamespace is { IsGlobalNamespace: false } ns
			? $"{ns.ToDisplayString()}.{type.MetadataName}"
			: type.MetadataName;
	}

	private static void AddFieldInitializer( Compilation compilation, IFieldSymbol field,
		Dictionary<SyntaxTree, SortedDictionary<int, Insertion>> insertions )
	{
		// Fields we couldn't return from a method, or assign with reflection

		if ( field.Type is IPointerTypeSymbol or IFunctionPointerTypeSymbol ) return;
		if ( field.IsFixedSizeBuffer || field.RefKind != RefKind.None ) return;

		if ( !TryGetInitializer( field, out var value, out var insertAfter, out var modifiers, out var type ) ) return;

		var supported = !UsesConstructorParameter( compilation, value )
			&& !value.DescendantTrivia().Any( x => x.IsDirective || x.IsKind( SyntaxKind.DisabledTextTrivia ) );

		var tree = type.SyntaxTree;
		var methodName = AddedFieldInitializerPrefix + GetSafeIdentifier( field.Name );
		var text = new StringBuilder();

		// Insert at the start of the next line, unless the type ends on this one. A record without a body, like
		// record R( int A );, gets one before its semicolon.

		var position = insertAfter;
		var lines = tree.GetText().Lines;
		var line = lines.GetLineFromPosition( position );
		var addBody = type.OpenBraceToken.IsKind( SyntaxKind.None );

		if ( addBody )
		{
			position = type.SemicolonToken.SpanStart;
			text.AppendLine();
		}
		else if ( line.LineNumber + 1 < lines.Count && lines[line.LineNumber + 1].Start <= type.CloseBraceToken.SpanStart )
		{
			position = lines[line.LineNumber + 1].Start;
		}
		else
		{
			position = type.CloseBraceToken.SpanStart;
			text.AppendLine();
		}

		text.AppendLine( "#pragma warning disable" );
		text.AppendLine( "#line hidden" );

		if ( supported )
		{
			var fieldType = field.Type.ToDisplayString( FieldTypeFormat );
			var methodModifiers = modifiers.Any( SyntaxKind.UnsafeKeyword ) ? "private static unsafe" : "private static";

			text.Append( $"[{AddedFieldInitializerAttribute}( \"{field.Name}\" )] {methodModifiers} {fieldType} {methodName}() =>" );

			// Array initializers like { 1, 2 } are only allowed in declarations. An array creation can't be
			// annotated as nullable, so leave off the field type's top-level "?".

			if ( value is InitializerExpressionSyntax )
			{
				var arrayType = field.Type.WithNullableAnnotation( NullableAnnotation.NotAnnotated ).ToDisplayString( FieldTypeFormat );
				text.Append( $" new {arrayType}" );
			}

			text.AppendLine();
			text.AppendLine( GetLineDirective( tree, value.SpanStart ) );
			text.Append( value ).AppendLine( ";" );
		}
		else
		{
			text.AppendLine( $"[{AddedFieldInitializerAttribute}( \"{field.Name}\", Supported = false )] private static void {methodName}() {{ }}" );
		}

		text.AppendLine( "#pragma warning restore" );
		text.Append( GetWarningState( tree, position ) );
		text.AppendLine( GetLineDirective( tree, position ) );

		if ( !insertions.TryGetValue( tree, out var treeInsertions ) )
		{
			insertions[tree] = treeInsertions = new SortedDictionary<int, Insertion>();
		}

		if ( !treeInsertions.TryGetValue( position, out var insertion ) )
		{
			treeInsertions[position] = insertion = new Insertion( addBody );
		}

		insertion.Text.Append( text );
	}

	/// <summary>
	/// Generated methods to insert at one position, wrapped in braces if they're the body of a record without one.
	/// </summary>
	private sealed class Insertion( bool addBody )
	{
		public StringBuilder Text { get; } = new();

		public override string ToString() => addBody ? $"{{{Text}}}" : Text.ToString();
	}

	/// <summary>
	/// Finds the declaration with <paramref name="field"/>'s initializer, which for a backing field is its property
	/// or event, or for a positional record property, its primary constructor parameter's default value.
	/// Returns false if there's no initializer.
	/// </summary>
	/// <param name="insertAfter">Position to generate the method after.</param>
	/// <param name="modifiers">Modifiers of the declaration, which might make it unsafe.</param>
	private static bool TryGetInitializer( IFieldSymbol field, out ExpressionSyntax value, out int insertAfter,
		out SyntaxTokenList modifiers, out TypeDeclarationSyntax type )
	{
		var declaringSymbol = field.IsImplicitlyDeclared && field.AssociatedSymbol is { } associated ? associated : field;

		// Partial properties have several declarations, but only one has the initializer

		foreach ( var reference in declaringSymbol.DeclaringSyntaxReferences )
		{
			switch ( reference.GetSyntax() )
			{
				case VariableDeclaratorSyntax { Initializer: { } initializer, Parent.Parent: BaseFieldDeclarationSyntax { Parent: TypeDeclarationSyntax fieldType } fieldDecl }:
					value = initializer.Value;
					insertAfter = fieldDecl.Span.End;
					modifiers = fieldDecl.Modifiers;
					type = fieldType;
					return true;

				case PropertyDeclarationSyntax { Initializer: { } initializer, Parent: TypeDeclarationSyntax propertyType } propertyDecl:
					value = initializer.Value;
					insertAfter = propertyDecl.Span.End;
					modifiers = propertyDecl.Modifiers;
					type = propertyType;
					return true;

				case ParameterSyntax { Default: { } defaultValue, Parent.Parent: TypeDeclarationSyntax recordType }
					when declaringSymbol is IPropertySymbol:

					// A positional record property. Without a default value the parameter is always passed in, like a
					// field assigned in a constructor, so there's no initializer.

					value = defaultValue.Value;
					insertAfter = recordType.OpenBraceToken.Span.End;
					modifiers = default;
					type = recordType;
					return true;
			}
		}

		value = null;
		insertAfter = 0;
		modifiers = default;
		type = null;
		return false;
	}

	/// <summary>
	/// Does <paramref name="value"/> reference a primary constructor parameter? If so it can't be evaluated
	/// outside of a constructor.
	/// </summary>
	private static bool UsesConstructorParameter( Compilation compilation, ExpressionSyntax value )
	{
		var model = compilation.GetSemanticModel( value.SyntaxTree );

		return value.DescendantNodesAndSelf()
			.OfType<IdentifierNameSyntax>()
			.Any( x => model.GetSymbolInfo( x ).Symbol is IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor } } );
	}

	/// <summary>
	/// A <c>#line</c> directive mapping the next line to wherever <paramref name="position"/> maps to, which may
	/// be another file if the tree was generated, like for Razor.
	/// </summary>
	private static string GetLineDirective( SyntaxTree tree, int position )
	{
		if ( tree.GetLineVisibility( position ) == LineVisibility.Hidden )
		{
			return "#line hidden";
		}

		var mapped = tree.GetMappedLineSpan( new TextSpan( position, 0 ) );
		var path = SyntaxFactory.Literal( mapped.Path ).Text;

		return $"#line {mapped.StartLinePosition.Line + 1} {path}";
	}

	/// <summary>
	/// <c>#pragma warning</c> directives that restore the warning state at <paramref name="position"/>, after
	/// a <c>#pragma warning restore</c> has reset everything to the project's settings.
	/// </summary>
	private static string GetWarningState( SyntaxTree tree, int position )
	{
		var allDisabled = false;
		var disabled = new List<string>();
		var restored = new List<string>();

		for ( var directive = tree.GetRoot().GetFirstDirective(); directive is not null && directive.SpanStart < position; directive = directive.GetNextDirective() )
		{
			if ( directive is not PragmaWarningDirectiveTriviaSyntax { IsActive: true } pragma ) continue;

			var disable = pragma.DisableOrRestoreKeyword.IsKind( SyntaxKind.DisableKeyword );

			if ( pragma.ErrorCodes.Count == 0 )
			{
				allDisabled = disable;
				disabled.Clear();
				restored.Clear();
				continue;
			}

			// After disabling everything, track exceptions that were restored. Otherwise track what was disabled.

			var exceptions = allDisabled ? restored : disabled;
			var addException = allDisabled ? !disable : disable;

			foreach ( var code in pragma.ErrorCodes.Select( GetWarningCode ) )
			{
				exceptions.Remove( code );

				if ( addException ) exceptions.Add( code );
			}
		}

		var builder = new StringBuilder();

		if ( allDisabled )
		{
			builder.AppendLine( "#pragma warning disable" );

			if ( restored.Count > 0 ) builder.AppendLine( $"#pragma warning restore {string.Join( ", ", restored )}" );
		}
		else if ( disabled.Count > 0 )
		{
			builder.AppendLine( $"#pragma warning disable {string.Join( ", ", disabled )}" );
		}

		return builder.ToString();
	}

	/// <summary>
	/// Warning codes can be written as numbers, which mean the compiler's own <c>CS</c> warnings.
	/// </summary>
	private static string GetWarningCode( ExpressionSyntax code )
	{
		return code is LiteralExpressionSyntax { Token.Value: int number } ? $"CS{number:D4}" : code.ToString();
	}

	private static string GetSafeIdentifier( string name )
	{
		var builder = new StringBuilder( name.Length );

		foreach ( var c in name )
		{
			builder.Append( char.IsLetterOrDigit( c ) || c == '_' ? c : '_' );
		}

		return builder.ToString();
	}
}
