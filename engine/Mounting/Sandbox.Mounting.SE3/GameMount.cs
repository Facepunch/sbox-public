using System;
using System.IO.Compression;

namespace SeriousEngine3;

public abstract partial class GameMount : BaseGameMount
{
	protected abstract long AppId { get; }
	protected abstract string GroPath { get; }

	string appDir;

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( AppId ) ) return;
		appDir = context.GetAppDirectory( AppId );
		IsInstalled = System.IO.Directory.Exists( appDir );
		return;
	}

	ZipArchive zip;

	public byte[] GetBytes( string fullName )
	{
		var e = zip.GetEntry( fullName ) ?? throw new FileNotFoundException( $"Entry not found: {fullName}" );
		var bytes = GC.AllocateUninitializedArray<byte>( checked((int)e.Length) );
		using ( var s = e.Open() ) s.ReadExactly( bytes );
		return bytes;
	}

	protected override void Shutdown()
	{
		zip?.Dispose();
		zip = null;
		base.Shutdown();
	}

	protected override Task Mount( MountContext context )
	{
		if ( string.IsNullOrWhiteSpace( appDir ) ) return Task.CompletedTask;

		zip = new ZipArchive( new FileStream( Path.Combine( appDir, GroPath ), FileMode.Open, FileAccess.Read, FileShare.Read ), ZipArchiveMode.Read );

		foreach ( var a in zip.Entries )
		{
			var ext = Path.GetExtension( a.Name );

			if ( ext == ".mdl" )
			{
				context.Add( ResourceType.Model, a.FullName, new ModelLoader( a.FullName ) );
			}
			else if ( ext == ".wav" )
			{
				context.Add( ResourceType.Sound, a.FullName, new SoundLoader( a.FullName ) );
			}
			else if ( ext == ".tex" )
			{
				context.Add( ResourceType.Texture, a.FullName, new TextureLoader( a.FullName ) );
			}
			else if ( ext == ".wld" )
			{
				context.Add( ResourceType.Scene, a.FullName, new WorldLoader( a.FullName ) );
			}
		}

		IsMounted = true;
		return Task.CompletedTask;
	}
}

public sealed class FirstEncounterMount : GameMount
{
	public override string Ident => "ss_hd_tfe";
	public override string Title => "Serious Sam HD: The First Encounter";
	protected override long AppId => 41000;
	protected override string GroPath => "Content/SeriousSamHD/All.gro";
}

public sealed class SecondEncounterMount : GameMount
{
	public override string Ident => "ss_hd_tse";
	public override string Title => "Serious Sam HD: The Second Encounter";
	protected override long AppId => 41010;
	protected override string GroPath => "Content/SeriousSamHD_TSE/All.gro";
}
