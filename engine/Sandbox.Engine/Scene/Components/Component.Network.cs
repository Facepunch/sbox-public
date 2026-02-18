using System.ComponentModel;
using System.Runtime.CompilerServices;
using Sandbox.Network;

namespace Sandbox;

public abstract partial class Component
{
	[ActionGraphInclude, Icon( "wifi" )]
	public GameObject.NetworkAccessor Network => GameObject.Network;

	private readonly Dictionary<string, IInterpolatedSyncVar> InterpolatedVars = new();

	/// <summary>
	/// True if this is a networked object and is owned by another client. This means that we're
	/// not controlling this object, so shouldn't try to move it or anything.
	/// </summary>
	public bool IsProxy => GameObject.IsProxy;

	[EditorBrowsable( EditorBrowsableState.Never )]
	protected void __sync_SetValue<T>( in WrappedPropertySet<T> p )
	{
		try
		{
			// If we aren't valid, then just set the property value anyway.
			if ( !IsValid )
			{
				p.Setter?.Invoke( p.Value );
				return;
			}

			// If it's the same value, just call the original setter because
			// we don't want to do all the logic below for the same value.
			// Obviously, if we're reading changes from the network, then we
			// should just allow all the logic to go through.
			if ( !NetworkTable.IsReadingChanges )
			{
				var currentValue = p.Getter();

				if ( EqualityComparer<T>.Default.Equals( currentValue, p.Value ) )
				{
					p.Setter?.Invoke( p.Value );
					return;
				}
			}

			var root = GameObject.FindNetworkRoot();
			var slot = NetworkObject.GetPropertySlot( p.MemberIdent, Id );

			if ( root is null )
			{
				p.Setter?.Invoke( p.Value );
				return;
			}

			var net = root._net;

			if ( !net.dataTable.IsRegistered( slot ) )
			{
				p.Setter?.Invoke( p.Value );
				return;
			}

			if ( !net.dataTable.HasControl( slot ) )
			{
				if ( !NetworkTable.IsReadingChanges )
					return;

				var attribute = p.GetAttribute<SyncAttribute>();
				var interpolate = attribute?.Flags.HasFlag( SyncFlags.Interpolate ) ?? false;

				if ( interpolate && p.Value is not null )
				{
					var interpolated = GetOrCreateInterpolatedVar( p.Value, p.PropertyName );
					interpolated?.Update( p.Value );
				}

				p.Setter?.Invoke( p.Value );
				return;
			}

			net.dataTable.UpdateSlotHash( slot, p.Value );
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
		var interpolate = attribute?.Flags.HasFlag( SyncFlags.Interpolate ) ?? false;
		if ( !interpolate ) return p.Value;

		var root = GameObject.FindNetworkRoot();
		if ( root is null ) return p.Value;

		var slot = NetworkObject.GetPropertySlot( p.MemberIdent, Id );
		var net = root._net;

		if ( !net.dataTable.IsRegistered( slot ) || net.dataTable.HasControl( slot ) )
			return p.Value;

		if ( InterpolatedVars.TryGetValue( p.PropertyName, out var i ) )
			return (T)i.Query( Time.Now );

		return p.Value;
	}

	[EditorBrowsable( EditorBrowsableState.Never )]
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	protected void __rpc_Wrapper<T>( in WrappedMethod m, T[] argument )
	{
		Rpc.OnCallInstanceRpc( GameObject, this, m, [argument] );
	}

	[EditorBrowsable( EditorBrowsableState.Never )]
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	protected void __rpc_Wrapper( in WrappedMethod m, params object[] argumentList )
	{
		Rpc.OnCallInstanceRpc( GameObject, this, m, argumentList );
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
}
