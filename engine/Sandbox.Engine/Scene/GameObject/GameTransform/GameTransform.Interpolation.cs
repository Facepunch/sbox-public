using Sandbox.Interpolation;
using Sandbox.Utility;

namespace Sandbox;

public partial class GameTransform
{
	Transform _interpolatedLocal;
	Transform _targetLocal;
	readonly InterpolationBuffer<TransformState> _networkTransformBuffer = new( TransformState.CreateInterpolator() );
	readonly InterpolationBuffer<Vector3State> _positionBuffer = new( Vector3State.CreateInterpolator() );
	readonly InterpolationBuffer<RotationState> _rotationBuffer = new( RotationState.CreateInterpolator() );
	readonly InterpolationBuffer<Vector3State> _scaleBuffer = new( Vector3State.CreateInterpolator() );

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
			// If we're a proxy let's try to find the latest networked tranform to interpolate to, or our local transform.
			if ( GameObject.IsProxy )
				return _networkTransformBuffer.IsEmpty ? _interpolatedLocal : _networkTransformBuffer.Last.State.Transform;

			return _targetLocal;
		}
	}

	/// <summary>
	/// This will be true if the GameObject is enabled, we're in a Fixed Update context, and interpolation
	/// is not disabled for this GameObject.
	/// </summary>
	bool ShouldInterpolate()
	{
		var isEnabled = GameObject?.Enabled ?? false;
		var isFixedUpdate = GameObject?.Scene?.IsFixedUpdate ?? false;
		var isInterpolationDisabled = GameObject?.Flags.Contains( GameObjectFlags.NoInterpolation ) ?? false;

		return FixedUpdateInterpolation && isFixedUpdate && isEnabled && !isInterpolationDisabled;
	}

	void UpdateInterpolatedLocal( in Transform value )
	{
		var targetTime = Time.Now + Time.Delta;
		var shouldInterpolate = false;

		if ( _targetLocal.Position != value.Position )
		{
			if ( _positionBuffer.IsEmpty )
				_positionBuffer.Add( new( _targetLocal.Position ), Time.Now );

			_positionBuffer.Add( new( value.Position ), targetTime );
			_targetLocal.Position = value.Position;
			shouldInterpolate = true;
		}

		if ( _targetLocal.Rotation != value.Rotation )
		{
			if ( _rotationBuffer.IsEmpty )
				_rotationBuffer.Add( new( _targetLocal.Rotation ), Time.Now );

			_rotationBuffer.Add( new( value.Rotation ), targetTime );
			_targetLocal.Rotation = value.Rotation;
			shouldInterpolate = true;
		}

		if ( _targetLocal.Scale != value.Scale )
		{
			if ( _scaleBuffer.IsEmpty )
				_scaleBuffer.Add( new( _targetLocal.Scale ), Time.Now );

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
			_positionBuffer.Clear();
			didTransformChange = true;
		}

		if ( _interpolatedLocal.Rotation != value.Rotation )
		{
			_interpolatedLocal.Rotation = value.Rotation;
			_targetLocal.Rotation = value.Rotation;
			_rotationBuffer.Clear();
			didTransformChange = true;
		}

		if ( _interpolatedLocal.Scale != value.Scale )
		{
			_interpolatedLocal.Scale = value.Scale;
			_targetLocal.Scale = value.Scale;
			_scaleBuffer.Clear();
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

		if ( !_networkTransformBuffer.IsEmpty )
		{
			var snapshot = _networkTransformBuffer.Last;
			SetLocalTransformFast( snapshot.State.Transform );
			_networkTransformBuffer.Clear();
		}

		_positionBuffer.Clear();
		_rotationBuffer.Clear();
		_scaleBuffer.Clear();
	}


	internal void Update()
	{
		if ( GameObject.IsProxy )
		{
			InterpolateNetwork();
			return;
		}

		InterpolateFixedUpdate();
	}

	void InterpolateFixedUpdate()
	{
		if ( GameObject?.Flags.Contains( GameObjectFlags.NoInterpolation ) ?? false )
		{
			_positionBuffer.Clear();
			_rotationBuffer.Clear();
			_scaleBuffer.Clear();
		}

		var tx = _interpolatedLocal;

		// Use 0 window since entries are timestamped into the future
		tx.Position = !_positionBuffer.IsEmpty ? _positionBuffer.Query( Time.Now ).Value : _targetLocal.Position;
		tx.Rotation = !_rotationBuffer.IsEmpty ? _rotationBuffer.Query( Time.Now ).Rotation : _targetLocal.Rotation;
		tx.Scale = !_scaleBuffer.IsEmpty ? _scaleBuffer.Query( Time.Now ).Value : _targetLocal.Scale;

		float updateFreq = ProjectSettings.Physics.FixedUpdateFrequency.Clamp( 1, 1000 );
		var fixedDelta = 1f / updateFreq;

		// Keep more history to avoid culling data we might still need for interpolation
		var cullOlderThanThreshold = fixedDelta * 2f;
		_positionBuffer.CullOlderThan( Time.Now - cullOlderThanThreshold );
		_rotationBuffer.CullOlderThan( Time.Now - cullOlderThanThreshold );
		_scaleBuffer.CullOlderThan( Time.Now - cullOlderThanThreshold );

		_interpolatedLocal = tx;
		TransformChanged( true );

		if ( _positionBuffer.IsEmpty && _rotationBuffer.IsEmpty && _scaleBuffer.IsEmpty )
		{
			Interpolate = false;
		}
	}

	void InterpolateNetwork()
	{
		if ( GameObject?.Flags.Contains( GameObjectFlags.NoInterpolation ) ?? false )
		{
			_networkTransformBuffer.Clear();
		}

		if ( !_networkTransformBuffer.IsEmpty )
		{
			var interpolationTime = Networking.InterpolationTime;
			var state = _networkTransformBuffer.Query( Time.Now - interpolationTime );

			_interpolatedLocal = state.Transform;
			_targetLocal = _interpolatedLocal;
			TransformChanged();

			_networkTransformBuffer.CullOlderThan( Time.Now - (interpolationTime * 3f) );
		}

		if ( _networkTransformBuffer.IsEmpty )
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
