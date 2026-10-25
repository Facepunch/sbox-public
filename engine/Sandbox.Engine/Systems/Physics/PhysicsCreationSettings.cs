using System.Runtime.InteropServices;

namespace Sandbox;

[Flags]
internal enum PhysicsBodyCreationFlags : uint
{
	Gravity = 1 << 0,
	AutoSleep = 1 << 1,
	Awake = 1 << 2,
	Bullet = 1 << 3,
	OverrideMassCenter = 1 << 4
}

[StructLayout( LayoutKind.Sequential )]
internal struct PhysicsBodySettings()
{
	public PhysicsBodyType BodyType = PhysicsBodyType.Static;
	public Vector3 Position;
	public Rotation Rotation = Rotation.Identity;
	public Vector3 Velocity;
	public Vector3 AngularVelocity;
	public float LinearDamping;
	public float AngularDamping;
	public float GravityScale = 1;
	public float SleepThreshold = 2;
	public float Mass;
	public float InertiaScale = 1;
	public Vector3 MassCenter;
	public PhysicsBodyCreationFlags Flags = PhysicsBodyCreationFlags.Gravity | PhysicsBodyCreationFlags.AutoSleep | PhysicsBodyCreationFlags.Awake;
	public uint MotionLocks;

	internal void SetLocks( PhysicsLock locks )
	{
		MotionLocks = (locks.X ? 1u : 0) | (locks.Y ? 2u : 0) | (locks.Z ? 4u : 0)
			| (locks.Pitch ? 8u : 0) | (locks.Yaw ? 16u : 0) | (locks.Roll ? 32u : 0);
	}
}

[Flags]
internal enum PhysicsShapeCreationFlags : uint
{
	Sensor = 1 << 0,
	IgnoreTraces = 1 << 1,
	NoMass = 1 << 2,
	Tags = 1 << 3,
	RebuildMass = 1 << 4,
	DefaultSolidTag = 1 << 5
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct PhysicsShapeSettings()
{
	public IntPtr SurfaceIndices;
	public fixed uint Tags[16];
	public Vector3 SurfaceVelocity;
	public float Friction = -1;
	public float Elasticity = -1;
	public float RollingResistance = -1;
	public int SurfaceIndex = -1;
	public uint CollisionFunctionMask = 3;
	public PhysicsShapeCreationFlags Flags = PhysicsShapeCreationFlags.RebuildMass;
	public int SurfaceCount;
}

internal abstract partial class PhysicsBodyInternal
{
	internal Collider CreationCollider { get; private set; }
	internal Surface CreationSurface { get; private set; }
	internal Surface[] CreationSurfaces { get; private set; }

	internal Surface ResolvedCreationSurface => CreationCollider?.Surface.IsValid() == true ? CreationCollider.Surface : CreationSurface;

	internal PhysicsShapeSettings GetShapeSettings( bool rebuildMass = true )
	{
		var settings = new PhysicsShapeSettings { SurfaceIndex = ResolvedCreationSurface?.Index ?? -1 };
		if ( !rebuildMass )
			settings.Flags &= ~PhysicsShapeCreationFlags.RebuildMass;

		var collider = CreationCollider;
		if ( collider is null )
			return settings;

		settings.SurfaceVelocity = collider.SurfaceVelocity;
		settings.Friction = collider.Friction ?? -1;
		settings.Elasticity = collider.Elasticity ?? -1;
		settings.RollingResistance = collider.RollingResistance ?? -1;
		settings.Flags |= PhysicsShapeCreationFlags.Tags | PhysicsShapeCreationFlags.DefaultSolidTag;
		settings.CollisionFunctionMask = collider.IsTrigger ? 0u : 1u;
		if ( collider.IsTrigger )
			settings.Flags |= PhysicsShapeCreationFlags.Sensor;
		if ( collider.ColliderFlags.Contains( ColliderFlags.IgnoreTraces ) )
			settings.Flags |= PhysicsShapeCreationFlags.IgnoreTraces;
		if ( collider.ColliderFlags.Contains( ColliderFlags.IgnoreMass ) )
			settings.Flags |= PhysicsShapeCreationFlags.NoMass;
		if ( collider.Rigidbody?.CollisionEventsEnabled ?? true )
			settings.CollisionFunctionMask |= 2;
		if ( collider.Rigidbody?.CollisionUpdateEventsEnabled ?? false )
			settings.CollisionFunctionMask |= 8;

		return settings;
	}

	internal ShapeCreationScope BeginShapeCreation( Collider collider ) => new( this, collider );
	internal SurfaceCreationScope UseShapeSurface( Surface surface, Surface[] surfaces = null ) => new( this, surface, surfaces );
	internal MassUpdateScope BatchMassUpdates() => new( this );
	internal abstract bool SetMassUpdatesDeferred( bool defer );

	internal ref struct MassUpdateScope : IDisposable
	{
		PhysicsBodyInternal body;
		readonly bool wasDeferred;

		internal MassUpdateScope( PhysicsBodyInternal body )
		{
			this.body = body;
			wasDeferred = body.SetMassUpdatesDeferred( true );
		}

		public void Dispose()
		{
			var target = body;
			body = null;
			if ( target.IsValid() )
				target.SetMassUpdatesDeferred( wasDeferred );
		}
	}

	internal ref struct ShapeCreationScope : IDisposable
	{
		PhysicsBodyInternal body;
		readonly Collider collider;
		readonly Surface surface;
		readonly Surface[] surfaces;
		MassUpdateScope mass;

		internal ShapeCreationScope( PhysicsBodyInternal body, Collider collider )
		{
			this.body = body;
			this.collider = body.CreationCollider;
			surface = body.CreationSurface;
			surfaces = body.CreationSurfaces;
			mass = body.BatchMassUpdates();
			body.CreationCollider = collider;
			body.CreationSurface = null;
			body.CreationSurfaces = null;
		}

		public void Dispose()
		{
			if ( body is not { } target )
				return;
			body = null;
			target.CreationCollider = collider;
			target.CreationSurface = surface;
			target.CreationSurfaces = surfaces;
			mass.Dispose();
		}
	}

	internal ref struct SurfaceCreationScope : IDisposable
	{
		PhysicsBodyInternal body;
		readonly Surface surface;
		readonly Surface[] surfaces;

		internal SurfaceCreationScope( PhysicsBodyInternal body, Surface surface, Surface[] surfaces )
		{
			this.body = body;
			this.surface = body.CreationSurface;
			this.surfaces = body.CreationSurfaces;
			body.CreationSurface = surface;
			body.CreationSurfaces = surfaces;
		}

		public void Dispose()
		{
			if ( body is not { } target )
				return;
			body = null;
			target.CreationSurface = surface;
			target.CreationSurfaces = surfaces;
		}
	}
}
