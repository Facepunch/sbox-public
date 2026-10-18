using System.ComponentModel;
using System.Runtime.CompilerServices;
using Sandbox.Network;

namespace Sandbox;

public abstract partial class GameObjectSystem : IDeltaSnapshot
{
	private readonly Dictionary<string, IInterpolatedSyncVar> InterpolatedVars = new();
	private NetworkTable dataTable = new();

	private Guid _id;
	public Guid Id
	{
		get => _id;
		internal set
		{
			if ( _id == value ) return;

			var oldId = _id;
			_id = value;

			Scene?.Directory?.Add( this, oldId );
			CreateDataTable();
		}
	}

	bool IDeltaSnapshot.IsProxy => !Networking.IsHost;
	bool IDeltaSnapshot.ShouldTransmit( Connection target ) => true;
	bool IDeltaSnapshot.UpdateTransmitState( Connection[] targets ) => true;
	ushort IDeltaSnapshot.SnapshotVersion => 0;

	/// <summary>
	/// Should only be called by <see cref="GameObjectDirectory.Add( GameObjectSystem )"/>.
	/// </summary>
	internal void ForceChangeId( Guid guid )
	{
		_id = guid;
	}

	[EditorBrowsable( EditorBrowsableState.Never )]
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	protected void __rpc_Wrapper<T>( in WrappedMethod m, T[] argument )
	{
		Rpc.OnCallInstanceRpc( this, m, [argument] );
	}

	[EditorBrowsable( EditorBrowsableState.Never )]
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	protected void __rpc_Wrapper( in WrappedMethod m, params object[] argumentList )
	{
		Rpc.OnCallInstanceRpc( this, m, argumentList );
	}

	// Generated RPC wrappers bind to these before the object array overload, so arguments aren't boxed

	[EditorBrowsable( EditorBrowsableState.Never )]
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	protected void __rpc_Wrapper( in WrappedMethod m ) => Rpc.OnCallInstanceRpc( this, m, new RpcArgs() );

	[EditorBrowsable( EditorBrowsableState.Never )]
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	protected void __rpc_Wrapper<T1>( in WrappedMethod m, T1 a1 ) => Rpc.OnCallInstanceRpc( this, m, new RpcArgs<T1>( a1 ) );

	[EditorBrowsable( EditorBrowsableState.Never )]
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	protected void __rpc_Wrapper<T1, T2>( in WrappedMethod m, T1 a1, T2 a2 ) => Rpc.OnCallInstanceRpc( this, m, new RpcArgs<T1, T2>( a1, a2 ) );

	[EditorBrowsable( EditorBrowsableState.Never )]
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	protected void __rpc_Wrapper<T1, T2, T3>( in WrappedMethod m, T1 a1, T2 a2, T3 a3 ) => Rpc.OnCallInstanceRpc( this, m, new RpcArgs<T1, T2, T3>( a1, a2, a3 ) );

	[EditorBrowsable( EditorBrowsableState.Never )]
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	protected void __rpc_Wrapper<T1, T2, T3, T4>( in WrappedMethod m, T1 a1, T2 a2, T3 a3, T4 a4 ) => Rpc.OnCallInstanceRpc( this, m, new RpcArgs<T1, T2, T3, T4>( a1, a2, a3, a4 ) );

	[EditorBrowsable( EditorBrowsableState.Never )]
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	protected void __rpc_Wrapper<T1, T2, T3, T4, T5>( in WrappedMethod m, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5 ) => Rpc.OnCallInstanceRpc( this, m, new RpcArgs<T1, T2, T3, T4, T5>( a1, a2, a3, a4, a5 ) );

	[EditorBrowsable( EditorBrowsableState.Never )]
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	protected void __rpc_Wrapper<T1, T2, T3, T4, T5, T6>( in WrappedMethod m, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6 ) => Rpc.OnCallInstanceRpc( this, m, new RpcArgs<T1, T2, T3, T4, T5, T6>( a1, a2, a3, a4, a5, a6 ) );

	[EditorBrowsable( EditorBrowsableState.Never )]
	protected void __sync_SetValue<T>( in WrappedPropertySet<T> p )
	{
		try
		{
			// If we aren't valid then just set the property value anyway.
			if ( !Scene.IsValid() )
			{
				p.Setter?.Invoke( p.Value );
				return;
			}

			var slot = NetworkObject.GetPropertySlot( p.MemberIdent, Id );

			if ( !dataTable.IsRegistered( slot ) )
			{
				p.Setter?.Invoke( p.Value );
				return;
			}

			if ( !dataTable.HasControl( slot ) )
			{
				if ( !NetworkTable.IsReadingChanges )
					return;

				var attribute = p.GetAttribute<SyncAttribute>();
				var interpolate = attribute is not null && (attribute.Flags & SyncFlags.Interpolate) != 0;

				if ( interpolate && p.Value is not null )
				{
					var interpolated = GetOrCreateInterpolatedVar( p.Value, p.PropertyName );
					interpolated?.Update( p.Value );
				}

				p.Setter?.Invoke( p.Value );
				return;
			}

			dataTable.UpdateSlotHash( slot, p.Value );
			p.Setter?.Invoke( p.Value );
		}
		catch ( Exception e )
		{
			Log.Error( e, $"Exception when setting {p.TypeName}.{p.PropertyName} - {e.Message}" );
		}
	}

	[EditorBrowsable( EditorBrowsableState.Never )]
	protected T __sync_GetValue<T>( WrappedPropertyGet<T> p )
	{
		var attribute = p.GetAttribute<SyncAttribute>();
		var interpolate = attribute is not null && (attribute.Flags & SyncFlags.Interpolate) != 0;
		if ( !interpolate ) return p.Value;

		var slot = NetworkObject.GetPropertySlot( p.MemberIdent, Id );

		if ( !dataTable.IsRegistered( slot ) || dataTable.HasControl( slot ) )
			return p.Value;

		if ( InterpolatedVars.TryGetValue( p.PropertyName, out var i ) )
			return (T)i.Query( Time.NowDouble );

		return p.Value;
	}

	/// <summary>
	/// Get or create a new interpolated variable. This will set the current interpolated value to the
	/// provided one if it hasn't been created yet.
	/// </summary>
	private InterpolatedSyncVar<T> GetOrCreateInterpolatedVar<T>( T value, string propertyName )
	{
		if ( InterpolatedVars.TryGetValue( propertyName, out var i ) )
			return (InterpolatedSyncVar<T>)i;

		var interpolator = IInterpolatedSyncVar.Create( value );

		if ( interpolator is null )
			return null;

		var interpolated = new InterpolatedSyncVar<T>( interpolator );
		InterpolatedVars[propertyName] = interpolated;

		return interpolated;
	}

	private void RegisterSyncVarProperties()
	{
		var type = GetType();

		// Register all our Sync properties with the data table.
		foreach ( var propertyAndAttribute in ReflectionQueryCache.SyncProperties( type ) )
		{
			var isQuery = propertyAndAttribute.Attribute.Flags.HasFlag( SyncFlags.Query );

			try
			{
				var identity = NetworkObject.GetPropertySlot( propertyAndAttribute.NameHash, Id );

				var entry = new NetworkTable.Entry
				{
					TargetType = propertyAndAttribute.Property.PropertyType,
					ControlCondition = c => c.IsHost,
					GetValue = () => propertyAndAttribute.Property.GetValue( this ),
					SetValue = v => propertyAndAttribute.Property?.SetValue( this, v ),
					Typed = NetworkTable.TypedAccess.Create( this, propertyAndAttribute.Property ),
					NeedsQuery = isQuery,
					DebugName = propertyAndAttribute.DebugName
				};

				dataTable.Register( identity, entry );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Got exception when creating network table (reading {this}.{propertyAndAttribute.Property.Name}) - {e.Message}" );
			}
		}
	}

	private void CreateDataTable()
	{
		dataTable?.Dispose();
		dataTable = new();

		RegisterSyncVarProperties();
	}

	internal readonly LocalSnapshotState LocalSnapshotState = new();

	LocalSnapshotState IDeltaSnapshot.WriteSnapshotState()
	{
		var system = SceneNetworkSystem.Instance;
		if ( system is null ) return null;

		LocalSnapshotState.Begin();
		LocalSnapshotState.SnapshotId = system.DeltaSnapshots.CreateSnapshotId( Id );
		LocalSnapshotState.ObjectId = Id;

		dataTable.QueryValues();
		dataTable.WriteSnapshotState( LocalSnapshotState );

		return LocalSnapshotState;
	}

	/// <summary>
	/// Try to send a network update or do nothing if no update is required. This is most
	/// likely called after WriteDeltaSnapshot.
	/// </summary>
	void IDeltaSnapshot.SendNetworkUpdate( bool queryValues )
	{
		if ( queryValues )
			dataTable.QueryValues( true );

		if ( !dataTable.HasReliableChanges() )
			return;

		var msg = new SceneNetworkTableMsg { Guid = Id };
		var data = ByteStream.Create( 4096 );

		dataTable.WriteReliableChanged( ref data );
		msg.TableData = data.ToArray();

		data.Dispose();

		SceneNetworkSystem.Instance.Broadcast( msg );
	}

	/// <summary>
	/// Write all pending data table changes.
	/// </summary>
	internal byte[] WriteDataTable( bool full )
	{
		if ( !dataTable.HasAnyChanges && !full )
			return null;

		var data = ByteStream.Create( 32 );

		if ( full )
			dataTable.WriteAll( ref data );
		else
			dataTable.WriteChanged( ref data );

		var bytes = data.ToArray();
		data.Dispose();

		return bytes;
	}

	/// <summary>
	/// Read the network table data.
	/// </summary>
	internal void ReadDataTable( byte[] data, NetworkTable.ReadFilter filter = null )
	{
		if ( data is null ) return;

		var reader = ByteStream.CreateReader( data );
		dataTable.Read( ref reader, filter );
		reader.Dispose();
	}

	bool IDeltaSnapshot.OnSnapshot( Connection source, DeltaSnapshot snapshot )
	{
		// Don't process this if the source connection is not the host.
		if ( !source.IsHost )
			return false;

		dataTable.ReadSnapshot( source, snapshot );
		return true;
	}

	internal void OnHotload()
	{
		CreateDataTable();
	}
}
