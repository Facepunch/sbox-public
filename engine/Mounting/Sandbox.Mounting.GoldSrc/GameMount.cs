/// <summary>
/// A mounting implementation for Half-Life 1
/// </summary>
public abstract class GameMount : BaseGameMount
{
	public abstract long AppId { get; }

	public abstract IReadOnlyList<string> GameDirs { get; }

	string appDir;

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( AppId ) )
			return;

		appDir = context.GetAppDirectory( AppId );
		IsInstalled = true;
	}

	protected override Task Mount( MountContext context )
	{
		if ( string.IsNullOrEmpty( appDir ) || GameDirs is null || GameDirs.Count == 0 )
			return Task.CompletedTask;

		foreach ( var dir in GameDirs )
		{
			var root = Path.Combine( appDir, dir );
			if ( !System.IO.Directory.Exists( root ) )
				continue;

			foreach ( var fullPath in System.IO.Directory.GetFiles( root, "*.*", SearchOption.AllDirectories ) )
			{
				var ext = Path.GetExtension( fullPath )?.ToLowerInvariant();
				if ( string.IsNullOrEmpty( ext ) )
					continue;

				var path = Path.GetRelativePath( appDir, fullPath ).Replace( '\\', '/' );

				if ( ext == ".wad" )
				{
					try
					{
						var wad = new Wad();
						wad.LoadWadFile( fullPath );
						var wadName = Path.GetFileNameWithoutExtension( path );

						foreach ( var lump in wad.Lumps )
						{
							if ( lump.Type != 67 ) continue;

							var texture = new WadTextureLoader( wad, lump.Name );
							context.Add( ResourceType.Texture, $"{dir}/textures/{wadName}/{lump.Name}", texture );
							context.Add( ResourceType.Material, $"{dir}/materials/{wadName}/{lump.Name}", new MaterialLoader( texture.Path ) );
						}
					}
					catch ( System.Exception ex )
					{
						Log.Warning( $"Failed to load WAD {fullPath}: {ex.Message}" );
					}

					continue;
				}

				if ( ext == ".mdl" )
				{
					using var stream = new FileStream( fullPath, FileMode.Open, FileAccess.Read );
					using var reader = new BinaryReader( stream );

					if ( reader.ReadInt32() != 0x54534449 || reader.ReadInt32() != 10 )
						continue;

					stream.Seek( 204, SeekOrigin.Begin );
					if ( reader.ReadInt32() <= 0 )
						continue;

					context.Add( ResourceType.Model, path, new ModelLoader( fullPath ) );
				}
				else if ( ext == ".wav" )
				{
					context.Add( ResourceType.Sound, path, new WavSoundLoader( fullPath ) );
				}
			}
		}

		IsMounted = true;
		return Task.CompletedTask;
	}
}

public class HalfLifeMount : GameMount
{
	public override string Ident => "hl1";
	public override string Title => "Half-Life";
	public override long AppId => 70;
	public override IReadOnlyList<string> GameDirs => ["valve_hd", "valve"];
}

public class OpposingForceMount : GameMount
{
	public override string Ident => "opfor";
	public override string Title => "Half-Life: Opposing Force";
	public override long AppId => 50;
	public override IReadOnlyList<string> GameDirs => ["gearbox_hd", "gearbox"];
}

public class BlueShiftMount : GameMount
{
	public override string Ident => "bshift";
	public override string Title => "Half-Life: Blue Shift";
	public override long AppId => 130;
	public override IReadOnlyList<string> GameDirs => ["bshift_hd", "bshift"];
}

public class CounterStrikeMount : GameMount
{
	public override string Ident => "cstrike";
	public override string Title => "Counter-Strike";
	public override long AppId => 10;
	public override IReadOnlyList<string> GameDirs => ["cstrike_hd", "cstrike"];
}

public class ConditionZeroMount : GameMount
{
	public override string Ident => "czero";
	public override string Title => "Counter-Strike: Condition Zero";
	public override long AppId => 80;
	public override IReadOnlyList<string> GameDirs => ["czero_hd", "czero"];
}

public class RicochetMount : GameMount
{
	public override string Ident => "ricochet";
	public override string Title => "Ricochet";
	public override long AppId => 60;
	public override IReadOnlyList<string> GameDirs => ["ricochet_hd", "ricochet"];
}

public class TeamFortressClassicMount : GameMount
{
	public override string Ident => "tfc";
	public override string Title => "Team Fortress Classic";
	public override long AppId => 20;
	public override IReadOnlyList<string> GameDirs => ["tfc_hd", "tfc"];
}

public class DeathmatchClassicMount : GameMount
{
	public override string Ident => "dmc";
	public override string Title => "Deathmatch Classic";
	public override long AppId => 40;
	public override IReadOnlyList<string> GameDirs => ["dmc_hd", "dmc"];
}

public class DayOfDefeatMount : GameMount
{
	public override string Ident => "dod";
	public override string Title => "Day of Defeat";
	public override long AppId => 30;
	public override IReadOnlyList<string> GameDirs => ["dod_hd", "dod"];
}

public class SvenCoopMount : GameMount
{
	public override string Ident => "svencoop";
	public override string Title => "Sven Co-op";
	public override long AppId => 225840;
	public override IReadOnlyList<string> GameDirs => ["svencoop"];
}

public class CryOfFearMount : GameMount
{
	public override string Ident => "cof";
	public override string Title => "Cry of Fear";
	public override long AppId => 223710;
	public override IReadOnlyList<string> GameDirs => ["cryoffear"];
}
