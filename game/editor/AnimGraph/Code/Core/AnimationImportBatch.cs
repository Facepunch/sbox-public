using System.IO;

namespace Editor.AnimGraph;

internal sealed class AnimationImportEntry
{
	public string Source { get; init; }
	public string Name { get; set; }
	public ClipDocument Document { get; init; }
	public bool Included { get; set; } = true;
	public Asset Asset { get; set; }
	public string Error { get; set; }
	public bool IsImporting { get; set; }
	public string OutputPath { get; set; }
}

internal sealed class AnimationImportBatch
{
	private readonly string _assetsDirectory;
	private readonly Dictionary<string, (string Path, DateTime Modified, long Length)> _copiedSources = new( StringComparer.OrdinalIgnoreCase );

	public ClipDocument Settings { get; }
	public List<AnimationImportEntry> Entries { get; } = [];
	public string OutputDirectory { get; set; }
	public bool IsRunning { get; private set; }
	public bool CancelRequested { get; set; }
	public int ProcessedCount { get; private set; }
	public int TotalCount { get; private set; }

	public AnimationImportBatch( string assetsDirectory, string outputDirectory, string skeletonName )
	{
		_assetsDirectory = Path.GetFullPath( assetsDirectory );
		OutputDirectory = outputDirectory;
		Settings = ClipDocument.Create( skeletonName );
	}

	public void AddSources( IEnumerable<string> sources )
	{
		var existing = Entries.Select( entry => entry.Source ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		foreach ( var source in sources )
		{
			var path = Path.GetFullPath( source, _assetsDirectory );
			if ( !existing.Add( path ) )
				continue;

			Entries.Add( CreateEntry( path, Path.GetFileNameWithoutExtension( path ), 0 ) );
		}
	}

	private AnimationImportEntry CreateEntry( string source, string name, int take )
	{
		var document = ClipDocument.Create();
		document.SourceFilename = source;
		document.Take = take;
		return new AnimationImportEntry { Source = source, Name = name, Document = document };
	}

	public void AddAllTakes( AnimationImportEntry entry )
	{
		var takes = entry.Document.GetAnimationTakes( refresh: true );
		if ( takes.Count == 0 )
			throw new InvalidOperationException( "Couldn't read animation takes. Check the source file and console for details." );

		var stem = Path.GetFileNameWithoutExtension( entry.Source );
		var takeNames = takes.Select( SafeName ).ToArray();
		var nameCounts = takeNames.CountBy( name => name, StringComparer.OrdinalIgnoreCase ).ToDictionary( StringComparer.OrdinalIgnoreCase );
		var existing = Entries.Where( other => string.Equals( other.Source, entry.Source, StringComparison.OrdinalIgnoreCase ) )
			.Select( other => other.Document.Take ).ToHashSet();
		for ( var take = 0; take < takes.Count; ++take )
		{
			var takeName = takeNames[take];
			var suffix = nameCounts[takeName] > 1 ? $"_{take + 1}" : "";
			var name = $"{stem}_{takeName}{suffix}";
			if ( !existing.Add( take ) )
			{
				if ( entry.Document.Take == take && entry.Document.FilePath is null && entry.Name == stem )
					entry.Name = name;
				continue;
			}

			Entries.Add( CreateEntry( entry.Source, name, take ) );
		}
	}

	private static string SafeName( string name )
	{
		var invalid = Path.GetInvalidFileNameChars();
		var result = new string( name.Select( c => invalid.Contains( c ) ? '_' : c ).ToArray() ).Trim().TrimEnd( '.' );
		return string.IsNullOrEmpty( result ) ? "take" : result;
	}

	public string GetOutputPath( AnimationImportEntry entry )
		=> entry.Document.FilePath ?? Path.Combine( Path.GetFullPath( OutputDirectory, _assetsDirectory ), entry.Name + ".anim" );

	public string ValidateSettings()
	{
		if ( string.IsNullOrWhiteSpace( Settings.SkeletonName ) )
			return "Choose the skeleton these animations will play on.";
		if ( Skeleton.Load( Settings.SkeletonName ) is not { IsValid: true, IsError: false, BoneCount: > 0 } )
			return "The selected skeleton could not be loaded. Check its compilation errors.";
		if ( !float.IsFinite( Settings.ImportScale ) || Settings.ImportScale <= 0 )
			return "Import scale must be positive and finite.";

		try
		{
			if ( string.IsNullOrWhiteSpace( OutputDirectory ) || !IsProjectAsset( Path.GetFullPath( OutputDirectory, _assetsDirectory ) )
				|| File.Exists( Path.GetFullPath( OutputDirectory, _assetsDirectory ) ) )
				return "Choose an output folder inside this project's Assets folder.";
		}
		catch ( Exception exception ) when ( exception is ArgumentException or NotSupportedException or PathTooLongException )
		{
			return "Choose a valid output folder inside this project's Assets folder.";
		}
		return null;
	}

	public Dictionary<AnimationImportEntry, string> ValidateEntries()
	{
		var outputCounts = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		var errors = new Dictionary<AnimationImportEntry, string>();
		foreach ( var entry in Entries.Where( entry => entry.Included ) )
		{
			try
			{
				var path = GetOutputPath( entry );
				outputCounts[path] = outputCounts.GetValueOrDefault( path ) + 1;
			}
			catch ( Exception exception ) when ( exception is ArgumentException or NotSupportedException or PathTooLongException )
			{
				errors[entry] = "The output path is invalid. Choose another folder or clip name.";
			}
		}
		foreach ( var entry in Entries )
			errors[entry] = GetEntryError( entry, outputCounts ) ?? errors.GetValueOrDefault( entry );
		return errors;
	}

	public string ValidateEntry( AnimationImportEntry entry ) => GetEntryError( entry );

	private string GetEntryError( AnimationImportEntry entry, Dictionary<string, int> outputCounts = null )
	{
		if ( !File.Exists( entry.Source ) || Path.GetExtension( entry.Source ).ToLowerInvariant() is not (".fbx" or ".dmx" or ".smd") )
			return "Choose an existing FBX, DMX or SMD animation file.";
		if ( string.IsNullOrWhiteSpace( entry.Name ) || entry.Name.IndexOfAny( Path.GetInvalidFileNameChars() ) >= 0
			|| entry.Name.EndsWith( '.' ) || entry.Name.EndsWith( ' ' ) || entry.Name is "." or ".." )
			return "Enter a valid clip name without an extension or folders.";
		if ( entry.Document.StartFrame is { } start && entry.Document.EndFrame is { } end && start > end )
			return "End frame must not be before start frame.";
		if ( entry.Document.DurationOverride is { } duration && (!float.IsFinite( duration ) || duration <= 0) )
			return "Duration must be positive and finite, or unset.";

		try
		{
			var path = GetOutputPath( entry );
			if ( !IsProjectAsset( path ) )
				return "Save clips inside this project's Assets folder.";
			if ( outputCounts is not null ? outputCounts.GetValueOrDefault( path ) > (entry.Included ? 1 : 0) : Entries.Any( other => other != entry && other.Included
				&& string.Equals( GetOutputPath( other ), path, StringComparison.OrdinalIgnoreCase ) ) )
				return "Another queued clip uses this output name. Rename or skip one.";
			if ( entry.Document.FilePath is null && (File.Exists( path ) || File.Exists( path + "_c" ) || File.Exists( path + ".meta" )) )
				return "An asset already exists here. Rename this clip or uncheck it to skip.";
		}
		catch ( Exception exception ) when ( exception is ArgumentException or NotSupportedException or PathTooLongException )
		{
			return "The output path is invalid. Choose another folder or clip name.";
		}
		return null;
	}

	public async Task<IReadOnlyList<Asset>> ImportAsync( Action<AnimationImportEntry> changed )
	{
		if ( IsRunning )
			throw new InvalidOperationException( "An animation import is already running." );
		var pending = Entries.Where( entry => entry.Included && entry.Asset is null ).ToArray();
		var errors = ValidateEntries();
		var validation = ValidateSettings() ?? pending.Select( entry => errors[entry] ).FirstOrDefault( error => error is not null );
		if ( validation is not null )
			throw new InvalidOperationException( validation );

		var imported = new List<Asset>();
		CancelRequested = false;
		ProcessedCount = 0;
		TotalCount = pending.Length;
		IsRunning = true;
		try
		{
			foreach ( var entry in pending )
			{
				if ( CancelRequested )
					break;

				entry.Error = null;
				entry.IsImporting = true;
				changed?.Invoke( entry );
				await Task.Delay( 1 );
				if ( CancelRequested )
				{
					entry.IsImporting = false;
					changed?.Invoke( entry );
					break;
				}
				try
				{
					if ( ValidateEntry( entry ) is { } error )
						throw new InvalidOperationException( error );

					entry.Asset = ImportEntry( entry );
					imported.Add( entry.Asset );
				}
				catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException )
				{
					Log.Warning( exception, $"AnimGraph: could not import '{entry.Source}'" );
					entry.Error = exception.Message;
				}
				finally
				{
					entry.IsImporting = false;
					ProcessedCount++;
					changed?.Invoke( entry );
				}
			}
		}
		finally
		{
			IsRunning = false;
		}
		return imported;
	}

	public void SaveEntry( AnimationImportEntry entry )
	{
		if ( (ValidateSettings() ?? ValidateEntry( entry )) is { } validation )
			throw new InvalidOperationException( validation );
		var path = GetOutputPath( entry );
		var source = entry.Source;
		if ( !IsProjectAsset( source ) )
		{
			var info = new FileInfo( source );
			if ( !_copiedSources.TryGetValue( source, out var copied ) || !File.Exists( copied.Path )
				|| copied.Modified != info.LastWriteTimeUtc || copied.Length != info.Length )
			{
				var directory = Path.Combine( Path.GetDirectoryName( path ), "source" );
				Directory.CreateDirectory( directory );
				var stem = Path.GetFileNameWithoutExtension( source );
				var extension = Path.GetExtension( source );
				var copy = Path.Combine( directory, stem + extension );
				for ( var index = 2; File.Exists( copy ); ++index )
					copy = Path.Combine( directory, $"{stem}_{index}{extension}" );
				File.Copy( source, copy, overwrite: false );
				copied = (copy, info.LastWriteTimeUtc, info.Length);
				_copiedSources[source] = copied;
			}
			source = copied.Path;
		}

		var document = entry.Document;
		document.SourceFilename = Path.GetRelativePath( _assetsDirectory, source ).Replace( '\\', '/' );
		document.SkeletonName = Settings.SkeletonName;
		document.ImportScale = Settings.ImportScale;
		if ( !(document.FilePath is null ? document.SaveAs( path ) : document.Save()) )
			throw new InvalidOperationException( "Couldn't save the clip. Check the console for details." );
		entry.OutputPath = path;
	}

	private Asset ImportEntry( AnimationImportEntry entry )
	{
		SaveEntry( entry );
		var path = entry.OutputPath;
		var asset = AssetSystem.RegisterFile( path );
		if ( asset is null || !asset.Compile( false ) )
			throw new InvalidOperationException( "Clip saved, but compilation failed. Correct its settings and retry; see the console for details." );
		var clip = Clip.Load( asset.Path );
		if ( clip is not { IsValid: true, IsError: false, FrameCount: > 0 }
			|| !string.Equals( clip.SkeletonName, entry.Document.SkeletonName, StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidOperationException( "Clip saved, but could not be loaded against the selected skeleton. Check the console and retry." );
		return asset;
	}

	private bool IsProjectAsset( string path )
	{
		var relative = Path.GetRelativePath( _assetsDirectory, path );
		return !Path.IsPathRooted( relative ) && relative != ".." && !relative.StartsWith( $"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal );
	}
}
