using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Facepunch.InteropGen.Parsers;

namespace Facepunch.InteropGen;

/// <summary>
/// Turns a .def file into a fully-resolved <see cref="Definition"/>:
/// parse → resolve types → sort → mangle → hash.
/// </summary>
internal static class InteropPipeline
{
	public static Definition Build( string filename )
	{
		string folder = Path.GetDirectoryName( filename );
		if ( string.IsNullOrEmpty( folder ) )
		{
			folder = ".";
		}

		string text = File.ReadAllText( filename );

		Definition d = new()
		{
			Filename = Path.GetFileName( filename ),
			Root = new DirectoryInfo( folder )
		};

		Parse( d, text );
		TypeResolver.Resolve( d );
		Sort( d );
		new Mangler().Mangle( d.Classes );
		d.Hash = ComputeFileHash( d.FullText );

		return d;
	}

	private static void Parse( Definition d, string text )
	{
		d.FullText = "";
		new GlobalParser().Parse( d, text, Path.Combine( d.Root.FullName, d.Filename ) );
	}

	private static void Sort( Definition d )
	{
		d.Structs = d.Structs.OrderBy( x => x.NativeName ).ToList();
		d.Classes = d.Classes.OrderBy( x => x.NativeNameWithNamespace ).ToList();

		foreach ( Class c in d.Classes )
		{
			c.Functions = c.Functions.OrderBy( x => x.MangledName ).ToList();
		}
	}

	private static int ComputeFileHash( string fullText )
	{
		using MD5 md5 = MD5.Create();
		byte[] hash = md5.ComputeHash( Encoding.UTF8.GetBytes( fullText ) );
		return BitConverter.ToUInt16( hash, 0 );
	}
}
