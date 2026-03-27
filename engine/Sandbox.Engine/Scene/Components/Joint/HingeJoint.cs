using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Create a hinged connection between two physics objects. Like a door hinge or a wheel.
/// </summary>
[Expose]
[Title( "Hinge Joint" )]
[Category( "Physics" )]
[Icon( "door_front" )]
[EditorHandle( "materials/gizmo/hinge.png" )]
public sealed class HingeJoint : Joint
{
	public enum MotorMode
	{
		Disabled,
		TargetAngle,
		TargetVelocity
	}

	/// <summary>
	/// Minimum angle it should be allowed to go
	/// </summary>
	[Title( "Min" ), Group( "Limit" )]
	[Property]
	public float MinAngle
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	}

	/// <summary>
	/// Maximum angle it should be allowed to go
	/// </summary>
	[Title( "Max" ), Group( "Limit" )]
	[Property]
	public float MaxAngle
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	}

	/// <summary>
	/// Motor mode
	/// </summary>
	[Group( "Motor" )]
	[Property]
	public MotorMode Motor
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	}

	/// <summary>
	/// Hinge friction
	/// </summary>
	[Group( "Motor" )]
	[Property, ShowIf( nameof( Motor ), MotorMode.Disabled )]
	public float Friction
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	}

	/// <summary>
	/// Target angle of motor
	/// </summary>
	[Group( "Motor" )]
	[Property, ShowIf( nameof( Motor ), MotorMode.TargetAngle )]
	public float TargetAngle
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	}

	[Obsolete( $"Use {nameof( Frequency )}" )]
	public float Fequency { get => Frequency; set => Frequency = value; }

	/// <summary>
	/// Frequency of motor
	/// </summary>
	[Group( "Motor" )]
	[Property, ShowIf( nameof( Motor ), MotorMode.TargetAngle )]
	public float Frequency
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	} = 1.0f;

	/// <summary>
	/// Damping of motor
	/// </summary>
	[Group( "Motor" )]
	[Property, ShowIf( nameof( Motor ), MotorMode.TargetAngle )]
	public float DampingRatio
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	} = 1.0f;

	/// <summary>
	/// Target velocity of motor
	/// </summary>
	[Group( "Motor" )]
	[Property, ShowIf( nameof( Motor ), MotorMode.TargetVelocity )]
	public float TargetVelocity
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	} = 0.0f;

	/// <summary>
	/// Max torque of motor
	/// </summary>
	[Group( "Motor" )]
	[Property, ShowIf( nameof( Motor ), MotorMode.TargetVelocity )]
	public float MaxTorque
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	} = 0.0f;

	[Group( "State" )]
	[Property, JsonIgnore]
	public float Angle => _joint.IsValid() ? _joint.Angle : default;

	[Group( "State" )]
	[Property, JsonIgnore]
	public float Speed => _joint.IsValid() ? _joint.Speed : default;

	[Group( "State" )]
	[Property, JsonIgnore]
	public Vector3 Axis => _joint.IsValid() ? _joint.Axis : default;

	Physics.HingeJoint _joint;

	protected override PhysicsJoint CreateJoint( PhysicsPoint point1, PhysicsPoint point2 )
	{
		var localFrame1 = LocalFrame1;
		var localFrame2 = LocalFrame2;

		if ( Attachment == AttachmentMode.Auto )
		{
			localFrame1 = point1.LocalTransform;
			localFrame2 = point2.LocalTransform;
		}

		if ( !Scene.IsEditor )
		{
			LocalFrame1 = localFrame1;
			LocalFrame2 = localFrame2;

			Attachment = AttachmentMode.LocalFrames;
		}

		point1.LocalTransform = localFrame1;
		point2.LocalTransform = localFrame2;

		_joint = PhysicsJoint.CreateHinge( point1, point2 );

		UpdateProperties();

		return _joint;
	}

	protected override void OnDirty()
	{
		UpdateProperties();
	}

	private void UpdateProperties()
	{
		if ( !_joint.IsValid() )
			return;

		_joint.MinAngle = MinAngle;
		_joint.MaxAngle = MaxAngle;

		if ( Motor == MotorMode.Disabled )
		{
			_joint.Friction = Friction;
		}
		else if ( Motor == MotorMode.TargetAngle )
		{
			_joint.native.SetAngularSpring( new Vector3( TargetAngle.DegreeToRadian(), Frequency, DampingRatio ) );
		}
		else if ( Motor == MotorMode.TargetVelocity )
		{
			_joint.native.SetAngularMotor( TargetVelocity.DegreeToRadian(), MaxTorque );
		}

		_joint.WakeBodies();
	}

	public override int ComponentVersion => 1;

	[Expose, JsonUpgrader( typeof( HingeJoint ), 1 )]
	static void Upgrader_v1( JsonObject obj )
	{
		if ( obj.TryGetPropertyValue( "Fequency", out var frequency ) )
		{
			obj["Frequency"] = (float)frequency;
		}
	}
}
