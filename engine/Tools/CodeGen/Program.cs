using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Facepunch;

class Program
{
	[DllImport( "user32.dll", CharSet = CharSet.Auto )]
	private static extern int MessageBox( IntPtr hWnd, string text, string caption, uint type );


	static void Main( string[] args )
	{
		if ( args is ["--keep-ref", var compiled, var current] )
		{
			RefAssembly.KeepIfOnlyLinesMoved( compiled, current );
			return;
		}

		//try
		//{
		Run( args );
		//}
		//catch ( System.Exception e )
		//{
		//	MessageBox( default, e.Message, e.StackTrace, 0 );
		//}
	}

	static void Run( string[] args )
	{
		var sw = Stopwatch.StartNew();

		System.IO.Directory.CreateDirectory( "obj/.generated" );

		// Get a list of files that already exist in the directory
		var looseFiles = System.IO.Directory.EnumerateFiles( $"{Environment.CurrentDirectory}/obj/.generated/", "*.cs", System.IO.SearchOption.AllDirectories ).ToList();

		// Get a list of files we need to copy over
		var files = System.IO.Directory.EnumerateFiles( Environment.CurrentDirectory, "*.cs", System.IO.SearchOption.AllDirectories ).Select( x => x.Replace( "\\", "/" ) ).ToArray();

		Console.WriteLine( $"[{sw.Elapsed.TotalSeconds:0.00}] Got Files" );

		List<SyntaxTree> SyntaxTree = new List<SyntaxTree>();
		var targetPaths = new HashSet<string>();

		//
		// Convert each file into a syntaxtree. Unless we have the destination file, and it's newer.
		//
		Parallel.ForEach( files, file =>
		{
			// skip bs
			if ( file.Contains( "/obj/.generated/" ) ) return;
			if ( file.Contains( "/obj/" ) ) return;

			AddTree( SyntaxTree, file, file );
		} );

		// The parse finishes in any order, and tree order decides things like which part of a
		// partial class gets its [Description]. Keep it stable so unchanged sources give unchanged output.
		SyntaxTree.Sort( ( a, b ) => StringComparer.OrdinalIgnoreCase.Compare( a.FilePath, b.FilePath ) );

		Console.WriteLine( $"[{sw.Elapsed.TotalSeconds:0.00}] Built Syntax Trees" );

		var optn = new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary )
								.WithConcurrentBuild( true )
								.WithOptimizationLevel( OptimizationLevel.Debug )
								.WithGeneralDiagnosticOption( ReportDiagnostic.Info )
								.WithPlatform( Microsoft.CodeAnalysis.Platform.AnyCpu )
								.WithAllowUnsafe( false );

		var refs = new List<MetadataReference>();

		var path = System.IO.Path.GetDirectoryName( typeof( System.Object ).Assembly.Location );
		refs.Add( MetadataReference.CreateFromFile( typeof( System.Object ).Assembly.Location ) );
		refs.Add( MetadataReference.CreateFromFile( Path.Combine( path, "System.Runtime.dll" ) ) );
		refs.Add( MetadataReference.CreateFromFile( typeof( Sandbox.CodeGeneratorAttribute ).Assembly.Location ) );

		// Projects outside the engine pass their references, so the [Sync] and [Rpc] attributes they use resolve
		var references = args.SelectMany( a => a.StartsWith( '@' ) ? File.ReadAllLines( a[1..] ) : [a] ).Select( a => a.Trim( '"' ) );
		var added = new HashSet<string>( refs.Select( r => Path.GetFileName( r.Display ) ), StringComparer.OrdinalIgnoreCase );
		foreach ( var reference in references.Where( File.Exists ) )
		{
			if ( added.Add( Path.GetFileName( reference ) ) )
				refs.Add( MetadataReference.CreateFromFile( reference ) );
		}

		CSharpCompilation compiler = CSharpCompilation.Create( $"CodeGen", SyntaxTree, refs, optn );

		//
		// When only method bodies changed, nothing another file's generated code depends on has moved,
		// so only the changed files need generating again.
		//
		var previous = State.Load();
		var state = State.Create( SyntaxTree, previous );
		var changedTrees = state.ChangedSince( previous, SyntaxTree );

		var generated = Generate( compiler, changedTrees ?? SyntaxTree, files, sw );

		// Files the generator added rather than rewrote are made from every file at once, which a
		// partial run can't do. Redo it in full, and don't save the state so it stays that way.
		var addedTrees = generated.SyntaxTrees.Count() != SyntaxTree.Count;
		if ( addedTrees && changedTrees is not null )
		{
			changedTrees = null;
			generated = Generate( compiler, SyntaxTree, files, sw );
		}

		var processedPaths = (changedTrees ?? generated.SyntaxTrees.ToList()).Select( x => x.FilePath ).ToHashSet();

		Parallel.ForEach( generated.SyntaxTrees, tree =>
		{
			var writtenPath = processedPaths.Contains( tree.FilePath ) ? WriteFileToOutputFolder( tree.FilePath, tree ) : GetTargetPath( tree.FilePath );
			lock ( targetPaths )
			{
				targetPaths.Add( writtenPath );
			}
		} );

		Console.WriteLine( $"[{sw.Elapsed.TotalSeconds:0.00}] Wrote files" );

		if ( addedTrees ) State.Delete();
		else state.Save();

		foreach ( var file in looseFiles )
		{
			if ( targetPaths.Contains( file ) )
			{
				continue;
			}

			System.IO.File.Delete( file );
			Console.WriteLine( $"Deleting unused file - {file}" );
		}

		Console.WriteLine( $"[{sw.Elapsed.TotalSeconds:0.00}] Done" );
	}

	static CSharpCompilation Generate( CSharpCompilation compiler, List<SyntaxTree> trees, string[] files, Stopwatch sw )
	{
		var total = compiler.SyntaxTrees.Count();
		Console.WriteLine( $"[{sw.Elapsed.TotalSeconds:0.00}] Processsing {trees.Count:n0} files ({total - trees.Count:n0} skipped)" );

		var processor = new Sandbox.Generator.Processor();
		processor.ILHotloadSupported = false;
		processor.IgnoreDiagnostics = true;
		processor.AddonName = "codegen";
		processor.PackageAssetResolver = ( p ) => $"/{p}/model_mock.mdl";
		processor.AddonFileMap = files.ToDictionary( x => x, x => GetRelativePath( x, Environment.CurrentDirectory ) );
		processor.Run( compiler, trees.ToList() );
		Console.WriteLine( $"[{sw.Elapsed.TotalSeconds:0.00}] Processed {trees.Count:n0} files" );

		return processor.Compilation;
	}

	static string GetRelativePath( string filespec, string folder )
	{
		Uri pathUri = new Uri( filespec );
		// Folders must end in a slash
		if ( !folder.EndsWith( Path.DirectorySeparatorChar.ToString() ) )
		{
			folder += Path.DirectorySeparatorChar;
		}
		Uri folderUri = new Uri( folder );
		return Uri.UnescapeDataString( folderUri.MakeRelativeUri( pathUri ).ToString().Replace( '/', Path.DirectorySeparatorChar ) );
	}

	static string ReadText( string path )
	{
		for ( int i = 0; i < 20; i++ )
		{
			try
			{
				return System.IO.File.ReadAllText( path );
			}
			catch
			{
				Thread.Sleep( 100 );
				// HUH
			}
		}

		throw new System.Exception( $"Couldn't read {path}" );
	}

	static void WriteText( string path, string value )
	{
		Exception _e = default;

		for ( int i = 0; i < 20; i++ )
		{
			try
			{
				System.IO.File.WriteAllText( path, value );
				return;
			}
			catch ( Exception e )
			{
				Thread.Sleep( 100 );
				_e = e;
				// HUH
			}
		}

		throw new System.Exception( $"Couldn't write {path} ({_e?.Message})" );
	}

	internal static string GetTargetPath( string file )
	{
		var relativePath = GetRelativePath( file, Environment.CurrentDirectory );
		return $"{Environment.CurrentDirectory}/obj/.generated/{relativePath}";
	}

	private static void AddTree( List<SyntaxTree> syntaxTree, string path, string relativePath )
	{
		var targetPath = GetTargetPath( path );
		var code = ReadText( path );

		var parseOptions = CSharpParseOptions.Default.WithLanguageVersion( LanguageVersion.CSharp11 );
		var tree = CSharpSyntaxTree.ParseText( text: code, options: parseOptions, path: relativePath, encoding: System.Text.Encoding.UTF8 );

		lock ( syntaxTree )
		{
			syntaxTree.Add( tree );
		}
	}

	private static string WriteFileToOutputFolder( string file, SyntaxTree tree )
	{
		var targetPath = GetTargetPath( file );
		var sourceContent = tree.ToString();
		var originalContent = ReadText( file );
		var crc = Crc64.FromString( originalContent );

		sourceContent = $"// <auto-generated>{crc}</auto-generated>\n{sourceContent}";

		// Leave unchanged files alone, so their timestamps don't make msbuild recompile the project
		if ( File.Exists( targetPath ) && ReadText( targetPath ) == sourceContent )
			return targetPath;

		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( targetPath ) );
		WriteText( targetPath, sourceContent );

		return targetPath;
	}
}
