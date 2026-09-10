using Sandbox.Interpolation;
using Sandbox.Utility;

namespace Sandbox;

public partial class GameTransform
{
	Transform _interpolatedLocal;
	Transform _targetLocal;
	// Allocated lazily — only created when interpolation is actually used.
	InterpolationBuffer<TransformState> _networkTransformBuffer;
	InterpolationBuffer<Vector3State> _positionBuffer;
	InterpolationBuffer<RotationState> _rotationBuffer;
	InterpolationBuffer<Vector3State> _scaleBuffer;

	InterpolationSystem InterpolationSystem
	{
		get
		{
			field ??= GameObject.Scene.GetSystem<InterpolationSystem>();
			return field;
		}
	}

	internal bool Interpolate
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			if ( field )
			{
				InterpolationSystem?.AddGameObject( GameObject );
			}
			else
			{
				InterpolationSystem?.RemoveGameObject( GameObject );
				ClearInterpolationInternal();
			}
		}
	}

	/// <summary>
	/// The desired local transform. If we are interpolating we'll use the last value in the interpolation buffer.
	/// This is useful for networking because we always want to send the "real" transform.
	/// </summary>
	internal Transform TargetLocal
	{
		get
		{
			if ( !GameObject.IsProxy )
				return _targetLocal;

			var flags = GameObject.Network?.Flags ?? NetworkFlags.None;
			var networkTarget = _networkTransformBuffer?.IsEmpty == false ? _networkTransformBuffer.Last.State.Transform : _interpolatedLocal;

			var result = _interpolatedLocal;
			result.Position = (flags & NetworkFlags.NoPositionSync) != 0 ? _targetLocal.Position : networkTarget.Position;
			result.Rotation = (flags & NetworkFlags.NoRotationSync) != 0 ? _targetLocal.Rotation : networkTarget.Rotation;
			result.Scale = (flags & NetworkFlags.NoScaleSync) != 0 ? _targetLocal.Scale : networkTarget.Scale;
			return result;
		}
	}

	/// <summary>
	/// This will be true if the GameObject is enabled, we're in a Fixed Update context, and interpolation
	/// is not disabled for this GameObject.
	/// </summary>
	bool ShouldInterpolate()
	{
		// If we're headless, don't interpolate. Interpolation here is for visual purposes only and we
		// have no graphics.
		if ( Application.IsHeadless )
			return false;

		var isEnabled = GameObject?.Enabled ?? false;
		var isFixedUpdate = GameObject?.Scene?.IsFixedUpdate ?? false;
		var isStatic = GameObject?.IsStatic ?? false;
		var isInterpolationDisabled = GameObject?.Flags.Contains( GameObjectFlags.NoInterpolation ) ?? false;

		return FixedUpdateInterpolation && isFixedUpdate && isEnabled && !isInterpolationDisabled && !isStatic;
	}

	void UpdateInterpolatedLocal( in Transform value )
	{
		var targetTime = Time.NowDouble + Time.Delta;
		var shouldInterpolate = false;

		if ( _targetLocal.Position != value.Position )
		{
			_positionBuffer ??= new( Vector3State.CreateInterpolator() );
			if ( _positionBuffer.IsEmpty )
				_positionBuffer.Add( new( _targetLocal.Position ), Time.NowDouble );

			_positionBuffer.Add( new( value.Position ), targetTime );
			_targetLocal.Position = value.Position;
			shouldInterpolate = true;
		}

		if ( _targetLocal.Rotation != value.Rotation )
		{
			_rotationBuffer ??= new( RotationState.CreateInterpolator() );
			if ( _rotationBuffer.IsEmpty )
				_rotationBuffer.Add( new( _targetLocal.Rotation ), Time.NowDouble );

			_rotationBuffer.Add( new( value.Rotation ), targetTime );
			_targetLocal.Rotation = value.Rotation;
			shouldInterpolate = true;
		}

		if ( _targetLocal.Scale != value.Scale )
		{
			_scaleBuffer ??= new( Vector3State.CreateInterpolator() );
			if ( _scaleBuffer.IsEmpty )
				_scaleBuffer.Add( new( _targetLocal.Scale ), Time.NowDouble );

			_scaleBuffer.Add( new( value.Scale ), targetTime );
			_targetLocal.Scale = value.Scale;
			shouldInterpolate = true;
		}

		if ( shouldInterpolate )
		{
			Interpolate = true;
			TransformChanged();
		}
	}

	void UpdateLocal( in Transform value )
	{
		_hasPositionSet = true;

		var didTransformChange = false;

		if ( _interpolatedLocal.Position != value.Position )
		{
			_interpolatedLocal.Position = value.Position;
			_targetLocal.Position = value.Position;
			_positionBuffer?.Clear();
			didTransformChange = true;
		}

		if ( _interpolatedLocal.Rotation != value.Rotation )
		{
			_interpolatedLocal.Rotation = value.Rotation;
			_targetLocal.Rotation = value.Rotation;
			_rotationBuffer?.Clear();
			didTransformChange = true;
		}

		if ( _interpolatedLocal.Scale != value.Scale )
		{
			_interpolatedLocal.Scale = value.Scale;
			_targetLocal.Scale = value.Scale;
			_scaleBuffer?.Clear();
			didTransformChange = true;
		}

		if ( didTransformChange )
			TransformChanged();
	}


	/// <summary>
	/// The interpolated world transform. For internal use only.
	/// </summary>
	internal Transform InterpolatedWorld
	{
		get
		{
			if ( !IsFollowingParent() ) return InterpolatedLocal;
			if ( Proxy is not null ) return Proxy.GetWorldTransform();

			return GameObject.Parent.Transform.InterpolatedWorld.ToWorld( InterpolatedLocal );
		}
	}

	/// <summary>
	/// Clear any interpolation and force us to reach our final destination immediately. If we own this object
	/// we'll tell other clients to clear interpolation too when they receive the next network update from us.
	/// </summary>
	public void ClearInterpolation()
	{
		GameObject?._net?.ClearInterpolation();
		Interpolate = false;
		TransformChanged();
	}

	[Obsolete( "Use ClearInterpolation" )]
	public void ClearLerp()
	{
		ClearInterpolation();
	}

	/// <summary>
	/// Like <see cref="ClearInterpolation"/> but will not clear interpolation across the network.
	/// </summary>
	internal void ClearLocalInterpolation()
	{
		Interpolate = false;
		TransformChanged();
	}

	void ClearInterpolationInternal()
	{
		_interpolatedLocal = _targetLocal;

		if ( _networkTransformBuffer?.IsEmpty == false )
		{
			var snapshot = _networkTransformBuffer.Last;
			SetLocalTransformFast( snapshot.State.Transform );
			_networkTransformBuffer.Clear();
		}

		_positionBuffer?.Clear();
		_rotationBuffer?.Clear();
		_scaleBuffer?.Clear();
	}


	internal void Update( double now, double cullBefore )
	{
		if ( GameObject.IsProxy )
		{
			InterpolateProxy( now, cullBefore );
			return;
		}

		InterpolateFixedUpdate( now, cullBefore );
	}

	void InterpolateFixedUpdate( double now, double cullBefore )
	{
		if ( GameObject?.Flags.Contains( GameObjectFlags.NoInterpolation ) ?? false )
		{
			_positionBuffer?.Clear();
			_rotationBuffer?.Clear();
			_scaleBuffer?.Clear();
		}

		var tx = _interpolatedLocal;

		// Use 0 window since entries are timestamped into the future
		tx.Position = _positionBuffer?.IsEmpty == false ? _positionBuffer.QueryAndCull( now, cullBefore ).Value : _targetLocal.Position;
		tx.Rotation = _rotationBuffer?.IsEmpty == false ? _rotationBuffer.QueryAndCull( now, cullBefore ).Rotation : _targetLocal.Rotation;
		tx.Scale = _scaleBuffer?.IsEmpty == false ? _scaleBuffer.QueryAndCull( now, cullBefore ).Value : _targetLocal.Scale;

		_interpolatedLocal = tx;
		TransformChanged( true );

		if ( (_positionBuffer?.IsEmpty ?? true) && (_rotationBuffer?.IsEmpty ?? true) && (_scaleBuffer?.IsEmpty ?? true) )
		{
			Interpolate = false;
		}
	}

	void InterpolateProxy( double now, double cullBefore )
	{
		var flags = GameObject.Network?.Flags ?? NetworkFlags.None;

		if ( GameObject?.Flags.Contains( GameObjectFlags.NoInterpolation ) ?? false )
		{
			_networkTransformBuffer?.Clear();
			_positionBuffer?.Clear();
			_rotationBuffer?.Clear();
			_scaleBuffer?.Clear();
		}

		var hasNetwork = _networkTransformBuffer?.IsEmpty == false;
		var networkState = _interpolatedLocal;
		if ( hasNetwork )
		{
			var interpolationTime = Networking.InterpolationTime;
			networkState = _networkTransformBuffer.QueryAndCull( now - interpolationTime, now - (interpolationTime * 3f) ).Transform;
		}

		var tx = _interpolatedLocal;
		var target = _targetLocal;

		if ( (flags & NetworkFlags.NoPositionSync) == 0 )
		{
			_positionBuffer?.Clear();

			if ( hasNetwork )
			{
				tx.Position = networkState.Position;
				target.Position = networkState.Position;
			}
		}
		else
		{
			tx.Position = _positionBuffer?.IsEmpty == false ? _positionBuffer.QueryAndCull( now, cullBefore ).Value : _targetLocal.Position;
		}

		if ( (flags & NetworkFlags.NoRotationSync) == 0 )
		{
			_rotationBuffer?.Clear();

			if ( hasNetwork )
			{
				tx.Rotation = networkState.Rotation;
				target.Rotation = networkState.Rotation;
			}
		}
		else
		{
			tx.Rotation = _rotationBuffer?.IsEmpty == false ? _rotationBuffer.QueryAndCull( now, cullBefore ).Rotation : _targetLocal.Rotation;
		}

		if ( (flags & NetworkFlags.NoScaleSync) == 0 )
		{
			_scaleBuffer?.Clear();

			if ( hasNetwork )
			{
				tx.Scale = networkState.Scale;
				target.Scale = networkState.Scale;
			}
		}
		else
		{
			tx.Scale = _scaleBuffer?.IsEmpty == false ? _scaleBuffer.QueryAndCull( now, cullBefore ).Value : _targetLocal.Scale;
		}

		var changed = tx != _interpolatedLocal || target != _targetLocal;
		_interpolatedLocal = tx;
		_targetLocal = target;

		if ( changed )
			TransformChanged();

		var networkDone = _networkTransformBuffer?.IsEmpty ?? true;
		var localDone = (_positionBuffer?.IsEmpty ?? true) && (_rotationBuffer?.IsEmpty ?? true) && (_scaleBuffer?.IsEmpty ?? true);

		if ( networkDone && localDone )
		{
			Interpolate = false;
		}
	}

	/// <summary>
	/// Temporarily disable Fixed Update Interpolation.
	/// </summary>
	/// <returns></returns>
	internal static DisposeAction<bool> DisableInterpolation()
	{
		var saved = FixedUpdateInterpolation;
		FixedUpdateInterpolation = false;

		unsafe
		{
			static void Restore( bool value ) => FixedUpdateInterpolation = value;
			return DisposeAction<bool>.Create( &Restore, saved );
		}
	}
}
