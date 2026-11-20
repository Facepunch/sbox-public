using Sandbox.Internal;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Sandbox;

public sealed partial class ScriptSystem
{
	private readonly HashSet<TypeLibrary> _libraries = new();
	private Assembly _assembly;
	// Registration can throw before the hotloader sees the new assembly. Keep its actual outgoing generation.
	private Assembly _watchedAssembly;
	private bool _publishingAssembly;
	[SkipHotload] private static readonly ConditionalWeakTable<Breen.ScriptDocument, CompilationUnitSyntax> DeclarationTrees = new();

	/// <summary>
	/// Generated delegate caches belong to one publication. Require ordinary hotload to reset them,
	/// including when an edit only changes another file in the module.
	/// </summary>
	private static bool CanReuseAssembly( Assembly assembly ) => !assembly.GetTypes()
		.SelectMany( type => type.GetFields( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly ) )
		.Any( field => typeof( Delegate ).IsAssignableFrom( field.FieldType ) && field.IsDefined( typeof( SkipHotloadAttribute ), false ) );

	/// <summary>
	/// Reuses the declaration's syntax for emitted metadata and its assembly-reuse key.
	/// </summary>
	private static CompilationUnitSyntax GetDeclarationTree( Breen.ScriptDeclarationMetadata declaration ) =>
		DeclarationTrees.GetValue( declaration.Document, document =>
			(CompilationUnitSyntax)CSharpSyntaxTree.ParseText( document.Source,
				new CSharpParseOptions( kind: Microsoft.CodeAnalysis.SourceCodeKind.Script ), document.FilePath ).GetRoot() );

	/// <summary>
	/// Includes mapped source coordinates so changing a line directive refreshes published navigation metadata.
	/// </summary>
	private static (string Path, int Line, string Summary) DeclarationAttributesKey( Breen.ScriptDeclarationMetadata declaration )
	{
		var location = GetDeclarationTree( declaration ).SyntaxTree.GetMappedLineSpan( new TextSpan( declaration.Span.Start, declaration.Span.Length ) );
		return (location.Path, location.StartLinePosition.Line + 1, declaration.Summary);
	}

	/// <summary>
	/// Emits source navigation and documentation into the same CLR metadata as authored attributes.
	/// </summary>
	private static IEnumerable<CustomAttributeBuilder> DeclarationAttributes( Breen.ScriptDeclarationMetadata declaration )
	{
		var root = GetDeclarationTree( declaration );
		var span = new TextSpan( declaration.Span.Start, declaration.Span.Length );
		var member = root.FindNode( span ) as MemberDeclarationSyntax;
		var metadata = DeclarationAttributesKey( declaration );
		var attributes = member?.AttributeLists.SelectMany( list => list.Attributes )
			.Select( attribute => attribute.Name.ToString().Replace( "global::", "", StringComparison.Ordinal ) ).ToArray() ?? [];
		if ( !attributes.Any( name => name is "Sandbox.Internal.SourceLocation" or "Sandbox.Internal.SourceLocationAttribute" ) )
		{
			yield return new CustomAttributeBuilder( typeof( SourceLocationAttribute ).GetConstructor( [typeof( string ), typeof( int )] ),
				[metadata.Path, metadata.Line] );
		}

		if ( declaration.Summary is { } summary && !attributes.Any( name => name is "Description" or "DescriptionAttribute"
			or "Sandbox.Description" or "Sandbox.DescriptionAttribute" ) )
		{
			yield return new CustomAttributeBuilder( typeof( DescriptionAttribute ).GetConstructor( [typeof( string )] ), [summary] );
		}
	}

	/// <summary>
	/// Checks names and unsupported code-generation contracts before replacing the current assembly.
	/// </summary>
	private void ValidateTypes( IReadOnlyList<Type> types )
	{
		_libraries.Add( _context.TypeLibrary );
		if ( _context == Engine.GlobalContext.Game && TypeLibrary.Editor is not null )
		{
			_libraries.Add( TypeLibrary.Editor );
		}

		foreach ( var type in types )
		{
			if ( _resolver.IncomingAssemblies.Any( assembly => assembly.GetType( type.FullName ) is not null ) )
			{
				throw new InvalidOperationException( $"A type named '{type.FullName}' is already declared in an incoming C# assembly." );
			}

			foreach ( var library in _libraries )
			{
				var existing = library.GetType( type.FullName, exactFullName: true );
				if ( existing is not null && existing.TargetType.Assembly != _assembly )
				{
					throw new InvalidOperationException( $"A type named '{type.FullName}' is already registered." );
				}
			}

			foreach ( var member in type.GetMembers( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly ).Prepend( type ) )
			{
				foreach ( var attribute in member.GetCustomAttributesData() )
				{
					if ( attribute.AttributeType.IsDefined( typeof( CodeGeneratorAttribute ), true ) )
					{
						if ( (_publishingGeneratedFiles || _namedFiles.Count > 0) && TypeLibraryScriptResolver.IsNetworkingAttribute( attribute.AttributeType ) ) continue;
						throw new NotSupportedException( $"[{attribute.AttributeType.Name}] requires C# code generation and is not supported on script declarations. Use a native component base for this contract." );
					}
				}
			}
		}
	}

	private void UnregisterAssembly()
	{
		if ( _assembly is null ) return;

		foreach ( var library in _libraries )
		{
			library.RemoveAssembly( _assembly );
		}
		if ( _watchedAssembly is not null )
		{
			_context.ReplaceScriptAssembly( _watchedAssembly, null );
			_watchedAssembly = null;
		}
		_assembly = null;
	}

	/// <summary>
	/// Replaces the real CLR assembly through ordinary hotload before executing startup code.
	/// TypeLibrary discovers members and attributes through its existing assembly registration path.
	/// </summary>
	private void ReplaceAssembly( Assembly previous, Assembly incoming )
	{
		// A fresh module can replace an assembly left by an interrupted publication.
		previous ??= _assembly;
		foreach ( var library in _libraries )
		{
			if ( previous is not null )
			{
				library.RemoveAssembly( previous );
			}
		}

		_assembly = incoming;
		_publishingAssembly = true;
		try
		{
			// Register the incoming types before the swap. Migration can run a script type's static
			// initializer, by creating an instance or carrying a static value across, and that
			// initializer must be able to find its own type in the type library.
			foreach ( var library in _libraries )
			{
				library.AddAssembly( incoming, true );
			}

			var migrationError = _context.ReplaceScriptAssembly( _watchedAssembly, incoming );
			_watchedAssembly = incoming;
			// Recovery must replace the queued generation even if migration stopped before walking its references.
			if ( migrationError is not null ) ExceptionDispatchInfo.Throw( migrationError );
		}
		finally
		{
			_publishingAssembly = false;
		}
	}
}
