namespace Sandbox;

[Hide]
public sealed class CableNodeComponent : Component, Component.ExecuteInEditor
{
	float _radiusScale = 1.0f;
	float _roll;
	Vector3 _lastLocalPosition;
	float _lastRadiusScale = 1.0f;
	float _lastRoll;

	[Property, Range( 0.1f, 128.0f, slider: true ), Step( 0.05f ), Title( "Radius Scale" )]
	public float RadiusScale
	{
		get => _radiusScale;
		set
		{
			var clamped = Math.Clamp( value, 0.1f, 128.0f );
			if ( MathF.Abs( _radiusScale - clamped ) < 0.0001f )
				return;

			_radiusScale = clamped;
			NotifyParentCable();
		}
	}

	[Property, Range( -180.0f, 180.0f, slider: true ), Step( 1.0f ), Title( "Roll" )]
	public float Roll
	{
		get => _roll;
		set
		{
			var clamped = Math.Clamp( value, -180.0f, 180.0f );
			if ( MathF.Abs( _roll - clamped ) < 0.0001f )
				return;

			_roll = clamped;
			NotifyParentCable();
		}
	}

	protected override void OnEnabled()
	{
		_lastLocalPosition = GameObject.LocalPosition;
		_lastRadiusScale = _radiusScale;
		_lastRoll = _roll;

		if ( !Scene.IsEditor )
			return;

		NotifyParentCable();
	}

	protected override void OnUpdate()
	{
		if ( !Scene.IsEditor )
			return;

		if ( _lastLocalPosition.AlmostEqual( GameObject.LocalPosition ) &&
			MathF.Abs( _lastRadiusScale - _radiusScale ) < 0.0001f &&
			MathF.Abs( _lastRoll - _roll ) < 0.0001f )
			return;

		_lastLocalPosition = GameObject.LocalPosition;
		_lastRadiusScale = _radiusScale;
		_lastRoll = _roll;
		NotifyParentCable();
	}

	void NotifyParentCable()
	{
		GameObject.Parent?.GetComponent<CableComponent>( true )?.NotifyNodeChanged();
	}
}
