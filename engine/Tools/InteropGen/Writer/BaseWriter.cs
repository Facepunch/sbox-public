using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Facepunch.InteropGen;

internal class BaseWriter : CodeWriter
{
	public string TargetName;
	protected Definition definitions;


	public BaseWriter( Definition definitions, string targetName )
	{
		TargetName = targetName;
		this.definitions = definitions;
	}

	public virtual void Generate()
	{

	}

	public void WriteToFile( string filename, string text )
	{
		text = text.Replace( "(  )", "()" ); // pet peeve

		if ( System.IO.File.Exists( filename ) )
		{
			if ( System.IO.File.ReadAllText( filename ) == text )
			{
				return;
			}
		}

		System.IO.File.WriteAllText( filename, text );
	}

	public virtual void SaveToFile( string file )
	{
		DirectoryInfo fullname = new( System.IO.Path.Combine( definitions.Root.FullName, file ) );
		string path = System.IO.Path.GetDirectoryName( fullname.FullName );

		CleanSubFiles( fullname.FullName );

		foreach ( KeyValuePair<string, string> sub in SubFiles )
		{
			WriteToFile( System.IO.Path.Combine( path, sub.Key ), sub.Value );
		}

		Log.WriteLine( $"Writing File {file}" );

		WriteToFile( fullname.FullName, sb.ToString() );
	}

	/// <summary>
	/// Delete any children of this filename. Children of interop.hammer.h look like interop.hammer.childname.h.
	/// We clear them out incase they're left over from a previous run - since they're quite messy they might be
	/// and we'll be in the situation of our code #including stuff that only exists on our local computer.
	/// </summary>
	private void CleanSubFiles( string filename )
	{
		string folder = System.IO.Path.GetDirectoryName( filename );
		string fileName = System.IO.Path.GetFileNameWithoutExtension( filename );
		string extension = System.IO.Path.GetExtension( filename );

		foreach ( string file in System.IO.Directory.EnumerateFiles( folder, $"{fileName}.*{extension}" ) )
		{
			// Don't delete active subfiles, that way we can compare and not rewrite them
			if ( SubFiles.ContainsKey( System.IO.Path.GetFileName( file ) ) )
			{
				continue;
			}

			System.IO.File.Delete( file );
		}
	}

	public bool ShouldSkip( Class def, string area = "" )
	{
		//
		// Don't import a native class if we already have it in this assembly
		//
		if ( def.Native && definitions.IsIncluded( def ) )
		{
			return true;
		}

		//
		// Don't import a native class if we already have it in this assembly
		//
		if ( definitions.SkipAll.Any( x => x.Classes.Any( y => y.ManagedNameWithNamespace == def.ManagedNameWithNamespace && y.NativeNameWithNamespace == def.NativeNameWithNamespace ) ) )
		{
			return true;
		}

		//
		// Don't import a native class if we already have it in this assembly
		//
		return area == "managed-definition" && definitions.isRebind( def );
	}

	public bool ShouldStubFunction( Class c, Function f )
	{
		// Stub functions in classes marked with WindowsOnly on non-Windows platforms
		return c.HasAttribute( "WindowsOnly" ) && !System.OperatingSystem.IsWindows();
	}

	public bool ShouldStubFunction( Class c, Variable f )
	{
		// Stub variables in classes marked with WindowsOnly on non-Windows platforms
		return c.HasAttribute( "WindowsOnly" ) && !System.OperatingSystem.IsWindows();
	}

	public bool ShouldSkip( Struct s, string area = "" )
	{
		//
		// Already included in an include
		//
		if ( definitions.IsIncluded( s ) )
		{
			return true;
		}

		//
		// Don't import a native class if we already have it in this assembly
		//
		return definitions.SkipAll.Any( x => x.Structs.Any( y => y.ManagedNameWithNamespace == s.ManagedNameWithNamespace && y.NativeNameWithNamespace == s.NativeNameWithNamespace ) );
	}

	public bool ShouldSkipInclude( string include, string area = "" )
	{
		return definitions.SkipAll.Any( x => x.Includes.Contains( include ) );
	}

	private System.Text.StringBuilder previousSb;
	public Dictionary<string, string> SubFiles = [];

	public void StartSubFile()
	{
		previousSb = sb;
		sb = new System.Text.StringBuilder();
	}

	public string EndSubFile( string moduleName )
	{
		string ext = System.IO.Path.GetExtension( TargetName );
		string fn = System.IO.Path.GetFileNameWithoutExtension( TargetName );
		string outName = $"{fn}.{moduleName}{ext}";

		SubFiles[outName] = sb.ToString();

		sb = previousSb;

		return outName;
	}
}
