namespace Sandbox;

public sealed partial class Model : Resource
{
	PhysicsGroupDescription _physics;

	public PhysicsGroupDescription Physics
	{
		get
		{
			if ( _physics is not null )
				return _physics;

			var container = native.GetPhysicsContainer();
			if ( container.IsNull ) return null;

			_physics = new PhysicsGroupDescription( container );
			return _physics;
		}
	}
}


public sealed class PhysicsGroupDescription
{
	internal CPhysAggregateData native;

	internal PhysicsGroupDescription( CPhysAggregateData native )
	{
		this.native = native;
		this.native.AddRef();

		Refresh();
	}

	~PhysicsGroupDescription()
	{
		var n = native;
		native = default;

		MainThread.Queue( () => n.Release() );
	}

	internal void Dispose()
	{
		foreach ( var p in _parts )
		{
			p.Dispose();
		}

		_parts.Clear();
		_joints.Clear();
	}

	readonly List<BodyPart> _parts = new();
	readonly List<Joint> _joints = new();
	readonly List<string> _surfaces = new();
	readonly List<List<StringToken>> _tags = new();

	public IReadOnlyList<BodyPart> Parts => _parts;
	public IReadOnlyList<Joint> Joints => _joints;

	/// <summary>
	/// Enumerate every <see cref="Surface"/> in this <see cref="Model"/> 
	/// </summary>
	public IEnumerable<Surface> Surfaces => Enumerable.Range( 0, _surfaces.Count )
		.Select( x => GetSurface( (uint)x ) );

	void Refresh()
	{
		_surfaces.Clear();

		var surfaceCount = native.GetSurfacePropertiesCount();
		for ( int i = 0; i < surfaceCount; i++ )
		{
			var s = native.GetSurfaceProperties( i );
			_surfaces.Add( s.IsValid ? s.m_name : "default" );
		}

		_tags.Clear();

		var attributeCount = native.GetCollisionAttributeCount();
		for ( int attributeIndex = 0; attributeIndex < attributeCount; attributeIndex++ )
		{
			var tagCount = native.GetTagCount( attributeIndex );
			var tags = new List<StringToken>( tagCount );

			for ( int tagIndex = 0; tagIndex < tagCount; tagIndex++ )
			{
				tags.Add( new StringToken( native.GetTag( attributeIndex, tagIndex ) ) );
			}

			_tags.Add( tags );
		}

		// todo - destroy old parts, null their pointers
		_parts.Clear();

		for ( int i = 0; i < native.GetPartCount(); i++ )
		{
			var tx = native.GetBoneCount() > 0 ? native.GetBindPose( i ) : Transform.Zero;
			var boneName = native.GetBoneCount() > 0 ? native.GetBoneName( i ) : "";

			_parts.Add( new BodyPart( this, boneName, native.GetPart( i ), tx ) );
		}

		_joints.Clear();

		for ( int i = 0; i < native.GetJointCount(); i++ )
		{
			_joints.Add( new Joint( native.GetJoint( i ) ) );
		}
	}

	internal Surface GetSurface( uint index )
	{
		if ( index >= _surfaces.Count )
			return null;

		var surfaceName = _surfaces[(int)index];
		return Surface.FindByName( surfaceName );
	}

	internal IReadOnlyList<StringToken> GetTags( int index )
	{
		if ( index >= _tags.Count )
			return null;

		return _tags[index];
	}

	public int BoneCount => native.GetBoneCount();

	public enum JointType
	{
		Ball,
		Hinge,
		Slider,
		Fixed,
	}

	public sealed class Joint
	{
		internal VPhysXJoint_t native;

		public JointType Type { get; internal set; }
		public bool Fixed { get; internal set; }

		public int Body1 => native.m_nBody1;
		public int Body2 => native.m_nBody2;

		public Transform Frame1 => native.m_Frame1;
		public Transform Frame2 => native.m_Frame2;

		public bool EnableCollision => native.m_bEnableCollision;

		public bool EnableLinearLimit => native.m_bEnableLinearLimit;
		public bool EnableLinearMotor => native.m_bEnableLinearMotor;
		public Vector3 LinearTargetVelocity => native.m_vLinearTargetVelocity;
		public float MaxForce => native.m_flMaxForce;
		public float LinearFrequency => native.m_flLinearFrequency;
		public float LinearDampingRatio => native.m_flLinearDampingRatio;
		public float LinearStrength => native.m_flLinearStrength;

		public bool EnableSwingLimit => native.m_bEnableSwingLimit;
		public bool EnableTwistLimit => native.m_bEnableTwistLimit;
		public bool EnableAngularMotor => native.m_bEnableAngularMotor;
		public Vector3 AngularTargetVelocity => native.m_vAngularTargetVelocity;
		public float MaxTorque => native.m_flMaxTorque;
		public float AngularFrequency => native.m_flAngularFrequency;
		public float AngularDampingRatio => native.m_flAngularDampingRatio;
		public float AngularStrength => native.m_flAngularStrength;

		public float LinearMin => native.GetLinearLimitMin();
		public float LinearMax => native.GetLinearLimitMax();

		public float SwingMin => native.GetSwingLimitMin().RadianToDegree();
		public float SwingMax => native.GetSwingLimitMax().RadianToDegree();

		public float TwistMin => native.GetTwistLimitMin().RadianToDegree();
		public float TwistMax => native.GetTwistLimitMax().RadianToDegree();

		internal Joint( VPhysXJoint_t native )
		{
			this.native = native;

			Fixed = native.m_nFlags == 1;

			var type = (PhysicsJointType)native.m_nType;
			Type = type switch
			{
				PhysicsJointType.SPHERICAL_JOINT or PhysicsJointType.CONICAL_JOINT or PhysicsJointType.QUAT_ORTHOTWIST_JOINT => JointType.Ball,
				PhysicsJointType.REVOLUTE_JOINT => JointType.Hinge,
				PhysicsJointType.PRISMATIC_JOINT => JointType.Slider,
				PhysicsJointType.WELD_JOINT => JointType.Fixed,
				_ => throw new ArgumentOutOfRangeException( nameof( type ), $"Unhandled joint type: {type}" )
			};
		}
	}

	public sealed class BodyPart
	{
		private readonly PhysicsGroupDescription parent;
		internal VPhysXBodyPart_t native;

		public Transform Transform { get; init; }

		public string BoneName { get; init; }

		private List<Part> All { get; } = new();

		public float Mass => native.m_flMass;
		public float LinearDamping => native.m_flLinearDamping;
		public float AngularDamping => native.m_flAngularDamping;
		public bool OverrideMassCenter => native.m_bOverrideMassCenter;
		public Vector3 MassCenterOverride => native.m_vMassCenterOverride;
		public float GravityScale => native.m_flGravityScale;

		internal BodyPart( PhysicsGroupDescription physicsGroupDescription, string boneName, VPhysXBodyPart_t vPhysXBodyPart_t, Transform transform )
		{
			Transform = transform;
			parent = physicsGroupDescription;
			native = vPhysXBodyPart_t;
			BoneName = boneName;

			for ( int i = 0; i < native.GetSphereCount(); i++ )
			{
				var p = native.GetSphere( i );
				All.Add( new SpherePart( p, parent.GetSurface( p.m_nSurfacePropertyIndex ) ) );
			}

			for ( int i = 0; i < native.GetCapsuleCount(); i++ )
			{
				var p = native.GetCapsule( i );
				All.Add( new CapsulePart( p, parent.GetSurface( p.m_nSurfacePropertyIndex ) ) );
			}

			for ( int i = 0; i < native.GetHullCount(); i++ )
			{
				var p = native.GetHull( i );
				All.Add( new HullPart( p, parent.GetSurface( p.m_nSurfacePropertyIndex ) ) );
			}

			var meshCount = native.GetMeshCount();
			if ( meshCount > 0 )
			{
				var surfaces = Enumerable.Range( 0, parent._surfaces.Count )
					.Select( x => parent.GetSurface( (uint)x ) ).ToArray();

				for ( int i = 0; i < meshCount; i++ )
				{
					var p = native.GetMesh( i );
					All.Add( new MeshPart( p, parent.GetSurface( p.m_nSurfacePropertyIndex ), surfaces ) );
				}
			}
		}

		internal void Dispose()
		{
			foreach ( var s in All )
			{
				s.Dispose();
			}
		}

		public IReadOnlyList<SpherePart> Spheres => All.OfType<SpherePart>().ToList();
		public IReadOnlyList<CapsulePart> Capsules => All.OfType<CapsulePart>().ToList();
		public IReadOnlyList<HullPart> Hulls => All.OfType<HullPart>().ToList();
		public IReadOnlyList<MeshPart> Meshes => All.OfType<MeshPart>().ToList();
		public IReadOnlyList<Part> Parts => All;

		public abstract class Part
		{
			public Surface Surface { get; protected set; }

			internal virtual void Dispose()
			{

			}
		}

		public class SpherePart : Part
		{
			internal RnSphereDesc_t native;

			public Sphere Sphere { get; init; }

			internal SpherePart( RnSphereDesc_t native, Surface surface )
			{
				this.native = native;
				Surface = surface;
				Sphere = native.m_Sphere;
			}

			internal override void Dispose()
			{
				native = default;
			}
		}


		public class CapsulePart : Part
		{
			internal RnCapsuleDesc_t native;

			public Capsule Capsule { get; init; }

			internal CapsulePart( RnCapsuleDesc_t native, Surface surface )
			{
				this.native = native;
				Surface = surface;
				Capsule = native.m_Capsule;
			}

			internal override void Dispose()
			{
				native = default;
			}
		}


		public class HullPart : Part
		{
			internal RnHullDesc_t native;
			internal RnHull_t hull;

			public BBox Bounds { get; init; }

			internal HullPart( RnHullDesc_t native, Surface surface )
			{
				this.native = native;
				Surface = surface;
				hull = native.GetHull();

				Bounds = hull.GetBbox();
			}

			/// <summary>
			/// For debug rendering
			/// </summary>
			public IEnumerable<Line> GetLines()
			{
				for ( int i = 0; i < hull.GetEdgeCount(); i++ )
				{
					hull.GetEdgeVertex( i, out var a, out var b );
					yield return new Line( a, b );
				}
			}

			public IEnumerable<Vector3> GetPoints()
			{
				for ( int i = 0; i < hull.GetVertexCount(); i++ )
				{
					yield return hull.GetVertex( i );
				}
			}

			internal override void Dispose()
			{
				native = default;
				hull = default;
			}
		}


		public class MeshPart : Part
		{
			internal RnMeshDesc_t native;
			internal RnMesh_t mesh;

			public BBox Bounds { get; init; }

			public Surface[] Surfaces { get; protected set; }

			internal MeshPart( RnMeshDesc_t native, Surface surface, Surface[] surfaces )
			{
				this.native = native;
				Surface = surface;
				mesh = native.GetMesh();

				if ( mesh.GetMaterialCount() > 0 )
				{
					Surfaces = surfaces;
				}

				Bounds = mesh.GetBbox();
			}

			/// <summary>
			/// For debug rendering
			/// </summary>
			public IEnumerable<Triangle> GetTriangles()
			{
				for ( int i = 0; i < mesh.GetTriangleCount(); i++ )
				{
					mesh.GetTriangle( i, out var a, out var b, out var c );
					yield return new Triangle( a, b, c );
				}
			}
			internal override void Dispose()
			{
				native = default;
				mesh = default;
			}
		}
	}

}

