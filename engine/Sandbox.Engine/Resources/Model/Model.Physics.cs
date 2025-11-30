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
	public IEnumerable<Surface> Surfaces
	{
		get
		{
			for ( int i = 0; i < _surfaces.Count; i++ )
			{
				yield return GetSurface( (uint)i );
			}
		}
	}

	void Refresh()
	{
		_surfaces.Clear();

		var surfaceCount = native.GetSurfacePropertiesCount();
		if ( _surfaces.Capacity < surfaceCount ) _surfaces.Capacity = surfaceCount;

		for ( int i = 0; i < surfaceCount; i++ )
		{
			var s = native.GetSurfaceProperties( i );
			_surfaces.Add( s.IsValid ? s.m_name : "default" );
		}

		_tags.Clear();

		var attributeCount = native.GetCollisionAttributeCount();
		if ( _tags.Capacity < attributeCount ) _tags.Capacity = attributeCount;

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

		// Pre-calculate the Surface array once, instead of doing it 
		// inside every BodyPart/MeshPart constructor.
		var cachedSurfaceArray = new Surface[_surfaces.Count];
		for ( int i = 0; i < _surfaces.Count; i++ )
		{
			cachedSurfaceArray[i] = GetSurface( (uint)i );
		}

		foreach ( var part in _parts )
		{
			part.Dispose();
		}
		_parts.Clear();

		var partCount = native.GetPartCount();
		if ( _parts.Capacity < partCount ) _parts.Capacity = partCount;

		for ( int i = 0; i < partCount; i++ )
		{
			var tx = native.GetBoneCount() > 0 ? native.GetBindPose( i ) : Transform.Zero;
			var boneName = native.GetBoneCount() > 0 ? native.GetBoneName( i ) : "";

			_parts.Add( new BodyPart( this, boneName, native.GetPart( i ), tx, cachedSurfaceArray ) );
		}

		_joints.Clear();

		var jointCount = native.GetJointCount();
		if ( _joints.Capacity < jointCount ) _joints.Capacity = jointCount;

		for ( int i = 0; i < jointCount; i++ )
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

		private List<SpherePart> _spheres = new();
		private List<CapsulePart> _capsules = new();
		private List<HullPart> _hulls = new();
		private List<MeshPart> _meshes = new();
		private List<Part> _all = new();

		public float Mass => native.m_flMass;
		public float LinearDamping => native.m_flLinearDamping;
		public float AngularDamping => native.m_flAngularDamping;
		public bool OverrideMassCenter => native.m_bOverrideMassCenter;
		public Vector3 MassCenterOverride => native.m_vMassCenterOverride;

		internal BodyPart( PhysicsGroupDescription physicsGroupDescription, string boneName, VPhysXBodyPart_t vPhysXBodyPart_t, Transform transform, Surface[] cachedSurfaces )
		{
			Transform = transform;
			parent = physicsGroupDescription;
			native = vPhysXBodyPart_t;
			BoneName = boneName;

			for ( int i = 0; i < native.GetSphereCount(); i++ )
			{
				var p = native.GetSphere( i );
				var part = new SpherePart( p, parent.GetSurface( p.m_nSurfacePropertyIndex ) );
				_spheres.Add( part );
				_all.Add( part );
			}

			for ( int i = 0; i < native.GetCapsuleCount(); i++ )
			{
				var p = native.GetCapsule( i );
				var part = new CapsulePart( p, parent.GetSurface( p.m_nSurfacePropertyIndex ) );
				_capsules.Add( part );
				_all.Add( part );
			}

			for ( int i = 0; i < native.GetHullCount(); i++ )
			{
				var p = native.GetHull( i );
				var part = new HullPart( p, parent.GetSurface( p.m_nSurfacePropertyIndex ) );
				_hulls.Add( part );
				_all.Add( part );
			}

			var meshCount = native.GetMeshCount();
			if ( meshCount > 0 )
			{
				for ( int i = 0; i < meshCount; i++ )
				{
					var p = native.GetMesh( i );
					var part = new MeshPart( p, parent.GetSurface( p.m_nSurfacePropertyIndex ), cachedSurfaces );
					_meshes.Add( part );
					_all.Add( part );
				}
			}
		}

		internal void Dispose()
		{
			foreach ( var s in _all )
			{
				s.Dispose();
			}
			
			_spheres.Clear();
			_capsules.Clear();
			_hulls.Clear();
			_meshes.Clear();
			_all.Clear();
		}

		// Return the typed lists directly. 
		public IReadOnlyList<SpherePart> Spheres => _spheres;
		public IReadOnlyList<CapsulePart> Capsules => _capsules;
		public IReadOnlyList<HullPart> Hulls => _hulls;
		public IReadOnlyList<MeshPart> Meshes => _meshes;
		public IReadOnlyList<Part> Parts => _all;

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
				var edgeCount = hull.GetEdgeCount();
				for ( int i = 0; i < edgeCount; i++ )
				{
					hull.GetEdgeVertex( i, out var a, out var b );
					yield return new Line( a, b );
				}
			}

			public IEnumerable<Vector3> GetPoints()
			{
				var vertCount = hull.GetVertexCount();
				for ( int i = 0; i < vertCount; i++ )
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
				var triCount = mesh.GetTriangleCount();
				for ( int i = 0; i < triCount; i++ )
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

