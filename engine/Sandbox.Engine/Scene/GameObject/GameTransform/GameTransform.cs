using Sandbox.Utility;
using System.Runtime.CompilerServices;
using Sandbox.Interpolation;

namespace Sandbox;

[Expose, ActionGraphIgnore]
[Icon( "control_camera" )]
public partial class GameTransform
{
	/// <summary>
	/// Automatically interpolate the transform over multiple frames when changed within the context
	/// of a fixed update. This results in a smoother appearance for a moving <see cref="GameObject"/>.
	/// </summary>
	static bool FixedUpdateInterpolation { get; set; } = true;

	[ActionGraphInclude]
	public GameObject GameObject { get; }

	/// <summary>
	/// Are we following our parent object?
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	bool IsFollowingParent()
	{
		// Is flagged not to follow parent
		if ( GameObject.Flags.Contains( GameObjectFlags.Absolute ) ) return false;

		// Has no parent
		if ( GameObject.Parent is null ) return false;

		// Parent is a scene
		if ( GameObject.Parent is Scene && GameObject.Parent is not PrefabScene ) return false;


		return true;
	}

	public TransformProxy Proxy { get; set; }

	/// <summary>
	/// Returns true if we're inside the transform changed callback.
	/// </summary>
	internal bool InsideChangeCallback { get; private set; }

	/// <summary>
	/// Returns true if we're interpolating the transform, which means we're not inside a change callback and
	/// we're not in a fixed update scene.
	/// </summary>
	private bool IsInterpolating => !InsideChangeCallback && !(GameObject?.Scene?.IsFixedUpdate ?? false);

	internal GameTransform( GameObject owner )
	{
		GameObject = owner;
		_interpolatedLocal = Transform.Zero;
		_targetLocal = Transform.Zero;
		_hasPositionSet = false;
	}

	bool _hasPositionSet;


	/// <summary>
	/// The current interpolated local transform.
	/// </summary>
	[ActionGraphInclude( AutoExpand = true )]
	public Transform InterpolatedLocal
	{
		get
		{
			return Proxy?.GetLocalTransform() ?? _interpolatedLocal;
		}
	}

	/// <summary>
	/// The current local transform.
	/// </summary>
	[ActionGraphInclude( AutoExpand = true )]
	public Transform Local
	{
		get
		{
			if ( Proxy is not null )
				return Proxy.GetLocalTransform();

			if ( !IsInterpolating )
				return _targetLocal;

			return _interpolatedLocal;
		}

		set
		{
			if ( value == default )
				value = Transform.Zero;

			if ( value.Position.IsNaN )
			{
				Log.Warning( "Ignoring NaN Position" );
				return;
			}

			if ( !(GameObject?.HasAuthority() ?? true) )
				return;

			if ( Proxy is not null )
			{
				Proxy.SetLocalTransform( value );
				return;
			}

			SetLocalTransform( value, ShouldInterpolate() );
		}
	}

	void SetLocalTransform( in Transform value, bool interpolate = false )
	{
		if ( _targetLocal == value )
			return;

		if ( interpolate && _hasPositionSet )
		{
			UpdateInterpolatedLocal( value );
		}
		else
		{
			UpdateLocal( value );
		}
	}

	/// <summary>
	/// Sets the local transform without firing a bunch of "transform changed" callbacks.
	/// The assumption is that you're changing a bunch of child transforms, and will then call
	/// transform changed on the root, which will then invoke all the callbacks just once.
	/// This is what the animation system does!
	/// </summary>
	internal bool SetLocalTransformFast( in Transform value )
	{
		if ( _targetLocal == value )
			return false;

		_interpolatedLocal = value;
		_targetLocal = value;

		return true;
	}


	/// <summary>
	/// The target world transform. For internal use only.
	/// </summary>
	internal Transform TargetWorld
	{
		get
		{
			if ( Proxy is not null ) return Proxy.GetWorldTransform();
			if ( !IsFollowingParent() ) return TargetLocal;

			return GameObject.Parent.Transform.TargetWorld.ToWorld( TargetLocal );
		}
	}

	/// <summary>
	/// The world transform gets cached to avoid recalculating it every time. It is invalidated in TransformChanged, which
	/// is called recursively down children when the transform changes.
	/// </summary>
	Transform? _worldCached;
	Transform? _worldInterpCached;

	/// <summary>
	/// The current world transform.
	/// </summary>
	public Transform World
	{
		get
		{
			if ( Proxy is not null ) return Proxy.GetWorldTransform();
			if ( !IsFollowingParent() ) return Local;

			if ( IsInterpolating )
			{
				if ( _worldInterpCached is Transform cached )
					return cached;

				var result = GameObject.Parent.Transform.World.ToWorld( Local );
				_worldInterpCached = result;
				return result;
			}
			else
			{
				if ( _worldCached is Transform cached )
					return cached;

				var result = GameObject.Parent.Transform.World.ToWorld( Local );
				_worldCached = result;
				return result;
			}
		}

		set
		{
			if ( !(GameObject?.HasAuthority() ?? true) )
				return;

			SetWorldInternal( value );
		}
	}

	/// <summary>
	/// Set from the provided <see cref="Transform"/> in world-space.
	/// </summary>
	/// <param name="value">The world-space transform.</param>
	internal void SetWorldInternal( Transform value )
	{
		if ( value == default )
			value = Transform.Zero;

		if ( value.Position.IsNaN )
		{
			Log.Warning( "Ignoring NaN Position" );
			return;
		}

		if ( Proxy is not null )
		{
			Proxy.SetWorldTransform( value );
			return;
		}

		var interpolate = ShouldInterpolate();

		if ( !IsFollowingParent() )
		{
			SetLocalTransform( value, interpolate );
			return;
		}

		var localTransform = GameObject.Parent.WorldTransform.ToLocal( value );
		SetLocalTransform( localTransform, interpolate );
	}

	/// <summary>
	/// The position in world coordinates.
	/// </summary>
	[ActionGraphInclude]
	[Obsolete( "Use WorldPosition instead of Transform.Position" )]
	public Vector3 Position
	{
		get => World.Position;
		set
		{
			if ( value.IsNaN )
				throw new ArgumentOutOfRangeException( nameof( value ), @"Position is NaN" );

			World = World.WithPosition( value );
		}
	}

	/// <summary>
	/// The rotation in world coordinates.
	/// </summary>
	[ActionGraphInclude]
	[Obsolete( "Use WorldRotation instead of Transform.Rotation" )]
	public Rotation Rotation
	{
		get => World.Rotation;
		set
		{
			World = World.WithRotation( value );
		}
	}

	/// <summary>
	/// The scale in world coordinates.
	/// </summary>
	[ActionGraphInclude]
	[DefaultValue( 1f )]
	[Obsolete( "Use WorldScale instead of Transform.Scale" )]
	public Vector3 Scale
	{
		get => World.Scale;
		set => World = World.WithScale( value );
	}

	/// <summary>
	/// Position in local coordinates.
	/// </summary>
	[ActionGraphInclude]
	[Property]
	[Obsolete( "Use LocalPosition instead of Transform.LocalPosition" )]
	public Vector3 LocalPosition
	{
		get => Local.Position;
		set
		{
			Local = Local.WithPosition( value );
		}
	}

	/// <summary>
	/// Rotation in local coordinates.
	/// </summary>
	[ActionGraphInclude]
	[Property]
	[Obsolete( "Use LocalRotation instead of Transform.LocalRotation" )]
	public Rotation LocalRotation
	{
		get => Local.Rotation;
		set
		{
			Local = Local.WithRotation( value );
		}
	}

	/// <summary>
	/// Scale in local coordinates.
	/// </summary>
	[ActionGraphInclude]
	[Property]
	[DefaultValue( 1f )]
	[Obsolete( "Use LocalScale instead of Transform.LocalScale" )]
	public Vector3 LocalScale
	{
		get => Local.Scale;
		set
		{
			Local = Local.WithScale( value );
		}
	}

	/// <summary>
	/// Performs linear interpolation between this and the given transform.
	/// </summary>
	/// <param name="target">The destination transform.</param>
	/// <param name="frac">Fraction, where 0 would return this, 0.5 would return a point between this and given transform, and 1 would return the given transform.</param>
	[ActionGraphInclude]
	public void LerpTo( in Transform target, float frac )
	{
		var tx = World;
		tx = Transform.Lerp( tx, target, frac, true );
		World = tx;
	}


	internal void FromNetwork( Transform transform, bool clearInterpolation )
	{
		if ( GameObject.Network.Interpolation && !clearInterpolation )
		{
			_networkTransformBuffer.Add( new TransformState( transform ), Time.Now );
			Interpolate = true;
		}
		else
		{
			if ( clearInterpolation )
				Interpolate = false;

			SetLocalTransform( transform );
		}
	}


	/// <summary>
	/// Disable the proxy temporarily
	/// </summary>
	public IDisposable DisableProxy()
	{
		if ( Proxy is null ) return default;

		var saved = Proxy;
		Proxy = default;

		return DisposeAction.Create( () => { Proxy = saved; } );
	}
}
