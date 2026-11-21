using System.Numerics;

/// <summary>
/// Direction to stack objects in.
/// </summary>
public enum StackDirection
{
	[Title( "#tool.option.up" )]
	Up,
	[Title( "#tool.option.down" )]
	Down,
	[Title( "#tool.option.left" )]
	Left,
	[Title( "#tool.option.right" )]
	Right,
	[Title( "#tool.option.forward" )]
	Forward,
	[Title( "#tool.option.back" )]
	Back
}

/// <summary>
/// How to interpret the stack direction axis.
/// </summary>
public enum StackAlignMode
{
	/// <summary>
	/// Stack along world-space axes regardless of object orientation.
	/// </summary>
	[Title( "#tool.option.world" )]
	World,

	/// <summary>
	/// Stack along the target object's local axes.
	/// </summary>
	[Title( "#tool.option.object" )]
	Object
}

[Icon( "📚" )]
[ClassName( "stacker" )]
[Group( "#tool.group.building" )]
[Title( "#tool.name.stacker" )]
public sealed class StackerTool : ToolMode
{
	private const int MaxStackCount = 50;

	public override IEnumerable<string> TraceIgnoreTags => ["player", "constraint", "collision"];

	/// <summary>
	/// Number of copies to create.
	/// </summary>
	[Property, Sync, Range( 1, MaxStackCount ), Step( 1 ), Title( "#tool.setting.count" ), ClientEditable, Description( "#tool.setting.stackertool.stackcount.description" )]
	public float StackCount { get; set; } = 2;

	/// <summary>
	/// Rotation offset (degrees) around the first perpendicular axis of the stack direction.
	/// For Up/Down stacking this rotates around the right axis.
	/// </summary>
	[Property, Sync, Title( "#tool.setting.angle_x" ), Range( -180, 180 ), Step( 1 ), Description( "#tool.setting.stackertool.angleoffsetx.description" )]
	public float AngleOffsetX { get; set; } = 0f;

	/// <summary>
	/// Rotation offset (degrees) around the second perpendicular axis of the stack direction.
	/// For Up/Down stacking this rotates around the forward axis.
	/// </summary>
	[Property, Sync, Title( "#tool.setting.angle_y" ), Range( -180, 180 ), Step( 1 ), Description( "#tool.setting.stackertool.angleoffsety.description" )]
	public float AngleOffsetY { get; set; } = 0f;

	/// <summary>
	/// Extra gap (in units) between each stacked copy.
	/// </summary>
	[Property, Sync, Range( 0, 128 ), Title( "#tool.setting.gap" ), Description( "#tool.setting.stackertool.positionoffset.description" )]
	public float PositionOffset { get; set; } = 0f;

	/// <summary>
	/// Which direction to stack in.
	/// </summary>
	[Property, Sync, Title( "#tool.setting.direction" ), Description( "#tool.setting.stackertool.direction.description" )]
	public StackDirection Direction { get; set; } = StackDirection.Up;

	/// <summary>
	/// Whether to align the stack axis to the world or the target object.
	/// </summary>
	[Property, Sync, Title( "#tool.setting.alignment" ), Description( "#tool.setting.stackertool.alignmode.description" )]
	public StackAlignMode AlignMode { get; set; } = StackAlignMode.Object;

	/// <summary>
	/// When true, stacked copies will be frozen (motion disabled).
	/// </summary>
	[Property, Sync, Title( "#tool.setting.freeze" ), Description( "#tool.setting.stackertool.freezeall.description" )]
	public bool FreezeAll { get; set; } = true;

	public override string Description => "#tool.hint.stacker.description";

	protected override void OnStart()
	{
		base.OnStart();

		RegisterAction( ToolInput.Primary, () => "#tool.hint.stacker.stack", OnStack );
		RegisterAction( ToolInput.Secondary, () => "#tool.hint.stacker.cycle_alignment", CycleAlignment );
		RegisterAction( ToolInput.Reload, () => "#tool.hint.stacker.cycle_direction", CycleDirection );
	}

	void OnStack()
	{
		var select = TraceSelect();
		if ( !IsValidTarget( select ) ) return;

		SpawnStack( ResolveRoot( select.GameObject ) );
		ShootEffects( select );
	}

	public override void OnControl()
	{
		base.OnControl();

		var select = TraceSelect();

		IsValidState = IsValidTarget( select );
		if ( !IsValidState )
			return;

		var target = ResolveRoot( select.GameObject );
		var transforms = ComputeStackTransforms( target );

		DrawStackPreview( target, transforms );
	}

	/// <summary>
	/// Resolve to the networked root object so we clone the full prop, not a child collider.
	/// </summary>
	private static GameObject ResolveRoot( GameObject go )
	{
		return go?.Network?.RootGameObject ?? go;
	}

	/// <summary>
	/// Cycle through stack directions on reload.
	/// </summary>
	private void CycleDirection()
	{
		Direction = Direction switch
		{
			StackDirection.Up => StackDirection.Right,
			StackDirection.Right => StackDirection.Down,
			StackDirection.Down => StackDirection.Left,
			StackDirection.Left => StackDirection.Forward,
			StackDirection.Forward => StackDirection.Back,
			StackDirection.Back => StackDirection.Up,
			_ => StackDirection.Up
		};
	}

	/// <summary>
	/// Toggle between world and object alignment.
	/// </summary>
	private void CycleAlignment()
	{
		AlignMode = AlignMode == StackAlignMode.World
			? StackAlignMode.Object
			: StackAlignMode.World;
	}

	/// <summary>
	/// Check if the aimed-at object is a valid stacking target.
	/// </summary>
	private bool IsValidTarget( SelectionPoint select )
	{
		if ( !select.IsValid() ) return false;
		if ( select.IsWorld ) return false;
		if ( select.IsPlayer ) return false;

		var root = ResolveRoot( select.GameObject );
		if ( root.Tags.Contains( "constraint" ) ) return false;

		return true;
	}

	/// <summary>
	/// Get the local-space direction vector for the configured <see cref="Direction"/>.
	/// </summary>
	private static Vector3 GetLocalDirection( StackDirection dir )
	{
		return dir switch
		{
			StackDirection.Up => Vector3.Up,
			StackDirection.Down => Vector3.Down,
			StackDirection.Left => Vector3.Left,
			StackDirection.Right => Vector3.Right,
			StackDirection.Forward => Vector3.Forward,
			StackDirection.Back => Vector3.Backward,
			_ => Vector3.Up
		};
	}

	/// <summary>
	/// Get the two perpendicular axes for a given stack direction.
	/// X and Y angle offsets rotate around these respectively.
	/// </summary>
	private static (Vector3 axisX, Vector3 axisY) GetPerpendicularAxes( StackDirection dir )
	{
		return dir switch
		{
			StackDirection.Up or StackDirection.Down => (Vector3.Right, Vector3.Forward),
			StackDirection.Left or StackDirection.Right => (Vector3.Forward, Vector3.Up),
			StackDirection.Forward or StackDirection.Back => (Vector3.Right, Vector3.Up),
			_ => (Vector3.Right, Vector3.Forward)
		};
	}

	/// <summary>
	/// Build the per-step rotation from the X/Y angle offsets around the
	/// two axes perpendicular to the stack direction.
	/// </summary>
	private Rotation GetStepRotation()
	{
		if ( AngleOffsetX == 0f && AngleOffsetY == 0f )
			return Rotation.Identity;

		var (axisX, axisY) = GetPerpendicularAxes( Direction );
		return Rotation.FromAxis( axisX, AngleOffsetX ) * Rotation.FromAxis( axisY, AngleOffsetY );
	}

	/// <summary>
	/// Compute how far apart copies should be along the stack axis by projecting
	/// the object's oriented bounding box corners onto the actual world-space axis.
	/// </summary>
	private float GetStackExtent( GameObject target, Vector3 worldAxis )
	{
		var renderer = target.GetComponentInChildren<ModelRenderer>();
		BBox localBounds;

		if ( renderer.IsValid() && renderer.Model.IsValid() )
		{
			var mb = renderer.Model.Bounds;
			localBounds = new BBox( mb.Mins * renderer.WorldScale, mb.Maxs * renderer.WorldScale );
		}
		else
		{
			localBounds = new BBox( -Vector3.One * 8f, Vector3.One * 8f );
		}

		// Project all 8 corners of the oriented bounding box onto the axis
		var rot = target.WorldRotation;
		float min = float.MaxValue;
		float max = float.MinValue;

		for ( int i = 0; i < 8; i++ )
		{
			var corner = new Vector3(
				(i & 1) == 0 ? localBounds.Mins.x : localBounds.Maxs.x,
				(i & 2) == 0 ? localBounds.Mins.y : localBounds.Maxs.y,
				(i & 4) == 0 ? localBounds.Mins.z : localBounds.Maxs.z
			);

			var worldCorner = rot * corner;
			var proj = Vector3.Dot( worldCorner, worldAxis );
			min = MathF.Min( min, proj );
			max = MathF.Max( max, proj );
		}

		return (max - min) + PositionOffset;
	}

	/// <summary>
	/// Compute the world transforms for all stacked copies using iterative placement.
	/// Each copy is positioned relative to the previous one, so angle offsets
	/// produce correct arcs and circles rather than spirals.
	/// </summary>
	private Transform[] ComputeStackTransforms( GameObject target )
	{
		var localDir = GetLocalDirection( Direction );
		var basePos = target.WorldPosition;
		var baseRot = target.WorldRotation;

		// The initial world-space axis (before any angle offsets)
		var initialAxis = AlignMode == StackAlignMode.Object
			? baseRot * localDir
			: localDir;

		var extent = GetStackExtent( target, initialAxis );
		var stepAngle = GetStepRotation();

		var count = Math.Clamp( StackCount, 1, MaxStackCount );
		var transforms = new Transform[count.CeilToInt()];

		// Track the current step's rotation and position iteratively
		var prevPos = basePos;
		var prevRot = baseRot;

		for ( int i = 0; i < count; i++ )
		{
			// Apply the per-step angle offset to get this copy's rotation
			Rotation copyRot;
			if ( AlignMode == StackAlignMode.Object )
			{
				// Object mode: angle offset rotates in the object's local frame
				copyRot = prevRot * stepAngle;
			}
			else
			{
				// World mode: angle offset rotates in world space
				copyRot = stepAngle * prevRot;
			}

			// Compute the step direction from the *previous* copy's orientation
			Vector3 stepAxis;
			if ( AlignMode == StackAlignMode.Object )
			{
				stepAxis = prevRot * localDir;
			}
			else
			{
				stepAxis = localDir;
			}

			var copyPos = prevPos + stepAxis * extent;

			transforms[i] = new Transform( copyPos, copyRot, target.WorldScale );

			prevPos = copyPos;
			prevRot = copyRot;
		}

		return transforms;
	}

	/// <summary>
	/// Render ghost previews of each stacked copy.
	/// </summary>
	private void DrawStackPreview( GameObject target, Transform[] transforms )
	{
		var overlayMaterial = Material.Load( "materials/effects/duplicator_override.vmat" );

		foreach ( var renderer in target.GetComponentsInChildren<ModelRenderer>() )
		{
			var model = renderer.Model ?? Model.Cube;
			if ( model.IsError ) continue;

			// Pass skinned bones so posed ragdolls preview in their current pose
			var bones = renderer is SkinnedModelRenderer skinned ? skinned.GetBoneTransforms( false ) : null;
			var localTx = target.WorldTransform.ToLocal( renderer.WorldTransform );

			foreach ( var tx in transforms )
			{
				DebugOverlay.Model( model, transform: tx.ToWorld( localTx ), castShadows: false, materialOveride: overlayMaterial, localBoneTransforms: bones );
			}
		}
	}

	/// <summary>
	/// Server-side spawn: recomputes transforms from synced properties to prevent
	/// clients from sending arbitrary placement data.
	/// </summary>
	[Rpc.Host( NetFlags.OwnerOnly )]
	private void SpawnStack( GameObject target )
	{
		if ( !FireToolAction( ToolInput.Primary ) ) return;

		if ( !target.IsValid() ) return;

		var root = ResolveRoot( target );
		if ( root.Tags.Contains( "constraint" ) ) return;

		// Recompute transforms server-side from synced properties
		var transforms = ComputeStackTransforms( root );

		var blocksPerCopy = 0;
		foreach ( var _ in root.GetComponentsInChildren<BuilderProp>() )
			blocksPerCopy++;

		if ( blocksPerCopy > 0 )
		{
			var owner = Player.Network.Owner;
			if ( !BuilderPolicy.CheckCanBuild( owner ) ) return;
			if ( LimitsSystem.Get( Scene ) is { } limits && !limits.CheckBuilderLimit( owner, blocksPerCopy * transforms.Length ) ) return;
		}

		// batch these spawns
		using var x = Scene.BatchGroup();

		var undo = Player.Undo.Create();
		undo.Name = "#tool.undo.stack";
		undo.Icon = "📚";

		var clones = new List<GameObject>( transforms.Length );

		for ( int i = 0; i < transforms.Length; i++ )
		{
			var tx = transforms[i];

			var clone = root.Clone( new CloneConfig
			{
				Transform = new Transform( tx.Position, tx.Rotation ),
				StartEnabled = true
			} );

			clone.Tags.Add( "removable" );

			if ( FreezeAll )
			{
				foreach ( var rb in clone.GetComponentsInChildren<Rigidbody>() )
				{
					rb.MotionEnabled = false;
				}
			}

			Ownable.Set( clone, Player.Network.Owner );
			clone.NetworkSpawn( true, null );
			clones.Add( clone );
			Track( clone );
		}

		undo.Add( clones );

		FirePostToolAction( ToolInput.Primary );
	}
}
