using System.Globalization;
using System.IO;
using System.Text;

namespace Borealis.Source2.Shaders;

/// <summary>
/// The shader modules variants made, kept beside the device's pipeline cache (<c>shader-modules.txt</c>), each by its
/// compiled file, program and static and dynamic combo: the next run makes them on a worker as soon as their file loads
/// (a material naming it, a GPU-driven variant found), rather than when a variant is first drawn, so the device's part
/// manifest (<c>pipeline-parts.txt</c>) has its modules, and makes their pipeline parts, before the draws that need them.
/// A module is recorded when a variant first makes it (never per frame); a line no run made in <see cref="MaxAge"/> runs is
/// dropped, as is one that doesn't read or names a combo the file no longer has. Nothing about the file can fail a load.
/// </summary>
internal sealed class ShaderModuleManifest
{
	const string Header = "borealis shader modules 1";

	/// <summary>Lines kept at most.</summary>
	public const int MaxEntries = 20000;

	/// <summary>Runs a line is kept for without a variant making its module.</summary>
	public const int MaxAge = 64;

	/// <summary>A module recorded: which program of a file, its static and dynamic combo.</summary>
	public readonly record struct Entry( ShaderProgramType Type, ulong Static, ulong Dynamic );

	readonly string path;
	readonly object gate = new();
	readonly int run = 1;

	// Each line (file, type, static, dynamic) and the run that last made it; entries read back not yet made, by file
	readonly Dictionary<string, int> lines = new( StringComparer.Ordinal );
	readonly Dictionary<string, List<Entry>> byFile = new( StringComparer.OrdinalIgnoreCase );
	bool dirty;

	/// <summary>Lines read, modules made ahead from them, lines dropped, and modules recorded this run.</summary>
	public int Loaded, Made, Dropped, Recorded;

	public ShaderModuleManifest( string path )
	{
		this.path = path;
		try
		{
			if ( !File.Exists( path ) ) return;
			using var reader = new StreamReader( path, Encoding.UTF8 );
			var header = reader.ReadLine();
			if ( header is null || !header.StartsWith( Header + "\t", StringComparison.Ordinal ) || !int.TryParse( header.AsSpan( Header.Length + 1 ), NumberStyles.None, CultureInfo.InvariantCulture, out var last ) ) return;
			run = last + 1;
			while ( reader.ReadLine() is { } text )
			{
				var tab = text.IndexOf( '\t' );
				if ( tab <= 0 || !int.TryParse( text.AsSpan( 0, tab ), NumberStyles.None, CultureInfo.InvariantCulture, out var used ) || used > last ) continue;
				var line = text[(tab + 1)..];
				if ( run - used > MaxAge || lines.Count >= MaxEntries || lines.ContainsKey( line ) || !Read( line, out var file, out var entry ) )
				{
					dirty = true;
					continue;
				}

				lines[line] = used;
				if ( !byFile.TryGetValue( file, out var list ) ) byFile[file] = list = new();
				list.Add( entry );
				Loaded++;
			}
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or DecoderFallbackException )
		{
			// An unreadable file is an empty one
			lines.Clear();
			byFile.Clear();
			Loaded = 0;
			run = 1;
		}
	}

	/// <summary>The modules recorded for a compiled file, once a run.</summary>
	public List<Entry> Take( string file )
	{
		lock ( gate ) return file is not null && byFile.Remove( file, out var taken ) ? taken : null;
	}

	/// <summary>A variant made a module: kept for the next run.</summary>
	public void Use( string file, Entry entry )
	{
		var line = Write( file, entry );
		lock ( gate )
		{
			if ( lines.TryGetValue( line, out var used ) )
			{
				if ( used == run ) return;
				lines[line] = run;
				dirty = true;
				return;
			}

			if ( lines.Count >= MaxEntries ) return;
			lines[line] = run;
			dirty = true;
			Recorded++;
		}
	}

	/// <summary>Forget a module whose combo the file no longer has.</summary>
	public void Drop( string file, Entry entry )
	{
		lock ( gate )
		{
			if ( !lines.Remove( Write( file, entry ) ) ) return;
			dirty = true;
			Dropped++;
		}
	}

	/// <summary>Write the file if anything changed since it was read or last written; false when nothing was written.</summary>
	public bool Save()
	{
		List<string> snapshot;
		lock ( gate )
		{
			if ( !dirty ) return false;
			snapshot = new List<string>( lines.Count + 1 ) { $"{Header}\t{run.ToString( CultureInfo.InvariantCulture )}" };
			foreach ( var (line, used) in lines ) snapshot.Add( $"{used.ToString( CultureInfo.InvariantCulture )}\t{line}" );
			dirty = false;
		}

		var temporary = $"{path}.{Environment.ProcessId}.tmp";
		try
		{
			Directory.CreateDirectory( Path.GetDirectoryName( path ) ?? "." );
			File.WriteAllLines( temporary, snapshot, new UTF8Encoding( false ) );
			File.Move( temporary, path, overwrite: true );
			return true;
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			try { File.Delete( temporary ); }
			catch ( Exception d ) when ( d is IOException or UnauthorizedAccessException ) { }
			lock ( gate ) dirty = true;
			return false;
		}
	}

	static string Write( string file, Entry entry ) => string.Join( '\t', file, ((int)entry.Type).ToString( CultureInfo.InvariantCulture ), entry.Static.ToString( CultureInfo.InvariantCulture ), entry.Dynamic.ToString( CultureInfo.InvariantCulture ) );

	static bool Read( string line, out string file, out Entry entry )
	{
		file = null;
		entry = default;
		var p = line.Split( '\t' );
		if ( p.Length != 4 || p[0].Length == 0 ) return false;
		if ( !int.TryParse( p[1], NumberStyles.None, CultureInfo.InvariantCulture, out var type ) || !Enum.IsDefined( (ShaderProgramType)type ) ) return false;
		if ( !ulong.TryParse( p[2], NumberStyles.None, CultureInfo.InvariantCulture, out var staticId ) || !ulong.TryParse( p[3], NumberStyles.None, CultureInfo.InvariantCulture, out var dynamicId ) ) return false;
		file = p[0];
		entry = new Entry( (ShaderProgramType)type, staticId, dynamicId );
		return true;
	}
}
