using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Reflection.Metadata;

namespace Sandbox;

public sealed partial class ScriptSystem
{
	private Dictionary<string, string> _compiledFiles = new( StringComparer.Ordinal );
	private bool _publishingGeneratedFiles;

	/// <summary>
	/// Carries a preparation failure without losing its original source location.
	/// </summary>
	private sealed class FilePreparationException( Script.ScriptError error ) : Exception( error.Message )
	{
		internal Script.ScriptError Error { get; } = error;
	}

	/// <summary>
	/// Generates networking declarations with the addon compiler, retaining script statements and file ownership.
	/// Ordinary scripts keep their existing Breen compilation path.
	/// </summary>
	private unsafe Dictionary<string, string> GenerateFiles( Dictionary<string, string> sources )
	{
		var options = new CSharpParseOptions( LanguageVersion.CSharp14, kind: SourceCodeKind.Script );
		var roots = sources.ToDictionary( pair => pair.Key,
			pair => (CompilationUnitSyntax)CSharpSyntaxTree.ParseText( pair.Value, options, pair.Key ).GetRoot(), StringComparer.Ordinal );
		var generatedPaths = roots.Where( pair => pair.Value.DescendantNodes().OfType<AttributeSyntax>()
			.Any( attribute => TypeLibraryScriptResolver.IsNetworkingAttributeName(
				string.Concat( attribute.Name.DescendantTokens().Select( token => token.ValueText ) ) ) ) )
			.Select( pair => pair.Key ).ToHashSet( StringComparer.Ordinal );
		if ( generatedPaths.Count == 0 ) return sources;

		var generator = new CodeGenerator
		{
			AssemblyName = "Sandbox.Game.Scripts",
			ValidateCompilation = false,
			Defines = [],
			GlobalUsings = ["System", "System.Collections.Generic", "System.Linq", "System.Threading.Tasks", "Sandbox"]
		};
		var metadata = new List<Microsoft.CodeAnalysis.AssemblyMetadata>();
		var assemblies = _context.TypeLibrary.GetTypes().Select( type => type.TargetType.Assembly )
			.Where( assembly => !assembly.IsDynamic && assembly != typeof( object ).Assembly
				&& !FrameworkReferences.All.ContainsKey( assembly.GetName().Name + ".dll" ) ).Distinct().ToArray();
		try
		{
			foreach ( var reference in FrameworkReferences.All.Values ) generator.AddReference( reference );
			foreach ( var assembly in assemblies )
			{
				if ( !assembly.TryGetRawMetadata( out var pointer, out var length ) ) continue;

				ModuleMetadata module = null;
				try
				{
					module = ModuleMetadata.CreateFromMetadata( (IntPtr)pointer, length );
					var item = Microsoft.CodeAnalysis.AssemblyMetadata.Create( module );
					metadata.Add( item );
					module = null; // AssemblyMetadata owns and disposes the module.
					generator.AddReference( item.GetReference() );
				}
				finally
				{
					module?.Dispose();
				}
			}

			var inputPaths = generatedPaths.ToHashSet( StringComparer.OrdinalIgnoreCase );
			var companionIndex = 0;
			foreach ( var (path, root) in roots.OrderBy( pair => pair.Key, StringComparer.Ordinal ) )
			{
				if ( !root.Members.Any( IsTypeDeclaration ) ) continue;
				var inputPath = path;
				var declarations = MaskMembers( root, member => !IsTypeDeclaration( member ) );
				if ( !generatedPaths.Contains( path ) )
				{
					// Companions supply type information only; their named-file identities need not be C# paths.
					do
					{
						inputPath = $"__script_companion_{companionIndex++}.csx";
					} while ( !inputPaths.Add( inputPath ) );

					declarations = $"#line 1 {SymbolDisplay.FormatLiteral( path, true )}\n" + declarations;
				}

				generator.AddFile( inputPath, declarations );
			}
			var generated = generator.GenerateAsync().GetAwaiter().GetResult();
			if ( !generated.Success )
			{
				var errors = generated.Diagnostics.Where( error => error.Severity == DiagnosticSeverity.Error ).ToArray();
				var diagnostic = errors.FirstOrDefault( error => error.Location.IsInSource ) ?? errors[0];
				var location = diagnostic.Location.GetMappedLineSpan();
				var path = location.Path ?? "";
				var start = location.StartLinePosition;
				var offset = 0;
				if ( roots.TryGetValue( path, out var root ) && start.Line < root.SyntaxTree.GetText().Lines.Count )
				{
					offset = root.SyntaxTree.GetText().Lines.GetPosition( start );
				}

				throw new FilePreparationException( new Script.ScriptError( "SCR_CODEGEN", string.Join( "\n", errors.Select( error => error.ToString() ) ),
					offset, diagnostic.Location.SourceSpan.Length, path, location.IsValid ? start.Line + 1 : 0, location.IsValid ? start.Character + 1 : 0 ) );
			}
			if ( generatedPaths.Except( generated.Files.Select( file => file.SourcePath ), StringComparer.Ordinal ).Any() )
			{
				throw new Breen.ScriptException( "SCR_CODEGEN", "Generated scripts require distinct, canonical project-relative source paths." );
			}

			var output = new Dictionary<string, string>( sources, StringComparer.Ordinal );
			foreach ( var file in generated.Files )
			{
				if ( file.SourcePath is null )
				{
					output.Add( file.Path, file.Text );
				}
				else if ( generatedPaths.Contains( file.SourcePath ) )
				{
					// Append only startup and global functions, not a second copy of the source's padding.
					// Imports and preprocessor definitions already occur in the generated declarations.
					var text = new System.Text.StringBuilder( file.Text );
					foreach ( var member in roots[file.SourcePath].Members.Where( member => !IsTypeDeclaration( member ) ) )
					{
						var location = member.GetLocation().GetMappedLineSpan();
						text.Append( $"\n#line {location.StartLinePosition.Line + 1} {SymbolDisplay.FormatLiteral( location.Path, true )}\n" );
						text.Append( member.ToString() );
					}
					output[file.SourcePath] = text.ToString();
				}
			}
			return output;
		}
		finally
		{
			foreach ( var item in metadata ) item.Dispose();
			GC.KeepAlive( assemblies );
		}
	}

	private static bool IsTypeDeclaration( MemberDeclarationSyntax member ) =>
		member is BaseTypeDeclarationSyntax or BaseNamespaceDeclarationSyntax or DelegateDeclarationSyntax;

	private static string MaskMembers( CompilationUnitSyntax root, Func<MemberDeclarationSyntax, bool> remove ) =>
		root.SyntaxTree.GetText().WithChanges( root.Members.Where( remove )
			.Select( member => new TextChange( member.Span, Blank( member.ToString() ) ) ) ).ToString();

	private static string Blank( string text ) => new( text.Select( character => character is '\r' or '\n' ? character : ' ' ).ToArray() );
}
