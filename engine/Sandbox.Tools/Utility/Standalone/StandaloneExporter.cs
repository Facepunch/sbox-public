using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using static Editor.ProjectPublisher;

namespace Editor;

public partial class StandaloneExporter
{
	static Logger Logger = new Logger( "Exporter" );

	public Project Project => _exportConfig.Project;
	public IReadOnlyList<QueuedFile> Files => _files.AsReadOnly();

	public PackageManifest PackageManifest { get; protected set; }
	public StandaloneManifest StandaloneManifest { get; protected set; }

	public Action<ExportProgress> OnProgressChanged { get; set; }

	private List<QueuedFile> _files = new();
	private ExportConfig _exportConfig;

	StandaloneExporter( ExportConfig config )
	{
		StandaloneManifest = new StandaloneManifest();
		PackageManifest = new PackageManifest();
		_exportConfig = config;
	}

	async Task GenerateAssetManifest( IProgress progress = null, CancellationToken cancel = default )
	{
		await PackageManifest.BuildFromAssets( _exportConfig.Project, progress, cancel );
	}

	public static async Task<StandaloneExporter> FromConfig( ExportConfig config )
	{
		var p = new StandaloneExporter( config );
		await p.GenerateAssetManifest();
		return p;
	}

	private void QueueCopyFile( string targetDir, ProjectFile file, BuildStep type )
	{
		var fileName = file.Name;
		var targetPath = Path.Combine( targetDir, fileName );

		//
		// Create directory if it doesn't exist
		//
		{
			var targetSubDir = Path.GetDirectoryName( targetPath );
			Directory.CreateDirectory( targetSubDir );
		}

		if ( file.Contents is not null )
		{
			if ( File.Exists( targetPath ) ) File.Delete( targetPath );
			File.WriteAllBytes( targetPath, file.Contents );
		}
		else if ( file.AbsolutePath is not null )
		{
			var sourcePath = file.AbsolutePath;
			QueueCopy( sourcePath, targetPath, type );
		}
		else
		{
			Log.Warning( $"Unable to copy {file.Name} - has no content defined!" );
		}

		file.Skip = true;
	}

	public async Task Run()
	{
		Logger.Info( $"Exporting {Project.Config.Title} to {_exportConfig.TargetDir}" );

		Logger.Info( $"Compiling assemblies.." );
		await Compile();

		Logger.Info( $"Building manifest.." );
		await BuildManifest();

		Logger.Info( $"Copying files.." );
		await CopyAllFiles();

		Logger.Info( $"Export complete!" );
		OnFinish();
	}

	private async Task BuildManifest()
	{
		if ( _exportConfig.AssemblyFiles is not null )
		{
			foreach ( var f in _exportConfig.AssemblyFiles )
			{
				if ( f.Value is byte[] bytes )
				{
					await AddFile( bytes, f.Key );
				}

				if ( f.Value is string json )
				{
					await AddFile( json, f.Key );
				}
			}
		}

		// Add assets from Cloud.Model( .. ) etc
		foreach ( var package in _exportConfig.CodePackages )
		{
			await AddCodePackageReference( package );
		}

		// Make sure we're copying into a directory that exists
		Directory.CreateDirectory( _exportConfig.TargetDir );

		// Create standalone properties
		StandaloneManifest = CreateStandaloneManifest( _exportConfig.TargetDir );
		WriteStandaloneManifest( _exportConfig.TargetDir );

		// Build queue
		QueueAddonFiles( _exportConfig.TargetDir, BuildStep.CopyProjectAssets );
		QueueBaseFiles( _exportConfig.TargetDir );
	}

	private void OnFinish()
	{
		if ( !string.IsNullOrEmpty( _exportConfig.TargetIcon ) )
			IconUpdater.UpdateExeIcon( $"{_exportConfig.TargetDir}/{StandaloneManifest.ExecutableName}.exe", _exportConfig.TargetIcon );
	}

	private async Task CopyAllFiles()
	{
		var orderedFiles = _files.OrderBy( x => x.Step );
		foreach ( var f in orderedFiles )
		{
			await CopyFileAsync( f );
		}
	}

	private void WriteStandaloneManifest( string targetDir )
	{
		var gameDataPath = Path.Combine( targetDir, Standalone.GamePath );
		Directory.CreateDirectory( gameDataPath );

		var serializedProperties = JsonSerializer.Serialize( StandaloneManifest );
		var manifestPath = Path.Combine( targetDir, Standalone.GamePath, Standalone.ManifestName );
		File.WriteAllText( manifestPath, serializedProperties );
	}

	private StandaloneManifest CreateStandaloneManifest( string baseDir )
	{
		if ( !_exportConfig.Project.Config.TryGetMeta( "ControlModes", out ControlModeSettings controlSettings ) )
			controlSettings = new();

		return new StandaloneManifest()
		{
			BuildDate = _exportConfig.BuildDate,
			ExecutableName = _exportConfig.ExecutableName,
			Ident = _exportConfig.Project.Package.Ident,
			Name = _exportConfig.Project.Package.Title,
			AppId = _exportConfig.AppId,

			IsVRProject = controlSettings.IsVROnly
		};
	}

	private void QueueAddonFiles( string baseDir, BuildStep type )
	{
		var targetDir = Path.Combine( baseDir, Standalone.GamePath );
		Directory.CreateDirectory( targetDir );

		var targets = PackageManifest.Assets.Where( x => !x.Skip ).ToArray();
		var tasks = new List<Task>();

		foreach ( var t in targets )
		{
			QueueCopyFile( targetDir, t, type );
		}
	}

	private void QueueBaseFiles( string baseDir )
	{
		var engineDir = Environment.CurrentDirectory;

		void QueueFrom( IEnumerable<string> paths )
		{
			foreach ( var file in paths )
			{
				var relativePath = Path.GetRelativePath( engineDir, file );
				var targetPath = Path.Combine( baseDir, relativePath );

				QueueCopy( file, targetPath, BuildStep.CopyCoreAssets );
			}
		}

		{
			// Get all core files - only the ones we absolutely need, because everything else should
			// already have been copied into the addon itself.
			// This is mainly stuff like dev textures that are necessary for the engine to run.
			QueueFrom( GetCoreFiles( engineDir ) );

			// Fetch all the DLLs we need to run the engine
			QueueFrom( GetDllFiles( engineDir ) );
		}

		//
		// Copy core compiled files
		//
		{
			void QueueCompiled( string dir, BuildStep type )
			{
				foreach ( var subdir in Directory.GetDirectories( dir ) )
				{
					QueueCompiled( subdir, type );
				}

				foreach ( var file in Directory.GetFiles( dir ) )
				{
					var relativePath = Path.GetRelativePath( engineDir, file );
					var targetPath = Path.Combine( baseDir, relativePath );

					if ( Path.GetExtension( file ).EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) )
						QueueCopy( file, targetPath, type );
				}
			}

			// Copy all from enabled addons, in case they reference anything at runtime
			// (e.g. UI shaders in base)
			QueueCompiled( $"{engineDir}/addons/base", BuildStep.CopyProjectAssets );

			// Get all core files - only the ones we absolutely need, because everything else should
			// already have been copied into the addon itself.
			// This is mainly stuff like dev textures that are necessary for the engine to run.
			foreach ( var file in GetCoreFiles( engineDir ) )
			{
				var relativePath = Path.GetRelativePath( engineDir, file );
				var targetPath = Path.Combine( baseDir, relativePath );

				if ( Path.GetExtension( file ).EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) )
					QueueCopy( file, targetPath, BuildStep.CopyCoreAssets );
			}
		}

		//
		// Fetch code resources (resource manifest stuff from native)
		//
		{
			var codeResources = GetCodeResources( engineDir );

			foreach ( var resource in codeResources )
			{
				var sourcePath = resource.AbsolutePath;
				var targetPath = Path.Combine( baseDir, $"core/{resource.RelativePath}" );

				// Only copy compiled resources
				if ( !sourcePath.EndsWith( "_c" ) )
					sourcePath += "_c";

				if ( !targetPath.EndsWith( "_c" ) )
					targetPath += "_c";

				QueueCopy( sourcePath, targetPath, BuildStep.CopyCode );
			}
		}

		//
		// Copy:
		// - addons/base/ui/*
		// - addons/base/fonts/*
		//
		{
			void QueueAll( string dir, BuildStep type )
			{
				foreach ( var subdir in Directory.GetDirectories( dir ) )
				{
					QueueAll( subdir, type );
				}

				foreach ( var file in Directory.GetFiles( dir ) )
				{
					var relativePath = Path.GetRelativePath( engineDir, file );
					var targetPath = Path.Combine( baseDir, relativePath );

					QueueCopy( file, targetPath, type );
				}
			}

			QueueAll( $"{engineDir}/addons/base/assets/ui", BuildStep.CopyBaseAssets ); // Necessary
			QueueAll( $"{engineDir}/addons/base/assets/fonts", BuildStep.CopyBaseAssets ); // Necessary
		}

		//
		// Copy exe
		//
		{
			QueueCopy( $"{engineDir}/sbox-standalone.exe", $"{baseDir}/{StandaloneManifest.ExecutableName}.exe", BuildStep.FinalizeExecutable );
		}

		//
		// Copy sbproj for base + addon - ideally we should store these in an embedded resource inside the exe
		//
		{
			var sbprojPath = Path.Combine( baseDir, Standalone.GamePath, ".sbproj" );
			QueueCopy( $"{_exportConfig.Project.ConfigFilePath}", sbprojPath, BuildStep.CopyMisc );
			QueueCopy( $"{engineDir}/addons/base/.sbproj", $"{baseDir}/addons/base/.sbproj", BuildStep.CopyMisc );
		}

		//
		// Copy loose files
		//
		{
			// Have to copy this or the engine will crash super early. Not sure we actually use it though
			QueueCopy( $"{engineDir}/bin/assettypes.txt", $"{baseDir}/bin/assettypes.txt", BuildStep.CopyMisc );
		}

		//
		// Splash screen image
		// Has to be part of core (maybe we can change this)
		//
		if ( !string.IsNullOrEmpty( _exportConfig.StartupImage ) )
		{
			var path = _exportConfig.StartupImage;
			var startupImagePath = Path.Combine( _exportConfig.Project.GetAssetsPath(), path + "_c" );
			QueueCopy( $"{startupImagePath}", $"{baseDir}/core/materials/startup_background.vtex_c", BuildStep.CopyMisc );
		}
	}

	private void QueueCopy( string source, string dest, BuildStep type )
	{
		_files.Add( new QueuedFile( source, dest, type ) );
	}

	private async Task CopyFileAsync( QueuedFile file )
	{
		//
		// Make sure we have a directory to copy into
		//
		{
			var targetDir = Path.GetDirectoryName( file.Destination );
			if ( !Directory.Exists( targetDir ) ) Directory.CreateDirectory( targetDir );
		}

		//
		// Attempt to copy the file
		//
		try
		{
			// Check if the file exists first - there's no point in spinning up a task if it doesn't.
			if ( !File.Exists( file.Source ) )
			{
				file.State = QueuedFileState.FileNotFound;
				return;
			}

			await Task.Run( () =>
			{
				try
				{
					File.Copy( file.Source, file.Destination, true );
					file.State = QueuedFileState.Copied;
				}
				catch ( Exception ex )
				{
					Log.Error( ex );
					file.State = QueuedFileState.FailedToCopy;
				}
			} );
		}
		finally
		{
			TriggerProgessChanged( file );
		}
	}

	/// <summary>
	/// Manually add a file to the manifest
	/// </summary>
	public Task AddFile( byte[] contents, string relativePath )
	{
		return PackageManifest.AddFile( contents, relativePath );
	}

	/// <summary>
	/// Manually add a file to the manifest
	/// </summary>
	public Task AddFile( string contents, string relativePath )
	{
		return PackageManifest.AddTextFile( contents, relativePath );
	}

	/// <summary>
	/// If the code is referencing a package - we can add it to the manifest using this.
	/// </summary>
	public Task AddCodePackageReference( string package )
	{
		return PackageManifest.AddCodePackageReference( package );
	}

	private void TriggerProgessChanged( QueuedFile file )
	{
		if ( OnProgressChanged is not null )
		{
			var extension = Path.GetExtension( file.Source );
			var assetType = AssetType.FromExtension( extension );

			var filesTotal = Files.Count;
			var filesDone = Files.Count( x => x.State != QueuedFileState.Waiting );
			var filesLeft = Files.Count( x => x.State == QueuedFileState.Waiting );

			var progressFraction = (float)(filesTotal - filesLeft) / filesTotal;
			var actionText = $"Copying files ({(progressFraction * 100f).CeilToInt()}%)";
			var stepString = file.Step.GetDescription();

			var buildIssues = new string[Files.Count( x => x.State != QueuedFileState.Waiting && x.State != QueuedFileState.Copied )];
			var issueIdx = 0;

			for ( int fileIdx = 0; fileIdx < Files.Count; fileIdx++ )
			{
				var processedFile = Files[fileIdx];

				if ( processedFile.State == QueuedFileState.Waiting )
					continue;

				if ( processedFile.State == QueuedFileState.Copied )
					continue;

				if ( processedFile.State == QueuedFileState.FailedToCopy )
					buildIssues[issueIdx] = $"Failed to copy '{processedFile.Source}' during step '{processedFile.Step.GetDescription()}'";

				if ( processedFile.State == QueuedFileState.FileNotFound )
					buildIssues[issueIdx] = $"File '{processedFile.Source}' not found during step '{processedFile.Step.GetDescription()}'";

				issueIdx++;
			}

			var progress = new ExportProgress()
			{
				ProgressFraction = progressFraction,
				CurrentOperation = stepString,
				FilesDone = filesDone,
				FilesTotal = filesTotal,
				BuildIssues = buildIssues
			};

			UpdateProgress( progress );
		}
	}
}
