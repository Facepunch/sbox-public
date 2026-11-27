using System.IO;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// A <c>.slang</c> file's offline compile (<c>name.slang_c</c> beside it), made by ShaderCompiler from build-shaders and shipped
/// as <c>.shader_c</c> files are: every stage of a surface or raw module, or every combo of a pass module, as cache entries
/// (<see cref="ShaderCacheFile"/>) by key. The game reads it before its own cache and compiles nothing it holds. Its header
/// names the compiler, the shader API level it compiled against (<see cref="ShaderApi"/>), the source's text and every file
/// the compile read, so an unchanged source isn't compiled again.
/// </summary>
public sealed class SlangOutput
{
	/// <summary>The compiled file's suffix after the source's name.</summary>
	public const string Suffix = "_c";

	const int Magic = 0x43474C53; // "SLGC"

	/// <summary>Raised with any change to the layout.</summary>
	const int Format = 5;

	readonly byte[] data;
	readonly Dictionary<string, (int Offset, int Length)> entries = new( StringComparer.Ordinal );
	readonly List<(int Offset, int Length)> records = [];

	/// <summary>The compiler identity and source text hash it was made from, and every file its compiles read with its hash.</summary>
	readonly string identity, textHash;
	readonly int apiLevel;
	readonly bool complete;
	readonly List<(string Name, string Hash)> dependencies = [];

	/// <summary>A pass module's declarations, includes and all (<see cref="PassModule.Declarations"/>); empty for other modules.</summary>
	readonly byte[] declarations = [];

	SlangOutput( byte[] data )
	{
		this.data = data;
		using var r = new BinaryReader( new MemoryStream( data, writable: false ) );
		if ( r.ReadInt32() != Magic || r.ReadInt32() != Format || r.ReadInt32() != ShaderBinary.Format ) return;
		identity = r.ReadString();
		apiLevel = r.ReadInt32();
		textHash = r.ReadString();
		complete = r.ReadBoolean();
		var dependencyCount = r.ReadInt32();
		for ( int i = 0; i < dependencyCount; i++ ) dependencies.Add( (r.ReadString(), r.ReadString()) );
		var recordCount = r.ReadInt32();
		for ( int i = 0; i < recordCount; i++ )
		{
			var length = r.ReadInt32();
			records.Add( ((int)r.BaseStream.Position, length) );
			r.BaseStream.Position += length;
		}

		var count = r.ReadInt32();
		for ( int i = 0; i < count; i++ ) entries[r.ReadString()] = records[r.ReadInt32()];
		declarations = r.ReadBytes( r.ReadInt32() );
	}

	/// <summary>
	/// The pass module's declarations as the offline compile read them (<see cref="PassModule.Declarations"/>), when it was made
	/// from <paramref name="text"/>: a player's install has none of the private headers some of them come from.
	/// </summary>
	internal byte[] DeclarationsOf( string text ) => declarations.Length > 0 && textHash == CompileCache.Hash( text ) ? declarations : null;

	/// <summary>The shader API level it was compiled against (<see cref="ShaderApi.Level"/> then); 0 when it couldn't be read.</summary>
	public int ApiLevel => apiLevel;

	/// <summary>Entries in it.</summary>
	public int Count => entries.Count;

	/// <summary>The compiled file of the source at <paramref name="source"/>.</summary>
	public static string PathOf( string source ) => source + Suffix;

	static readonly Dictionary<string, (DateTime Time, long Length, SlangOutput Output)> loaded = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>The compiled file at <paramref name="path"/>, read once (again when it changed); null when there's none or it can't be read.</summary>
	public static SlangOutput At( string path )
	{
		var info = new FileInfo( path );
		if ( !info.Exists ) return null;
		lock ( loaded )
		{
			if ( loaded.TryGetValue( path, out var known ) && known.Time == info.LastWriteTimeUtc && known.Length == info.Length ) return known.Output;
			SlangOutput output = null;
			try
			{
				output = new SlangOutput( File.ReadAllBytes( path ) );
			}
			catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or EndOfStreamException )
			{
			}

			loaded[path] = (info.LastWriteTimeUtc, info.Length, output);
			return output;
		}
	}

	/// <summary>The entry with <paramref name="key"/>'s bytes, its key left out (<see cref="ShaderCacheFile.WithoutKey"/>).</summary>
	internal bool TryGet( string key, out ReadOnlyMemory<byte> bytes )
	{
		bytes = default;
		if ( !entries.TryGetValue( key, out var at ) ) return false;
		bytes = data.AsMemory( at.Offset, at.Length );
		return true;
	}

	/// <summary>
	/// Whether it was made whole (nothing failed) by this compiler (<paramref name="compiler"/>) from this text and every file
	/// its compiles read (named as <paramref name="paths"/> resolves them) is unchanged: then compiling the source again would
	/// make the same.
	/// </summary>
	internal bool Current( string compiler, string text, DependencyPaths paths ) =>
		complete && identity == compiler && textHash == CompileCache.Hash( text ) && dependencies.TrueForAll( d => paths.Resolve( d.Name ) is { } path && PassCache.FileHash( path ) == d.Hash );

	/// <summary>Write a compiled file at <paramref name="path"/>; not <paramref name="complete"/> when some of it failed, so it's compiled again next time.</summary>
	internal static void Write( string path, string compiler, string text, bool complete, IEnumerable<(string Name, string Hash)> dependencies, IEnumerable<KeyValuePair<string, byte[]>> entries, byte[] declarations = null )
	{
		// Keys whose compiles came out the same share one record
		var list = entries.OrderBy( e => e.Key, StringComparer.Ordinal ).Select( e => (e.Key, Record: ShaderCacheFile.WithoutKey( e.Value )) ).ToList();
		var records = new List<byte[]>();
		var index = new Dictionary<string, int>( StringComparer.Ordinal );
		var keys = new List<(string Key, int Record)>();
		foreach ( var (key, record) in list )
		{
			var hash = Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( record ) );
			if ( !index.TryGetValue( hash, out var at ) )
			{
				index[hash] = at = records.Count;
				records.Add( record );
			}

			keys.Add( (key, at) );
		}

		var read = dependencies.Distinct().OrderBy( d => d.Name, StringComparer.Ordinal ).ToList();
		var temporary = path + ".tmp";
		using ( var w = new BinaryWriter( File.Create( temporary ) ) )
		{
			w.Write( Magic );
			w.Write( Format );
			w.Write( ShaderBinary.Format );
			w.Write( compiler );
			w.Write( ShaderApi.Level );
			w.Write( CompileCache.Hash( text ) );
			w.Write( complete );
			w.Write( read.Count );
			foreach ( var (name, hash) in read )
			{
				w.Write( name );
				w.Write( hash ?? "" );
			}

			w.Write( records.Count );
			foreach ( var record in records )
			{
				w.Write( record.Length );
				w.Write( record );
			}

			w.Write( keys.Count );
			foreach ( var (key, record) in keys )
			{
				w.Write( key );
				w.Write( record );
			}

			declarations ??= [];
			w.Write( declarations.Length );
			w.Write( declarations );
		}

		File.Move( temporary, path, overwrite: true );
	}
}
