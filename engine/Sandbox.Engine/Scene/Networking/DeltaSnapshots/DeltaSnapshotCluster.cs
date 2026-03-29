using Sandbox.Network;

internal class DeltaSnapshotCluster : IObjectPoolEvent
{
	public static NetworkObjectPool<DeltaSnapshotCluster> Pool { get; } = new();

	private int ReferenceCount { get; set; }

	/// <summary>
	/// Add to reference count for this object.
	/// </summary>
	public void AddReference()
	{
		ReferenceCount++;
	}

	/// <summary>
	/// Release a reference for this object, and return it to the pool
	/// if nothing else is referencing it.
	/// </summary>
	public void Release()
	{
		if ( ReferenceCount == 0 )
			throw new InvalidOperationException( "ReferenceCount is already zero" );

		ReferenceCount--;

		if ( ReferenceCount <= 0 )
		{
			Pool.Return( this );
		}
	}

	void IObjectPoolEvent.OnRented()
	{
		TimeSinceCreated = 0f;
		ReferenceCount = 1;
		Id = ++s_nextAvailableId;
	}

	void IObjectPoolEvent.OnReturned()
	{
		foreach ( var snapshot in Snapshots )
		{
			snapshot.Release();
		}

		Snapshots.Clear();
		Size = 0;
	}

	/// <summary>
	/// The maximum size (in bytes) of a single snapshot cluster. Since clusters are generally sent unreliably,
	/// the maximum size should really be under the typical MTU size of 1500 bytes.
	/// </summary>
	[ConVar( "net_max_cluster_size" )]
	public static int MaxSize { get; set; } = 1200;

	private static ushort s_nextAvailableId;

	public List<DeltaSnapshot> Snapshots { get; init; } = new();
	public RealTimeSince TimeSinceCreated { get; private set; } = 0f;
	public int Size { get; private set; }
	public ushort Id { get; private set; }

	public DeltaSnapshotCluster()
	{
		Id = ++s_nextAvailableId;
	}

	/// <summary>
	/// Add a <see cref="DeltaSnapshot"/> to the cluster. It will be ignored if the snapshot is empty.
	/// </summary>
	/// <param name="snapshot"></param>
	public bool Add( DeltaSnapshot snapshot )
	{
		if ( snapshot.Size == 0 )
			return false;

		snapshot.AddReference();

		Snapshots.Add( snapshot );
		Size += snapshot.Size;

		return true;
	}
}
