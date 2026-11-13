using Sandbox.Engine.Resources;
using Sandbox.Navigation;
using Sandbox.Volumes;
using System.Numerics;

namespace Sandbox;

/// <summary>
/// An area that influences the NavMesh generation.
/// Areas can be used to block off parts of the NavMesh.
/// Static areas have almost no performance overhead.
/// Moving areas at runtime will have an impact on performance if done excessively.
/// </summary>
[Expose]
[Title( "NavMesh - Area" )]
[Category( "Navigation" )]
[Icon( "crop" )]
[EditorHandle( "materials/gizmo/navmeshagent.png" )]
[Alias( "NavAgent" )]
public class NavMeshArea : VolumeComponent, Component.ExecuteInEditor
{
	/// <summary>
	/// Whether navmesh generation in this area will be completely disabled.
	/// </summary>
	[Property]
	public bool IsBlocker { get; set; } = true;

	/// <summary>
	/// The NavMesh area definition to apply to this area.
	/// </summary>
	[Property]
	public NavMeshAreaDefinition Area { get; set; }

	/// <summary>
	/// The collider this area's shape is based on.
	/// In almost every case, you will want to use a trigger collider for this.
	/// </summary>
	[Property, Hide]
	[Obsolete( "Use the SceneVolume property inherited from VolumeComponent instead" )]
	public Collider LinkedCollider
	{
		get => _linkedCollider;
		set
		{
			_linkedCollider = value;
		}
	}

	protected override async Task OnLoad( LoadingContext context )
	{
		if ( _linkedCollider == null ) return;

		context.Title = "Converting Collider";

		await ConvertColliderToSceneVolumeLoadTask();
	}

	private Collider _linkedCollider;

	internal override void OnEnabledInternal()
	{
		Scene.NavMesh.AddArea( this );
		base.OnEnabledInternal();
	}

	internal override void OnDisabledInternal()
	{
		Scene.NavMesh?.RemoveArea( this );
		base.OnDisabledInternal();
	}

	/// <summary>
	/// Legacy support:
	/// Convert the linked collider to a SceneVolume and clear the reference
	/// </summary>
	private async Task ConvertColliderToSceneVolumeLoadTask()
	{
		if ( _linkedCollider == null )
			return;

		SceneVolume volume = new SceneVolume();

		// Check for specific collider types
		if ( _linkedCollider is SphereCollider sphereCollider )
		{
			volume.Type = SceneVolume.VolumeTypes.Sphere;
			volume.Sphere = new Sphere( sphereCollider.Center, sphereCollider.Radius );
		}
		else if ( _linkedCollider is BoxCollider boxCollider )
		{
			volume.Type = SceneVolume.VolumeTypes.Box;
			volume.Box = BBox.FromPositionAndSize( boxCollider.Center, boxCollider.Scale );
		}
		else if ( _linkedCollider is CapsuleCollider capsuleCollider )
		{
			volume.Type = SceneVolume.VolumeTypes.Capsule;
			volume.Capsule = new Capsule( capsuleCollider.Start, capsuleCollider.End, capsuleCollider.Radius );
		}
		else if ( _linkedCollider is PlaneCollider planeCollider )
		{
			volume.Type = SceneVolume.VolumeTypes.Box;
			volume.Box = BBox.FromPositionAndSize( planeCollider.Center, new Vector3( planeCollider.Scale, 8f ) );
		}
		else
		{
			// Wait until physics are ready.
			await GameTask.DelayRealtimeSeconds( 0.5f );

			volume.Type = SceneVolume.VolumeTypes.Box;
			volume.Box = _linkedCollider.LocalBounds;
		}

		SceneVolume = volume;

		_linkedCollider = null;
	}

	// A blocker keeps agents one agent radius away, like a wall does
	internal Nav.NavVolume CreateVolume( float agentRadius )
	{
		var shape = SceneVolume;
		var result = new Nav.NavVolume
		{
			Area = (byte)(IsBlocker ? 0 : Scene.NavMesh.AreaDefinitionToId( Area )),
			Margin = IsBlocker ? agentRadius : 0,
			LocalToWorld = Matrix4x4.Identity
		};

		if ( shape.Type == SceneVolume.VolumeTypes.Infinite )
		{
			result.Shape = Nav.VolumeShape.Everything;
			return result;
		}

		var transform = WorldTransform;
		result.LocalToWorld = Matrix4x4.CreateScale( transform.Scale ) * Matrix4x4.CreateFromQuaternion( transform.Rotation ) * Matrix4x4.CreateTranslation( transform.Position );

		if ( shape.Type == SceneVolume.VolumeTypes.Sphere )
		{
			result.Shape = Nav.VolumeShape.Sphere;
			result.A = shape.Sphere.Center;
			result.Radius = shape.Sphere.Radius;
		}
		else if ( shape.Type == SceneVolume.VolumeTypes.Capsule )
		{
			result.Shape = Nav.VolumeShape.Capsule;
			result.A = shape.Capsule.CenterA;
			result.B = shape.Capsule.CenterB;
			result.Radius = shape.Capsule.Radius;
		}
		else
		{
			result.Shape = Nav.VolumeShape.Box;
			result.A = shape.Box.Mins;
			result.B = shape.Box.Maxs;
		}
		return result;
	}
}
