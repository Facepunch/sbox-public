using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Facepunch;

class Program
{
	[DllImport( "user32.dll", CharSet = CharSet.Auto )]
	private static extern int MessageBox( IntPtr hWnd, string text, string caption, uint type );


	static void Main( string[] args )
	{
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

		files = Array.FindAll(files, (string path) =>
		{
			return !(path.Contains("/obj/.generated") || path.Contains( "/obj/"));
		} );
		Console.WriteLine( $"[{sw.Elapsed.TotalSeconds:0.00}] Got Files" );

		List<SyntaxTree> SyntaxTree = new List<SyntaxTree>();
		var targetPaths = new HashSet<string>();

		//
		// Convert each file into a syntaxtree. Unless we have the destination file, and it's newer.
		//
		//properly skip bs
		files = Array.FindAll(files.ToArray(),( string path ) =>
		{
			var relPath = Path.GetRelativePath($"{Environment.CurrentDirectory}",path);
			var absDstPath = $"{Environment.CurrentDirectory}/obj/.generated/{relPath}";
			var dstWriteTime = File.GetLastWriteTimeUtc(absDstPath);
			var srcWriteTime = File.GetLastWriteTimeUtc(path);
			bool finalCheck = srcWriteTime > dstWriteTime;
			if ( !finalCheck )
			{
				targetPaths.Add(absDstPath);
			}
			return finalCheck;
		} );

		
		Parallel.ForEach( files, file =>
		{

			AddTree( SyntaxTree, file, file );
		} );

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

		CSharpCompilation compiler = CSharpCompilation.Create( $"CodeGen", SyntaxTree, refs, optn );

		//
		// Process each file
		//
		{
			Console.WriteLine( $"[{sw.Elapsed.TotalSeconds:0.00}] Processsing {SyntaxTree.Count:n0} files ({targetPaths.Count:n0} skipped)" );
			var processor = new Sandbox.Generator.Processor();
			processor.ILHotloadSupported = false;
			processor.AddonName = "codegen";
			processor.PackageAssetResolver = ( p ) => $"/{p}/model_mock.mdl";
			processor.AddonFileMap = files.ToDictionary( x => x, x => GetRelativePath( x, Environment.CurrentDirectory ) );
			processor.Run( compiler );
			Console.WriteLine( $"[{sw.Elapsed.TotalSeconds:0.00}] Processed {SyntaxTree.Count:n0} files" );

			compiler = processor.Compilation;
		}

		Parallel.ForEach( compiler.SyntaxTrees, tree =>
		{
			var writtenPath = WriteFileToOutputFolder( tree.FilePath, tree );
			lock ( targetPaths )
			{
				targetPaths.Add( writtenPath );
			}
		} );

		Console.WriteLine( $"[{sw.Elapsed.TotalSeconds:0.00}] Wrote files" );

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

	static string GetTargetPath( string file )
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

		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( targetPath ) );
		WriteText( targetPath, sourceContent );

		return targetPath;
	}
}
