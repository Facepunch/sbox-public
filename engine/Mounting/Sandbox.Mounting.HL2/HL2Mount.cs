using System;
using ValvePak;

/// <summary>
/// A mounting implementation for Half-Life 2 and Episodes + Lost Coast
/// </summary>
public partial class HL2Mount : BaseGameMount
{
	public override string Ident => "hl2";
	public override string Title => "Half-Life 2";

	private const long HL2AppId = 220;

	private readonly List<VpkArchive> _packages = [];
	private readonly List<string> _gameDirs = [];

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( HL2AppId ) )
			return;

		var baseDir = context.GetAppDirectory( HL2AppId );
		if ( string.IsNullOrEmpty( baseDir ) || !System.IO.Directory.Exists( baseDir ) )
			return;

		AddGameDir( baseDir, "hl2" );
		AddGameDir( baseDir, "episodic" );
		AddGameDir( baseDir, "ep2" );
		AddGameDir( baseDir, "lostcoast" );
		AddGameDir( baseDir, "hl2_complete" );

		IsInstalled = _gameDirs.Count > 0;
	}

	private void AddGameDir( string baseDir, string subDir )
	{
		var path = Path.Combine( baseDir, subDir );
		if ( System.IO.Directory.Exists( path ) )
			_gameDirs.Add( path );
	}

	protected override Task Mount( MountContext context )
	{
		if ( !IsInstalled )
			return Task.CompletedTask;

		foreach ( var gameDir in _gameDirs )
			MountGameDirectory( context, gameDir );

		IsMounted = true;
		return Task.CompletedTask;
	}

	private void MountGameDirectory( MountContext context, string gameDir )
	{
		foreach ( var vpkPath in System.IO.Directory.EnumerateFiles( gameDir, "*_dir.vpk" ) )
		{
			var package = new VpkArchive();
			package.Read( vpkPath );
			_packages.Add( package );
			MountVpkContents( context, package );
		}

		MountLooseFiles( context, gameDir );
	}

	private void MountVpkContents( MountContext context, VpkArchive package )
	{
		foreach ( var (extension, entries) in package.Entries )
		{
			var ext = extension.ToLowerInvariant();

			foreach ( var entry in entries )
			{
				var fullPath = entry.GetFullPath();
				var path = fullPath[..^(ext.Length + 1)];

				switch ( ext )
				{
					case "vtf":
						context.Add( ResourceType.Texture, path, new HL2Texture( package, entry ) );
						break;
					case "vmt":
						context.Add( ResourceType.Material, path, new HL2Material( package, entry ) );
						break;
					case "mdl":
						context.Add( ResourceType.Model, path, new HL2Model( package, entry ) );
						break;
					case "wav":
					case "mp3":
						context.Add( ResourceType.Sound, path, new HL2Sound( package, entry ) );
						break;
				}
			}
		}
	}

	private void MountLooseFiles( MountContext context, string gameDir )
	{
		MountLooseFilesOfType( context, gameDir, "materials", "*.vtf", ResourceType.Texture,
			filePath => new HL2Texture( filePath ) );
		MountLooseFilesOfType( context, gameDir, "materials", "*.vmt", ResourceType.Material,
			filePath => new HL2Material( filePath ) );
		MountLooseFilesOfType( context, gameDir, "models", "*.mdl", ResourceType.Model,
			filePath => new HL2Model( filePath ) );
		MountLooseFilesOfType( context, gameDir, "sound", "*.wav", ResourceType.Sound,
			filePath => new HL2Sound( filePath ) );
		MountLooseFilesOfType( context, gameDir, "sound", "*.mp3", ResourceType.Sound,
			filePath => new HL2Sound( filePath ) );
	}

	private void MountLooseFilesOfType( MountContext context, string gameDir, string subDir, string pattern,
		ResourceType resourceType, Func<string, ResourceLoader<HL2Mount>> createLoader )
	{
		var dir = Path.Combine( gameDir, subDir );
		if ( !System.IO.Directory.Exists( dir ) )
			return;

		foreach ( var filePath in System.IO.Directory.EnumerateFiles( dir, pattern, SearchOption.AllDirectories ) )
		{
			var relativePath = Path.GetRelativePath( gameDir, filePath ).Replace( '\\', '/' );
			var resourcePath = relativePath[..^Path.GetExtension( relativePath ).Length];
			context.Add( resourceType, resourcePath, createLoader( filePath ) );
		}
	}

	internal bool FileExists( string path )
	{
		path = path.ToLowerInvariant().Replace( '\\', '/' );

		foreach ( var package in _packages )
		{
			if ( package.FindEntry( path ) != null )
				return true;
		}

		foreach ( var dir in _gameDirs )
		{
			var fullPath = Path.Combine( dir, path );
			if ( File.Exists( fullPath ) )
				return true;
		}

		return false;
	}

	internal byte[] ReadFile( string path )
	{
		path = path.ToLowerInvariant().Replace( '\\', '/' );

		foreach ( var package in _packages )
		{
			var entry = package.FindEntry( path );
			if ( entry != null )
			{
				package.ReadEntry( entry, out var data );
				return data;
			}
		}

		foreach ( var dir in _gameDirs )
		{
			var fullPath = Path.Combine( dir, path );
			if ( File.Exists( fullPath ) )
				return File.ReadAllBytes( fullPath );
		}

		return null;
	}

	protected override void Shutdown()
	{
		foreach ( var package in _packages )
			package.Dispose();

		_packages.Clear();
		_gameDirs.Clear();
	}
}
