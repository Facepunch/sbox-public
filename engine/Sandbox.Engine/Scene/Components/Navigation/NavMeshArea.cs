using Sandbox.Engine.Resources;
using Sandbox.Navigation;
using Sandbox.Volumes;

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
	[Property, MakeDirty]
	public bool IsBlocker { get; set; } = true;

	/// <summary>
	/// The NavMesh area definition to apply to this area.
	/// </summary>
	[Property, MakeDirty]
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
	private NavMeshAreaData _navMeshArea;

	protected override void OnDirty()
	{
		UpdateNavMeshArea();
	}

	internal override void OnEnabledInternal()
	{
		UpdateNavMeshArea();
		Transform.OnTransformChanged += OnTransformChanged;
		base.OnEnabledInternal();
	}

	internal override void OnDisabledInternal()
	{
		Transform.OnTransformChanged -= OnTransformChanged;
		RemoveNavMeshArea();
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

	private void UpdateNavMeshArea()
	{
		// Create area if it doesn't exist
		if ( _navMeshArea == null )
		{
			_navMeshArea = new NavMeshAreaData();
			_navMeshArea.IsBlocked = IsBlocker;
			_navMeshArea.AreaDefinition = Area;
			Scene.NavMesh.AddSpatiaData( _navMeshArea );
		}

		// Update the properties
		_navMeshArea.Volume = SceneVolume;
		_navMeshArea.IsBlocked = IsBlocker;
		_navMeshArea.AreaDefinition = Area;
		_navMeshArea.LocalBounds = SceneVolume.GetBounds();
		_navMeshArea.WorldBounds = _navMeshArea.LocalBounds.Transform( WorldTransform );
		_navMeshArea.Transform = WorldTransform;
		_navMeshArea.HasChanged = true;
	}

	/// <summary>
	/// Removes the current NavMeshArea
	/// </summary>
	private void RemoveNavMeshArea()
	{
		if ( _navMeshArea != null )
		{
			_navMeshArea.IsPendingRemoval = true;
			_navMeshArea = null;
		}
	}

	/// <summary>
	/// Called when the transform changes
	/// </summary>
	private void OnTransformChanged()
	{
		UpdateNavMeshArea();
	}
}
