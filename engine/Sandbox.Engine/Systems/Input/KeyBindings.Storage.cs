using NativeEngine;
using System.IO;

namespace Sandbox.Engine;

internal static partial class KeyBindings
{
	static Storage _storage;

	internal enum LoadResult { Missing, Loaded, Migrated, Failed }

	internal static uint ResolveAccountId( bool hasSteamUser, ulong steamId ) => hasSteamUser ? (uint)steamId : 0;

	static Storage CreateStorage() => new( EngineFileSystem.Root, ResolveAccountId( Steam.SteamUser().IsValid, Game.SteamId.ValueUnsigned ) );

	internal static bool Initialize( bool enabled, bool developer )
	{
		Reset();
		return !enabled ||
			(developer && LoadDefaultsFile( "core/cfg/user_keys_dev_default.vcfg" )) ||
			LoadDefaultsFile( "core/cfg/user_keys_default.vcfg" );
	}

	static bool LoadDefaultsFile( string path )
	{
		try
		{
			var text = EngineFileSystem.Root.ReadAllText( path );
			return text is not null && LoadLegacyDefaults( text );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			Log.Warning( $"Failed to load default key bindings '{path}': {e.Message}" );
			return false;
		}
	}

	internal static bool LoadLegacyDefaults( string text )
	{
		var json = EngineGlue.KeyValuesToJson( text );
		if ( json is null ) return false;
		LoadDefaults( json );
		return true;
	}

	internal static bool ReadLegacy( string text )
	{
		var json = EngineGlue.KeyValuesToJson( text );
		return json is not null && Read( json );
	}

	internal static LoadResult Load() => Application.IsUnitTest ? LoadResult.Missing : (_storage ??= CreateStorage()).Initialize();
	internal static bool Save() => Application.IsUnitTest || Application.IsDedicatedServer || (_storage?.Save() ?? false);

	/// <summary>Persistence in the configuration folder, with an explicit filesystem and account.</summary>
	internal sealed class Storage( BaseFileSystem files, uint account )
	{
		bool _canSave;
		string LocalFile( string extension, bool offline = false ) => $"core/cfg/user_keys_{(offline ? 0 : account)}{extension}";

		/// <summary>Use defaults when necessary and write at startup only when converting legacy bindings.</summary>
		internal LoadResult Initialize()
		{
			var result = Load();
			if ( result is LoadResult.Missing or LoadResult.Failed ) ResetToDefaults();
			if ( result == LoadResult.Migrated ) Save();
			return result;
		}

		internal LoadResult Load()
		{
			_canSave = false;
			try
			{
				// Prefer account-local files, then fall back to offline copies.
				var json = files.ReadAllText( LocalFile( ".json" ) );
				var legacy = json is null ? files.ReadAllText( LocalFile( ".vcfg" ) ) : null;
				if ( json is null && legacy is null && account != 0 )
				{
					json = files.ReadAllText( LocalFile( ".json", true ) );
					if ( json is null ) legacy = files.ReadAllText( LocalFile( ".vcfg", true ) );
				}
				if ( json is null )
				{
					if ( legacy is null )
					{
						_canSave = true;
						return LoadResult.Missing;
					}
					if ( !ReadLegacy( legacy ) ) return LoadResult.Failed;
				}
				else if ( !Read( json ) ) return LoadResult.Failed;
				_canSave = true;

				// Keep legacy copies for older releases and other machines during the migration period.
				return legacy is not null ? LoadResult.Migrated : LoadResult.Loaded;
			}
			catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
			{
				Log.Warning( $"Failed to load or migrate key bindings: {e.Message}" );
				_canSave = false;
				return LoadResult.Failed;
			}
		}

		/// <summary>Save atomically in the configuration folder; Steam handles folder synchronization.</summary>
		internal bool Save()
		{
			if ( !_canSave ) return false;
			try
			{
				var json = Write();
				files.WriteAllTextAtomic( LocalFile( ".json" ), json );
				if ( account != 0 ) files.WriteAllTextAtomic( LocalFile( ".json", true ), json );

				return true;
			}
			catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
			{
				Log.Warning( $"Failed to save key bindings: {e.Message}" );
				return false;
			}
		}
	}
}
