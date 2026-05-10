using Sandbox.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Sandbox.Mounting;


/// <summary>
/// Holds all of the AssetSource systems and acts as a single access point.
/// </summary>
internal class MountHost : IDisposable
{
	Logger Log = new Logger( "MountingSystem" );

	Dictionary<string, BaseGameMount> Sources = new();
	HashSet<string> PendingMounts = new();

	public ISteamIntegration Steam { get; init; }

	public IReadOnlyCollection<BaseGameMount> All => Sources.Values;

	public MountHost( Configuration config )
	{
		Steam = config.SteamIntegration;
	}

	public void Dispose()
	{
		var sources = All.ToArray();

		foreach ( var e in sources )
		{
			if ( e is null ) continue;
			e.ShutdownInternal();
		}

		Sources.Clear();
	}

	public void Initialize( Type targetType )
	{
		try
		{
			var t = (BaseGameMount)Activator.CreateInstance( targetType );

			if ( Sources.ContainsKey( t.Ident ) )
				throw new System.Exception( "Already exists" );

			t.InitializeInternal( this );

			Sources[t.Ident] = t;

			if ( PendingMounts.Remove( t.Ident ) )
			{
				_ = t.MountInternal();
			}
		}
		catch ( Exception e )
		{
			Log.Error( $"Failed to initialize {targetType.Name}: {e}" );
		}
	}

	/// <summary>
	/// Get an asset source by its ident
	/// </summary>
	public BaseGameMount GetSource( string title )
	{
		return Sources.GetValueOrDefault( title );
	}

	/// <summary>
	/// Mount this asset source
	/// </summary>
	public async Task Mount( string title )
	{
		var source = GetSource( title );
		if ( source is null || source.IsMounted )
			return;

		await source.MountInternal();
	}

	/// <summary>
	/// Mount this asset source
	/// </summary>
	public void Unmount( string title )
	{
		var source = GetSource( title );
		if ( source is null || !source.IsMounted )
			return;

		source.ShutdownInternal();
	}

	internal void RegisterTypes( Assembly assembly )
	{
		var assetSourceType = typeof( BaseGameMount );
		var types = assembly.GetTypes().Where( t => assetSourceType.IsAssignableFrom( t ) && !t.IsAbstract );

		foreach ( var type in types )
		{
			Initialize( type );
		}

		PendingMounts.Clear();
	}

	internal void UnregisterTypes( Assembly assembly )
	{
		foreach ( var (ident, source) in Sources.ToArray() )
		{
			if ( source.GetType().Assembly != assembly ) continue;

			if ( source.IsMounted )
			{
				PendingMounts.Add( ident );
			}

			source.ShutdownInternal();
			Sources.Remove( ident );
		}
	}
}
