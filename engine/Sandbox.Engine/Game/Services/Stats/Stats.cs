using System.Runtime.CompilerServices;
using System.Threading;

namespace Sandbox.Services;

/// <summary>
/// Allows access to stats for the current game. Stats are defined by the game's author
/// and can be used to track anything from player actions to performance metrics. They are
/// how you submit data to leaderboards.
/// </summary>
public static partial class Stats
{
	/// <summary>
	/// Send any pending stats to the backend. Don't wait for confirmation of ingestiom, fire and forget.
	/// </summary>
	[MethodImpl( MethodImplOptions.NoInlining )]
	public static Task FlushAsync( CancellationToken token = default )
	{
		var package = Application.GameIdent;
		if ( package is null )
			return Task.CompletedTask;

		return Api.Stats.FlushAsync( package, token );
	}

	/// <summary>
	/// Send any pending stats to the backend. Don't wait for confirmation of ingestiom, fire and forget.
	/// </summary>
	[MethodImpl( MethodImplOptions.NoInlining )]
	public static void Flush()
	{
		var package = Application.GameIdent;
		if ( package is null )
			return;

		_ = Api.Stats.FlushAsync( package, default );
	}

	/// <summary>
	/// Send any pending stats to the backend, will wait until they're available for query before finishing.
	/// </summary>
	[MethodImpl( MethodImplOptions.NoInlining )]
	public static Task FlushAndWaitAsync( CancellationToken token = default )
	{
		var package = Application.GameIdent;
		if ( package is null )
			return Task.CompletedTask;

		return Api.Stats.FlushAsync( package, token );
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	public static void Increment( string name, double amount )
	{
		var package = Application.GameIdent;
		if ( package is null ) return;

		Api.Stats.AddIncrement( package, name, amount, null );

		var localStats = Stats.GetLocalPlayerStats( package );
		localStats?.Predict( name, amount );
	}

	[MethodImpl( MethodImplOptions.NoInlining ), Obsolete]
	public static void Increment( string name, double amount, string context, object data = default ) => Increment( name, amount );

	[MethodImpl( MethodImplOptions.NoInlining )]
	public static void Increment( string name, double amount, Dictionary<string, object> data )
	{
		var package = Application.GameIdent;
		if ( package is null ) return;

		Api.Stats.AddIncrement( package, name, amount, GetObjectDictionary( data ) );

		var localStats = Stats.GetLocalPlayerStats( package );
		localStats?.Predict( name, amount );
	}

	private static Dictionary<string, object> GetObjectDictionary( object data )
	{
		if ( data is null )
			return null;

		if ( data is Dictionary<string, object> o )
		{
			return o;
		}

		try
		{
			var json = System.Text.Json.JsonSerializer.Serialize( data );
			return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>( json );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Couldn't encode stat data ({e.Message})" );
			return null;
		}
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	public static void SetValue( string name, double amount, string context = null, object data = null )
	{
		var package = Application.GameIdent;
		if ( package is null ) return;

		Api.Stats.SetValue( package, name, amount, GetObjectDictionary( data ) );

		var localStats = Stats.GetLocalPlayerStats( package );
		localStats?.Predict( name, amount );
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	public static void SetValue( string name, double amount, Dictionary<string, object> data )
	{
		var package = Application.GameIdent;
		if ( package is null ) return;

		Api.Stats.SetValue( package, name, amount, GetObjectDictionary( data ) );

		var localStats = Stats.GetLocalPlayerStats( package );
		localStats?.Predict( name, amount );
	}

	internal static void RPC_StatsIncrement( string package, string name, double amount, string data )
	{
		Dictionary<string, object> oData = null;

		if ( !string.IsNullOrWhiteSpace( data ) )
		{
			oData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>( data );
		}

		Api.Stats.AddIncrement( package, name, amount, oData );
	}

	internal static void RPC_StatsSetValue( string package, string name, double amount, string data )
	{
		Dictionary<string, object> oData = null;

		if ( !string.IsNullOrWhiteSpace( data ) )
		{
			oData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>( data );
		}

		Api.Stats.SetValue( package, name, amount, oData );
	}
}

