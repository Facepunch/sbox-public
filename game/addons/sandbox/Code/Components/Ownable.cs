using System.Text.Json.Serialization;

/// <summary>
/// Tracks which connection spawned this object
/// </summary>
public sealed class Ownable : Component
{
	[Sync( SyncFlags.FromHost )]
	private Guid _ownerId { get; set; }

	/// <summary>
	/// I would fucking love to be able to Sync these..
	/// And it would just do this exact behaviour. Why not?
	/// </summary>
	[Property, ReadOnly, JsonIgnore]
	public Connection Owner
	{
		get => Connection.All.FirstOrDefault( c => c.Id == _ownerId );
		internal set => _ownerId = value?.Id ?? Guid.Empty;
	}

	/// <summary>
	/// Sets a gameObject as owned by a connection. If the gameObject already has an Ownable component, it'll just update the owner.
	/// </summary>
	/// <param name="go"></param>
	/// <param name="owner"></param>
	/// <returns></returns>
	public static Ownable Set( GameObject go, Connection owner )
	{
		var ownable = go.GetOrAddComponent<Ownable>();
		ownable.Owner = owner;
		return ownable;
	}
}
