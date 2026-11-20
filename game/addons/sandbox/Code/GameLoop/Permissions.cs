/// <summary>
/// Marks a const string as a permission, so it's sent to clients and listed for admins.
/// </summary>
[AttributeUsage( AttributeTargets.Field )]
public sealed class PermissionAttribute : Attribute
{
}

/// <summary>
/// Permission checks that work on clients too. Only the host can ask the engine,
/// so it sends each player the declared permissions they have when they join.
/// </summary>
public sealed class Permissions : GameObjectSystem<Permissions>, Component.INetworkListener
{
	/// <summary>
	/// Admins count as having every permission.
	/// </summary>
	[Permission, Title( "Admin" ), Description( "Full server control. Kick and ban players, cleanup props, change server and gameplay settings, etc.." )]
	public const string Admin = "admin";

	public record Declared( string Name, string Title, string Description );

	static IReadOnlyList<Declared> _declared;
	static Scene _declaredFor;

	public Permissions( Scene scene ) : base( scene )
	{
	}

	void Component.INetworkListener.OnBecameHost( Connection previousHost )
	{
		foreach ( var data in Scene.GetAll<PlayerData>() )
			Refresh( data );
	}

	public static bool Has( Connection connection, string permission )
	{
		if ( connection is null ) return false;
		if ( connection.IsHost ) return true;

		if ( Networking.IsHost )
			return connection.HasPermission( permission ) || connection.HasPermission( Admin );

		// clients only know what the host sent them
		var granted = PlayerData.For( connection )?.Permissions;
		return granted is not null && (granted.Contains( permission ) || granted.Contains( Admin ));
	}

	/// <summary>
	/// True if the setting is on for everyone, or if this player has the permission.
	/// </summary>
	public static bool Allows( Connection connection, bool enabled, string permission ) => enabled || Has( connection, permission );

	/// <summary>
	/// Every permission declared in the gamemode and in addons.
	/// </summary>
	public static IReadOnlyList<Declared> All
	{
		get
		{
			if ( _declared is null || _declaredFor != Game.ActiveScene )
			{
				_declared = FindDeclared();
				_declaredFor = Game.ActiveScene;
			}

			return _declared;
		}
	}

	static IReadOnlyList<Declared> FindDeclared()
	{
		return Game.TypeLibrary.GetTypes()
			.SelectMany( type => type.Fields )
			.Where( field => field.IsStatic && field.FieldType == typeof( string ) && field.HasAttribute<PermissionAttribute>() )
			.Select( field => new Declared( (string)field.GetValue( null ), field.Title, field.Description ) )
			.Where( x => !string.IsNullOrEmpty( x.Name ) )
			.DistinctBy( x => x.Name )
			.ToList();
	}

	/// <summary>
	/// Sends a player their current permissions. Host only.
	/// </summary>
	public static void Refresh( Connection connection )
	{
		if ( PlayerData.For( connection ) is { } data )
			Refresh( data );
	}

	internal static void Refresh( PlayerData data )
	{
		if ( !Networking.IsHost ) return;

		var connection = data.Network.Owner;
		if ( connection is null ) return;

		var granted = All.Where( x => connection.HasPermission( x.Name ) ).Select( x => x.Name ).ToList();
		if ( granted.SequenceEqual( data.Permissions ) ) return;

		data.Permissions.Clear();
		foreach ( var name in granted )
			data.Permissions.Add( name );
	}
}
