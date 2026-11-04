using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Editor;

partial class StandaloneExporter
{
	/// <summary>
	/// Writes Win32 resources into the exported executable in one UpdateResource session: the raw
	/// data blobs the game is identified by, the version info Explorer, Defender and SmartScreen
	/// show for it, and its icon. Everything is written as language 0 (neutral), which is what the
	/// SDK gave the launcher's own resources, so ours replace the template's rather than sit beside them.
	/// </summary>
	static class PeResources
	{
		[DllImport( "kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode )]
		private static extern IntPtr BeginUpdateResource( string pFileName, [MarshalAs( UnmanagedType.Bool )] bool bDeleteExistingResources );

		[DllImport( "kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode )]
		[return: MarshalAs( UnmanagedType.Bool )]
		private static extern bool UpdateResource( IntPtr hUpdate, IntPtr lpType, string lpName, ushort wLanguage, byte[] lpData, uint cbData );

		[DllImport( "kernel32.dll", SetLastError = true )]
		[return: MarshalAs( UnmanagedType.Bool )]
		private static extern bool UpdateResource( IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage, byte[] lpData, uint cbData );

		[DllImport( "kernel32.dll", SetLastError = true )]
		[return: MarshalAs( UnmanagedType.Bool )]
		private static extern bool EndUpdateResource( IntPtr hUpdate, [MarshalAs( UnmanagedType.Bool )] bool fDiscard );

		private static readonly IntPtr RT_ICON = new( 3 );
		private static readonly IntPtr RT_RCDATA = new( 10 );
		private static readonly IntPtr RT_GROUP_ICON = new( 14 );
		private static readonly IntPtr RT_VERSION = new( 16 );
		private const ushort LanguageNeutral = 0;

		public record VersionInfo( string Title, string Company, string Comments );

		/// <summary>
		/// Write named RCDATA blobs, replace the version info, and (if <paramref name="icoPath"/> is given) the icon.
		/// </summary>
		public static void Write( string exePath, IReadOnlyDictionary<string, byte[]> data, VersionInfo version, string icoPath )
		{
			var update = BeginUpdateResource( exePath, false );
			if ( update == IntPtr.Zero )
				throw new Win32Exception();

			try
			{
				foreach ( var (name, bytes) in data )
				{
					Update( update, RT_RCDATA, name, bytes );
				}

				Update( update, RT_VERSION, new IntPtr( 1 ), BuildVersionInfo( version ) );

				if ( !string.IsNullOrEmpty( icoPath ) )
				{
					var (group, images) = ParseIcoFile( File.ReadAllBytes( icoPath ) );
					Update( update, RT_GROUP_ICON, new IntPtr( 1 ), group );
					for ( int i = 0; i < images.Length; i++ )
					{
						Update( update, RT_ICON, new IntPtr( i + 1 ), images[i] );
					}
				}

				if ( !EndUpdateResource( update, false ) )
					throw new Win32Exception();
			}
			catch
			{
				EndUpdateResource( update, true );
				throw;
			}
		}

		private static void Update( IntPtr update, IntPtr type, string name, byte[] bytes )
		{
			if ( !UpdateResource( update, type, name, LanguageNeutral, bytes, (uint)bytes.Length ) )
				throw new Win32Exception();
		}

		private static void Update( IntPtr update, IntPtr type, IntPtr id, byte[] bytes )
		{
			if ( !UpdateResource( update, type, id, LanguageNeutral, bytes, (uint)bytes.Length ) )
				throw new Win32Exception();
		}

		/// <summary>
		/// A VS_VERSIONINFO block: the fixed file info, one en-US string table, and the translation
		/// entry that tells readers which string table to look in. Every node is (WORD wLength,
		/// wValueLength, wType) + key + value + children, DWORD aligned.
		/// </summary>
		private static byte[] BuildVersionInfo( VersionInfo info )
		{
			const string version = "1.0.0.0";

			var fixedInfo = new MemoryStream();
			using ( var w = new BinaryWriter( fixedInfo ) )
			{
				w.Write( 0xFEEF04BDu );   // dwSignature
				w.Write( 0x00010000u );   // dwStrucVersion
				w.Write( 0x00010000u );   // dwFileVersionMS 1.0
				w.Write( 0u );            // dwFileVersionLS .0.0
				w.Write( 0x00010000u );   // dwProductVersionMS
				w.Write( 0u );            // dwProductVersionLS
				w.Write( 0x3Fu );         // dwFileFlagsMask
				w.Write( 0u );            // dwFileFlags
				w.Write( 0x00040004u );   // dwFileOS: VOS_NT_WINDOWS32
				w.Write( 1u );            // dwFileType: VFT_APP
				w.Write( 0u );            // dwFileSubtype
				w.Write( 0u );            // dwFileDateMS
				w.Write( 0u );            // dwFileDateLS
			}

			var strings = new (string Key, string Value)[]
			{
				("CompanyName", info.Company ?? ""),
				("FileDescription", info.Title),
				("FileVersion", version),
				("InternalName", info.Title),
				("ProductName", info.Title),
				("ProductVersion", version),
				("Comments", info.Comments ?? ""),
			};

			// "040904B0": language 0x0409 (en-US), code page 0x04B0 (Unicode); Translation says the same, as bytes
			var stringTable = Node( "040904B0", type: 1, value: null, children: strings.Select( s => Node( s.Key, type: 1, value: Utf16( s.Value ), children: null, valueLengthInWords: true ) ) );
			var stringFileInfo = Node( "StringFileInfo", type: 1, value: null, children: [stringTable] );
			var translation = Node( "Translation", type: 0, value: [0x09, 0x04, 0xB0, 0x04], children: null );
			var varFileInfo = Node( "VarFileInfo", type: 1, value: null, children: [translation] );

			return Node( "VS_VERSION_INFO", type: 0, value: fixedInfo.ToArray(), children: [stringFileInfo, varFileInfo] );
		}

		private static byte[] Utf16( string text ) => Encoding.Unicode.GetBytes( text + "\0" );

		private static byte[] Node( string key, ushort type, byte[] value, IEnumerable<byte[]> children, bool valueLengthInWords = false )
		{
			using var stream = new MemoryStream();
			using var w = new BinaryWriter( stream );

			var valueLength = value is null ? 0 : (valueLengthInWords ? value.Length / 2 : value.Length);

			w.Write( (ushort)0 ); // wLength, patched below
			w.Write( (ushort)valueLength );
			w.Write( type );
			w.Write( Utf16( key ) );
			Align( w );

			if ( value is not null )
			{
				w.Write( value );
				Align( w );
			}

			foreach ( var child in children ?? [] )
			{
				w.Write( child );
				Align( w );
			}

			var bytes = stream.ToArray();
			bytes[0] = (byte)bytes.Length;
			bytes[1] = (byte)(bytes.Length >> 8);
			return bytes;
		}

		private static void Align( BinaryWriter w )
		{
			while ( w.BaseStream.Position % 4 != 0 )
				w.Write( (byte)0 );
		}

		/// <summary>
		/// Split an .ico into the RT_GROUP_ICON directory (which references images by resource id)
		/// and the RT_ICON images it references, ids 1..n.
		/// </summary>
		private static (byte[] Group, byte[][] Images) ParseIcoFile( byte[] ico )
		{
			using var reader = new BinaryReader( new MemoryStream( ico ) );

			reader.ReadInt16();                 // reserved
			var type = reader.ReadInt16();      // 1 for .ico
			var count = reader.ReadInt16();
			if ( type != 1 )
				throw new ArgumentException( "Invalid ICO file" );

			// 16 byte directory entries: width, height, colors, reserved, planes, bitcount, size, offset
			var entries = new byte[count][];
			var images = new byte[count][];
			for ( int i = 0; i < count; i++ )
			{
				entries[i] = reader.ReadBytes( 16 );
			}

			for ( int i = 0; i < count; i++ )
			{
				var size = BitConverter.ToInt32( entries[i], 8 );
				var offset = BitConverter.ToInt32( entries[i], 12 );
				images[i] = ico.AsSpan( offset, size ).ToArray();
			}

			// The group is the same directory, with each entry's 4 byte file offset replaced by a 2 byte resource id
			using var group = new MemoryStream();
			using var w = new BinaryWriter( group );
			w.Write( (short)0 );
			w.Write( (short)1 );
			w.Write( count );
			for ( int i = 0; i < count; i++ )
			{
				w.Write( entries[i], 0, 12 );
				w.Write( (short)(i + 1) );
			}

			return (group.ToArray(), images);
		}
	}
}
