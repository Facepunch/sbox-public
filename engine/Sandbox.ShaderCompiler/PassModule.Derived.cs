using System.IO;
using System.Text.Json;

namespace Sandbox.ShaderCompiler;

public sealed partial class PassModule
{
	/// <summary>
	/// Where what's read from this module's preprocessed lines per combo values (entry points, render states, expansion) is
	/// kept between runs, so a run reads it rather than preprocessing the module and its includes again: a frame's first
	/// draws of pass-module shaders spent hundreds of milliseconds on that. Null keeps it for this run only. An entry is used
	/// while the module's text and every include it read are as they were.
	/// </summary>
	public string DerivedCacheFolder { get; set; }

	const int DerivedFormat = 1;

	readonly object derivedGate = new();
	DerivedFile derived;
	readonly Dictionary<string, int> setsByKey = new( StringComparer.Ordinal );
	readonly Dictionary<int, (bool Valid, long At)> setChecks = new();
	bool saveQueued;
	string textHash;

	string TextHash => textHash ??= CompileCache.Hash( Text );

	string DerivedFilePath => DerivedCacheFolder is null ? null
		: System.IO.Path.Combine( DerivedCacheFolder, $"{System.IO.Path.GetFileNameWithoutExtension( Path )}.{CompileCache.Hash( System.IO.Path.GetFullPath( Path ).ToLowerInvariant() )[..16].ToLowerInvariant()}.json" );

	/// <summary>The kept file: the module text's hash, include sets (path, hash, path, hash...), and reads by values.</summary>
	internal sealed class DerivedFile
	{
		public int Format { get; set; }
		public string Text { get; set; }
		public List<string[]> Sets { get; set; } = [];
		public Dictionary<string, DerivedEntry> Entries { get; set; } = [];
	}

	/// <summary>One combo values' reads, and the include set they were read with.</summary>
	internal sealed class DerivedEntry
	{
		public int Set { get; set; }
		public Dictionary<string, string> Reads { get; set; } = [];
	}

	/// <summary>Read the kept file once (on any thread: the game reads modules ahead on a worker).</summary>
	public void ReadDerived()
	{
		lock ( derivedGate ) Loaded();
	}

	DerivedFile Loaded()
	{
		if ( derived is not null ) return derived;
		derived = new DerivedFile { Format = DerivedFormat, Text = TextHash };
		if ( DerivedFilePath is { } file )
		{
			try
			{
				if ( File.Exists( file ) && JsonSerializer.Deserialize<DerivedFile>( File.ReadAllText( file ) ) is { Format: DerivedFormat } kept && kept.Text == TextHash ) derived = kept;
			}
			catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or JsonException )
			{
			}
		}

		for ( int i = 0; i < derived.Sets.Count; i++ ) setsByKey[string.Join( '|', derived.Sets[i] )] = i;
		return derived;
	}

	/// <summary>Whether an include set's files hash as they did, looked at again at most every <see cref="IncludeCheckMs"/>.</summary>
	bool SetValid( int index )
	{
		var now = Environment.TickCount64;
		if ( setChecks.TryGetValue( index, out var check ) && now - check.At < IncludeCheckMs ) return check.Valid;
		var set = derived.Sets[index];
		var valid = true;
		for ( int i = 0; valid && i + 1 < set.Length; i += 2 ) valid = PassCache.FileHash( set[i] ) == set[i + 1];
		setChecks[index] = (valid, now);
		return valid;
	}

	string KeptRead( string key, string what )
	{
		lock ( derivedGate )
		{
			var file = Loaded();
			if ( !file.Entries.TryGetValue( key, out var entry ) || !entry.Reads.TryGetValue( what, out var text ) ) return null;
			return entry.Set < file.Sets.Count && SetValid( entry.Set ) ? text : null;
		}
	}

	void KeepRead( string key, string what, string text, List<(string Path, string Hash)> includes )
	{
		lock ( derivedGate )
		{
			var file = Loaded();
			var set = includes.SelectMany( i => new[] { i.Path, i.Hash ?? "" } ).ToArray();
			var setKey = string.Join( '|', set );
			if ( !setsByKey.TryGetValue( setKey, out var index ) )
			{
				index = file.Sets.Count;
				file.Sets.Add( set );
				setsByKey[setKey] = index;
			}

			// Read with other includes before: what else it kept for these values is stale
			if ( !file.Entries.TryGetValue( key, out var entry ) || entry.Set != index ) file.Entries[key] = entry = new DerivedEntry { Set = index };
			entry.Reads[what] = text;
			setChecks[index] = (true, Environment.TickCount64);

			if ( DerivedCacheFolder is null || saveQueued ) return;
			saveQueued = true;
		}

		// Written a moment later, once with what the frame read rather than for each read
		System.Threading.Tasks.Task.Delay( 2000 ).ContinueWith( _ => SaveDerived() );
	}

	/// <summary>Write the kept reads now. For tests; the module writes them itself a moment after it reads something new.</summary>
	public void SaveDerived()
	{
		string json;
		lock ( derivedGate )
		{
			saveQueued = false;
			if ( derived is null || DerivedFilePath is null ) return;
			json = JsonSerializer.Serialize( derived );
		}

		ShaderCacheFile.Save( DerivedFilePath, System.Text.Encoding.UTF8.GetBytes( json ) );
	}
}
