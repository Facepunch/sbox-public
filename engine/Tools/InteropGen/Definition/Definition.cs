using Facepunch.InteropGen.Parsers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Facepunch.InteropGen;

public class Definition
{
	[ThreadStatic]
	public static Definition Current;

	/// <summary>
	/// The root folder 
	/// </summary>
	public System.IO.DirectoryInfo Root { get; private set; }
	public string Ident { get; internal set; } = "!!!NO IDENT!!!";
	public string NativeDll { get; internal set; } = "!!!NO NativeDll!!!";
	public string InitFrom { get; internal set; } = "Native";
	public string StringTools { get; internal set; } = "Sandbox.Interop";
	public string Filename { get; private set; }
	public string ExceptionHandlerName { get; internal set; }
	public string SaveFileCpp { get; internal set; }
	public string SaveFileCppH { get; internal set; }
	public string SaveFileCs { get; internal set; }
	public string SaveFileCsAot { get; internal set; }
	public string ManagedNamespace { get; internal set; } = "ManagedNamespace";
	public string PrecompiledHeader { get; set; }
	public string FullText { get; set; }
	public int Hash { get; private set; }
	public string CustomNamespace => SaveFileCs.Replace( ".cs", "Internal" ).Replace( ".", "" ).Replace( "/", "" );

	public List<Class> Classes = [];
	public List<Struct> Structs = [];
	public List<string> Includes = [];
	public List<string> CppIncludes = [];
	public List<string> Externs = [];
	public List<string> FunctionPointers = [];
	public List<string> Delegates = [];
	public List<Definition> IncludedDefinitions = [];
	public List<Definition> SkipDefines = [];
	public List<Definition> SkipAll = [];

	public static Definition FromFile( string filename )
	{
		string folder = System.IO.Path.GetDirectoryName( filename );
		if ( string.IsNullOrEmpty( folder ) )
		{
			folder = ".";
		}

		// Log.WriteLine( $"Reading {filename}" );

		string text = System.IO.File.ReadAllText( filename );

		Definition d = new()
		{
			Filename = System.IO.Path.GetFileName( filename ),
			Root = new System.IO.DirectoryInfo( folder )
		};

		Current = d;

		// Log.WriteLine( $"Parsing {filename}" );
		d.ParseFrom( text );

		// Log.WriteLine( $"Completing definition" );
		d.CompleteDefinition();

		// Log.WriteLine( $"Sorting" );
		d.Sort();

		// Log.WriteLine( $"Mangling" );
		Mangler mangler = new();
		mangler.Mangle( d.Classes );

		d.GenerateHash();

		return d;
	}

	private void ParseFrom( string text )
	{
		FullText = "";

		GlobalParser parser = new();
		parser.Parse( this, text, System.IO.Path.Combine( Root.FullName, Filename ) );
	}

	private void GenerateHash()
	{
		using MD5 md5 = MD5.Create();
		md5.Initialize();
		_ = md5.ComputeHash( Encoding.UTF8.GetBytes( FullText ) );
		Hash = BitConverter.ToUInt16( md5.Hash, 0 );
	}

	private void CompleteDefinition()
	{
		foreach ( Class c in Classes )
		{
			c.ClassIdent = FastHash( c.ManagedNameWithNamespace );
			//
			// This class has a baseclass, find the actual class and set it.
			//
			if ( !string.IsNullOrWhiteSpace( c.BaseClassName ) )
			{
				c.BaseClass = Classes.FirstOrDefault( x => x.ManagedNameWithNamespace == c.BaseClassName );

				c.BaseClass ??= Classes.SingleOrDefault( x => x.ManagedName == c.BaseClassName );

				if ( c.BaseClass == null )
				{
					throw new Exception( $"{c.ManagedNameWithNamespace} has unknown baseclass: {c.BaseClassName}" );
				}
			}

			Class baseclass = c.BaseClass;

			while ( baseclass != null )
			{
				c.ClassDepth++;
				c.Functions.AddRange( baseclass.Functions.Where( x => !x.Static ) );
				baseclass = baseclass.BaseClass;
			}

			//
			// Fix the functions up
			//
			foreach ( Function f in c.Functions )
			{
				try
				{
					f.Return = TryFixArg( f.Return );

					for ( int i = 0; i < f.Parameters.Length; i++ )
					{
						f.Parameters[i] = TryFixArg( f.Parameters[i] );
					}
				}
				catch ( System.Exception e )
				{
					Log.Warning( $"[{e.Message}] in {c.ManagedNameWithNamespace}.{f.Name}" );
					throw new Exception( $"Unhandled Type [{e.Message}] in {c.ManagedNameWithNamespace}.{f.Name}" );
				}
			}

			//
			// Fix the variables up
			//
			foreach ( Variable f in c.Variables )
			{
				try
				{
					f.Return = TryFixArg( f.Return );
				}
				catch ( System.Exception e )
				{
					Log.Warning( $"[{e.Message}] in {c.ManagedNameWithNamespace}.{f.Name}" );
					throw new Exception( "Unhandled Type" );
				}
			}
		}

		foreach ( Class c in Classes )
		{
			c.Functions = c.Functions.Distinct().Select( x => x.Copy() ).ToList();
			c.Children = Classes.Where( x => x.DerivesFrom( c ) ).ToList();
		}
	}

	private Arg TryFixArg( Arg arg )
	{
		if ( arg is ArgUnknown au )
		{
			Class c = Classes.SingleOrDefault( x => x.ManagedNameWithNamespace == au.Type );
			if ( c != null && c.Native )
			{
				return ClassArgument( au, c );
			}

			if ( c != null && !c.Native )
			{
				return au.Wrap( new ArgManagedClass( c, au.Name, au.Flags ) );
			}

			Struct s = Structs.SingleOrDefault( x => x.ManagedNameWithNamespace == au.Type );
			if ( s != null )
			{
				return au.Wrap( s.IsEnum ? new ArgEnum( s, au.Name ) : new ArgDefinedStruct( s, au.Name, au.Flags ) );
			}

			c = Classes.SingleOrDefault( x => x.ManagedName == au.Type );
			if ( c != null && c.Native )
			{
				return ClassArgument( au, c );
			}

			if ( c != null && !c.Native )
			{
				return au.Wrap( new ArgManagedClass( c, au.Name, au.Flags ) );
			}

			s = Structs.SingleOrDefault( x => x.ManagedName == au.Type || x.NativeNameWithNamespace == au.Type || x.NativeName == au.Type );
			return s != null
				? au.Wrap( s.IsEnum ? new ArgEnum( s, au.Name ) : new ArgDefinedStruct( s, au.Name, au.Flags ) )
				: FunctionPointers.Contains( au.Type )
				? au.Wrap( new ArgFunctionPointer( au.Type, au.Name, au.Flags ) )
				: Delegates.Contains( au.Type )
				? au.Wrap( new ArgDelegate( au.Type, au.Name, au.Flags ) )
				: throw new System.Exception( $"Unknown Type {au.Type}" );
		}

		return arg;
	}

	private Arg ClassArgument( ArgUnknown au, Class c )
	{
		return c.HasAttribute( "SharedDataPointer" )
			? au.Wrap( new ArgSharedDataPointer( c, au.Name, au.Flags ) )
			: au.Wrap( new ArgDefinedClass( c, au.Name, au.Flags ) );
	}

	private void Sort()
	{
		Structs = Structs.OrderBy( x => x.NativeName ).ToList();
		Classes = Classes.OrderBy( x => x.NativeNameWithNamespace ).ToList();

		foreach ( Class c in Classes )
		{
			c.Functions = c.Functions.OrderBy( x => x.MangledName ).ToList();
		}
	}

	public bool IsIncluded( Class c )
	{
		return IncludedDefinitions.Any( x => x.Classes.Any( y => y.ManagedNameWithNamespace == c.ManagedNameWithNamespace && y.NativeNameWithNamespace == c.NativeNameWithNamespace ) );
	}

	public bool IsIncluded( Struct s )
	{
		return IncludedDefinitions.Any( x => x.Structs.Any( y => y.ManagedNameWithNamespace == s.ManagedNameWithNamespace && y.NativeNameWithNamespace == s.NativeNameWithNamespace ) );
	}

	public bool isRebind( Class c )
	{
		return SkipDefines.Any( x => x.Classes.Any( y => y.ManagedNameWithNamespace == c.ManagedNameWithNamespace && y.NativeNameWithNamespace == c.NativeNameWithNamespace ) );
	}

	public static int FastHash( string str )
	{
		// FNV-1a hash
		uint hash = 0x811C9DC5;
		byte[] data = Encoding.Unicode.GetBytes( str );

		foreach ( byte b in data )
		{
			hash ^= b;
			hash *= 0x1000193;
		}

		return unchecked((int)hash);
	}
}
