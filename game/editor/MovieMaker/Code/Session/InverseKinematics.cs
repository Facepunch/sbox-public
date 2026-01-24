using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Properties;
using Sandbox.Physics;
using System.Linq;
using System.Text.RegularExpressions;

namespace Editor.MovieMaker;

#nullable enable

internal sealed class SessionInverseKinematics
{
	private readonly Session _session;

	private Vector2 _lastMousePos;
	private Transform _worldBoneDragStart;
	private bool _boneDragCancelled;
	private IkBone? _hoveredBone;

	private readonly record struct IkBone( TrackView BonesTrackView, BoneCollection.Bone Bone );

	public SessionInverseKinematics( Session session )
	{
		_session = session;
	}

	private TrackView? GetBonesTrackView( TrackView selectedTrackView )
	{
		while ( selectedTrackView.Track is not IReferenceTrack<GameObject> )
		{
			if ( selectedTrackView.Parent is not { } parentView ) return null;

			selectedTrackView = parentView;
		}

		if ( selectedTrackView.Find( nameof( SkinnedModelRenderer ) ) is not { Track: IReferenceTrack<SkinnedModelRenderer> } rendererView ) return null;
		if ( rendererView.Find( "Bones" ) is not { } bonesTrackView ) return null;
		if ( rendererView.Target is not ITrackReference<SkinnedModelRenderer> ) return null;

		return bonesTrackView;
	}

	public void DrawGizmos()
	{
		var hasFocus = Gizmo.CurrentRay != default;

		IkBone? newHoveredBone = null;

		SkinnedModelRenderer? draggedRenderer = null;
		TrackView? draggedBonesTrackView = null;
		BoneCollection.Bone? draggedBone = null;
		Vector3? localTargetPosition = null;
		Rotation? localTargetRotation = null;

		var bonesTrackViews = _session.TrackList.UnlockedTracks
			.Where( x => x.Name == "Bones" )
			.Where( x => x.Track is IPropertyTrack<BoneAccessor> )
			.OfType<TrackView?>();

		foreach ( var bonesTrackView in bonesTrackViews )
		{
			if ( bonesTrackView is null ) continue;
			if ( bonesTrackView.Parent?.Target is not ITrackReference<SkinnedModelRenderer> { IsBound: true, Value: { } renderer } ) continue;
			if ( renderer.Model is not { } model || model.BoneCount == 0 ) continue;

			foreach ( var bone in model.Bones.AllBones )
			{
				var boneTrackView = bonesTrackView.Find( bone.Name );

				if ( boneTrackView?.IsLocked is true ) continue;

				if ( !ShouldShowBoneGizmo( bone.Name ) ) continue;
				if ( !renderer.TryGetBoneTransform( bone, out var worldBoneTransform ) ) continue;

				if ( bone.Parent is null || !renderer.TryGetBoneTransform( bone.Parent, out var parentWorldTransform ) )
				{
					parentWorldTransform = renderer.WorldTransform;
				}

				using var scope = Gizmo.Scope( $"BoneHandle{bone.Index}", worldBoneTransform );

				var radius = Math.Min( 1f, worldBoneTransform.Position.Distance( Gizmo.CameraTransform.Position ) / 64f );
				var handleSphere = new Sphere( 0f, radius );
				var modelWorldTransform = renderer.WorldTransform;

				var isHovered = hasFocus ? Gizmo.IsHovered : _hoveredBone?.Bone == bone;
				var isSelected = boneTrackView?.IsSelected is true;
				var isHoveredOrSelected = isHovered || isSelected;
				var isLocked = bone.IsLocked( bonesTrackView );
				var hasBody = _dragTarget is not null && _boneBodies.ContainsKey( bone ) && !_lockedBones.Contains( bone );

				Gizmo.Draw.Color = (hasBody ? Color.Yellow : Color.White).WithAlpha( isHovered || isSelected ? 1f : 0.25f );
				Gizmo.Draw.LineThickness = isHovered || isSelected ? 2f : 1f;
				Gizmo.Draw.Line( 0f, worldBoneTransform.PointToLocal( parentWorldTransform.Position ) );


				if ( isLocked )
				{
					Gizmo.Draw.Sprite( handleSphere.Center, radius * 4f, "https://files.facepunch.com/ziks/2025-06-06/lock.png" );
				}
				else if ( _dragTarget is null && isHoveredOrSelected || hasBody )
				{
					Gizmo.Draw.LineCircle( handleSphere.Center, radius );

					if ( isHoveredOrSelected )
					{
						Gizmo.Draw.SolidSphere( handleSphere.Center, handleSphere.Radius * 0.5f, 6, 6 );
					}
				}

				Gizmo.Hitbox.DepthBias = 0.01f;
				Gizmo.Hitbox.Sphere( handleSphere );

				if ( !isHovered ) continue;

				newHoveredBone = new IkBone( bonesTrackView, bone );

				var textPos = Gizmo.Transform.Position + Gizmo.CameraTransform.Right * radius * 2f;

				Gizmo.Draw.Text( bone.Name, Gizmo.Transform.ToLocal( new Transform( textPos ) ), flags: TextFlag.LeftCenter );

				if ( Gizmo.WasLeftMousePressed && boneTrackView is not null )
				{
					boneTrackView.Select();
					_session.Editor.TimelinePanel?.Timeline.ScrollToTrack( boneTrackView );
				}

				if ( Gizmo.IsShiftPressed && Gizmo.WasLeftMouseReleased )
				{
					bone.SetLocked( bonesTrackView, !bone.IsLocked( bonesTrackView ) );
					continue;
				}

				if ( !Gizmo.IsLeftMouseDown || Gizmo.IsShiftPressed )
				{
					_boneDragCancelled = false;
					continue;
				}

				if ( Application.IsKeyDown( KeyCode.Escape ) )
				{
					CancelDraggingBone( renderer );
					continue;
				}

				if ( _boneDragCancelled ) continue;

				var rotating = Application.IsKeyDown( KeyCode.E );

				if ( Gizmo.WasLeftMousePressed )
				{
					_lastMousePos = Application.CursorPosition;
					_worldBoneDragStart = worldBoneTransform;

					// To reset physics sim

					StopDraggingBone();
				}

				var worldPlane = new Plane( _worldBoneDragStart.Position, Gizmo.CameraTransform.Forward );
				var ray = Gizmo.CurrentRay;

				if ( !worldPlane.TryTrace( ray, out var worldHit, true ) ) continue;

				Gizmo.Draw.SolidSphere( worldBoneTransform.PointToLocal( _worldBoneDragStart.Position ), radius * 0.25f );

				draggedBonesTrackView = bonesTrackView;
				draggedRenderer = renderer;
				draggedBone = bone;

				if ( Application.CursorPosition.AlmostEqual( _lastMousePos ) ) continue;

				_lastMousePos = Application.CursorPosition;

				if ( rotating )
				{
					var right = Vector3.Cross( Gizmo.CameraTransform.Forward, _worldBoneDragStart.Forward ).Normal;
					var delta = right.Dot( worldHit - _worldBoneDragStart.Position );

					localTargetRotation = modelWorldTransform.RotationToLocal( _worldBoneDragStart.Rotation * Rotation.FromRoll( delta * 5f ) );
				}
				else
				{
					localTargetPosition = modelWorldTransform.PointToLocal( worldHit );
				}
			}
		}

		if ( hasFocus )
		{
			_hoveredBone = newHoveredBone;
		}

		if ( draggedBone is null )
		{
			StopDraggingBone();
		}
		else if ( draggedRenderer is not null && draggedBonesTrackView is not null )
		{
			DragBone( draggedRenderer, new IkBone( draggedBonesTrackView, draggedBone ), localTargetPosition, localTargetRotation );
		}
	}

	public void ShowContextMenu( EditorEvent.ShowContextMenuEvent ev )
	{
		if ( _hoveredBone is not { } target ) return;

		ev.Menu.AddSeparator();

		if ( target.Bone.IsLocked( target.BonesTrackView ) )
		{
			ev.Menu.AddOption( "Unlock Bone", "lock_open", () => target.Bone.SetLocked( target.BonesTrackView, false ) );
		}
		else
		{
			ev.Menu.AddOption( "Lock Bone", "lock", () => target.Bone.SetLocked( target.BonesTrackView, true ) );
		}
	}

	private static string[] FilteredBoneNameParts { get; } =
	[
		"twist", "helper", "target", "rule", "clothing",
		"matrix", "ik", "eye", "lid", "ear", "hold"
	];

	private static Regex FilteredBoneNameRegex { get; } =
		new Regex( string.Join( "|", FilteredBoneNameParts ), RegexOptions.IgnoreCase );

	private static bool ShouldShowBoneGizmo( string boneName )
	{
		return !FilteredBoneNameRegex.IsMatch( boneName );
	}

	private PhysicsWorld? _ikWorld;
	private IkBone? _dragTarget;
	private readonly HashSet<BoneCollection.Bone> _activeBones = new();
	private readonly HashSet<BoneCollection.Bone> _lockedBones = new();
	private readonly Dictionary<BoneCollection.Bone, Transform> _initialLocalBoneTransforms = new();
	private readonly Dictionary<BoneCollection.Bone, Transform> _lastStepTransforms = new();
	private readonly Dictionary<BoneCollection.Bone, PhysicsBody> _boneBodies = new();

	private void DragBone( SkinnedModelRenderer renderer, IkBone target, Vector3? localTargetPosition, Rotation? localTargetRotation )
	{
		if ( _dragTarget != target )
		{
			StopDraggingBone();
		}

		_dragTarget = target;

		var boneTrackView = target.BonesTrackView.Find( target.Bone.Name );

		if ( boneTrackView?.IsLocked is true ) return;
		if ( localTargetPosition is null && localTargetRotation is null ) return;

		var draggedBone = target.Bone;
		var firstTime = false;

		if ( _ikWorld is null )
		{
			StartDraggingBone( renderer, target );

			firstTime = true;
		}

		if ( _ikWorld is not { } world ) return;
		if ( !_boneBodies.TryGetValue( draggedBone, out var draggedBody ) ) return;

		if ( firstTime )
		{
			foreach ( var bone in _activeBones )
			{
				EditorEvent.RunInterface<EditorEvent.ISceneEdited>( x => x.ComponentPreEdited( renderer, $"Bones.{bone.Name}" ) );
			}
		}

		// Reset to where bones were when drag started

		ResetBoneBodies();
		world.Step( 0.125f );
		ResetBoneBodies();

		// Create control bodies / joints

		var controlBody = new PhysicsBody( _ikWorld )
		{
			BodyType = PhysicsBodyType.Static,
			Position = draggedBody.Position,
			Rotation = draggedBody.Rotation
		};

		var initialTransform = controlBody.Transform;

		var targetTransform = new Transform(
			localTargetPosition ?? draggedBody.Position,
			localTargetRotation ?? draggedBody.Rotation );

		PhysicsJoint? parentJoint = null;

		if ( localTargetRotation is null )
		{
			// Just dragging position: let the bone rotate

			PhysicsJoint.CreateBallSocket(
				PhysicsPoint.Local( controlBody ),
				PhysicsPoint.Local( draggedBody ) );

			if ( draggedBone.Parent is { } parentBone && _boneBodies.TryGetValue( parentBone, out var parentBody ) )
			{
				// If we have no target rotation, try to keep same relative rotation to parent

				var parentLocalTransform = parentBody.Transform.ToLocal( draggedBody.Transform );

				parentJoint = PhysicsJoint.CreateFixed(
					PhysicsPoint.Local( draggedBody ),
					PhysicsPoint.Local( parentBody, parentLocalTransform.Position, parentLocalTransform.Rotation ) );
			}
		}
		else
		{
			// Dragging rotation: use fixed joint to match target rotation

			var joint = PhysicsJoint.CreateFixed(
				PhysicsPoint.Local( controlBody ),
				PhysicsPoint.Local( draggedBody ) );

			joint.SpringLinear = new PhysicsSpring( 0f, 1f );
			joint.SpringAngular = new PhysicsSpring( 0f, 1f );
		}

		_lastStepTransforms.Clear();

		const float velocityLimit = 10f;
		const float controlVelocityLimit = 5f;

		const float simTime = 4f;
		const int steps = 128;

		var controlMoveTime = Math.Max( 1f, (targetTransform.Position - initialTransform.Position).Length / controlVelocityLimit );

		const float dt = simTime / steps;

		try
		{
			// Simulate some steps

			for ( var i = 0; i < steps; ++i )
			{
				var time = (i + 1f) * simTime / steps;

				controlBody.Transform = Transform.Lerp( initialTransform, targetTransform, time / controlMoveTime, true );

				foreach ( var (bone, body) in _boneBodies )
				{
					_lastStepTransforms[bone] = body.Transform;
				}

				world.Step( dt );

				if ( _boneBodies.Values.Any( body => body.Velocity.Length > velocityLimit ) ) break;
			}

			ApplyBoneTransforms( renderer, bone => _lastStepTransforms.TryGetValue( bone, out var transform ) ? transform : null );
		}
		finally
		{
			controlBody.Remove();
			parentJoint?.Remove();
		}
	}

	private void ResetBoneBodies()
	{
		foreach ( var (bone, body) in _boneBodies )
		{
			if ( _initialLocalBoneTransforms.TryGetValue( bone, out var transform ) )
			{
				body.Transform = transform;
			}

			body.Velocity = 0f;
			body.AngularVelocity = 0f;
			body.ClearForces();
		}
	}

	private void ApplyBoneTransforms( SkinnedModelRenderer renderer, Func<BoneCollection.Bone, Transform?> getLocalBoneTransform )
	{
		var modelWorldTransform = renderer.WorldTransform;

		foreach ( var bone in _activeBones.OrderBy( x => x.Index ) )
		{
			if ( getLocalBoneTransform( bone ) is not { } localBoneTransform ) continue;

			var parentTransform = bone.Parent is { } parent
				? getLocalBoneTransform( parent ) ?? modelWorldTransform.ToLocal( renderer.SceneModel.GetBoneWorldTransform( parent.Index ) )
				: Transform.Zero;

			var parentSpaceTransform = parentTransform.ToLocal( localBoneTransform );

			MovieBoneAnimatorSystem.Current.SetParentSpaceBone( renderer, bone.Index, parentSpaceTransform );
		}

		foreach ( var bone in _activeBones.OrderBy( x => x.Index ) )
		{
			EditorEvent.RunInterface<EditorEvent.ISceneEdited>( x =>
				x.ComponentEdited( renderer, $"Bones.{bone.Name}" ) );
		}
	}

	private void StartDraggingBone( SkinnedModelRenderer renderer, IkBone target )
	{
		foreach ( var (_, body) in _boneBodies )
		{
			body.Remove();
		}

		_boneBodies.Clear();

		_ikWorld ??= new PhysicsWorld();

		_activeBones.Clear();
		_lockedBones.Clear();

		var bone = target.Bone;
		var bonesTrackView = target.BonesTrackView;

		if ( renderer.Model is not { } model ) return;

		// AnimGraph will interfere with manual animation

		renderer.UseAnimGraph = false;

		var rootBone = bone.GetRoot( bonesTrackView );

		// Make sure dragged bone is in the skeleton

		AddBoneChain( bonesTrackView, bone, rootBone );

		var constrainedBones = model.Bones.AllBones
			.Where( x => x.IsLocked( bonesTrackView ) && x.GetRoot( bonesTrackView ) == rootBone );

		foreach ( var lockedBone in constrainedBones )
		{
			// Any locked bones that are below rootBone need to be in the skeleton too,
			// so they constrain the pose

			AddBoneChain( bonesTrackView, lockedBone, rootBone );
		}

		if ( _lockedBones.Count == 0 )
		{
			// Default to locking the root if nothing else is locked

			_lockedBones.Add( model.Bones.Root );
		}

		SetupRagdoll( renderer.WorldTransform, renderer.SceneModel, model.Bones, model.Physics );

		foreach ( var lockedBone in _lockedBones )
		{
			if ( _boneBodies.TryGetValue( lockedBone, out var lockedBody ) )
			{
				lockedBody.BodyType = PhysicsBodyType.Static;
			}
		}

		if ( _boneBodies.TryGetValue( bone, out var draggedBody ) )
		{
			draggedBody.BodyType = PhysicsBodyType.Dynamic;
		}
	}

	/// <summary>
	/// Add <paramref name="bone"/> to <see cref="_lockedBones"/>,
	/// based on whether the corresponding child track of <paramref name="bonesTrackView"/> is
	/// <see cref="ModelExtensions.IsLocked"/>. If there is no corresponding track, treat it as locked.
	/// If the bone isn't locked, recurse to all neighbouring bones (parent / children).
	/// </summary>
	private void AddBoneChain( TrackView bonesTrackView, BoneCollection.Bone bone, BoneCollection.Bone root )
	{
		if ( !ShouldShowBoneGizmo( bone.Name ) ) return;

		while ( bone is not null )
		{
			if ( !_activeBones.Add( bone ) ) break;

			if ( bone.IsLocked( bonesTrackView ) )
			{
				_lockedBones.Add( bone );
			}

			if ( bone == root ) break;

			bone = bone.Parent;
		}
	}

	private void SetupRagdoll( Transform modelWorldTransform, SceneModel model, BoneCollection bones, PhysicsGroupDescription physics )
	{
		// Adapted from Sandbox.Ragdoll

		// Map bone index to body part index

		var indices = physics.Parts.Select( ( x, i ) =>
			{
				var sourceBone = bones.GetBone( x.BoneName );

				return _activeBones.Contains( sourceBone )
					? (Bone: sourceBone.Index, Part: i)
					: (Bone: -1, Part: -1);
			} )
			.Where( x => x is { Bone: > -1, Part: > -1 } )
			.ToArray();

		var bodyPartToBone = indices
			.ToDictionary( x => x.Part, x => x.Bone );

		// Set up physics bodies

		_initialLocalBoneTransforms.Clear();

		foreach ( var bone in bones.AllBones )
		{
			if ( !_activeBones.Contains( bone ) ) continue;

			var localTransform = modelWorldTransform.ToLocal( model.GetBoneWorldTransform( bone.Index ) );

			var body = new PhysicsBody( _ikWorld! )
			{
				Transform = localTransform,
				BodyType = PhysicsBodyType.Dynamic,
				Mass = 100f,
				LinearDamping = 100f, // part.LinearDamping;
				AngularDamping = 100f, // part.AngularDamping;
				GravityEnabled = false
			};

			_boneBodies[bone] = body;
			_initialLocalBoneTransforms[bone] = localTransform;

			body.AddSphereShape( new Sphere( 0f, 1f ) )
				.EnableSolidCollisions = false;
		}

		const float extraAngleLimit = 30f;

		var joints = new HashSet<(int Bone1, int Bone2)>();

		// Add joints from model physics

		foreach ( var jointDesc in physics.Joints )
		{
			if ( !bodyPartToBone.TryGetValue( jointDesc.Body1, out var boneIndex1 ) ) continue;
			if ( !bodyPartToBone.TryGetValue( jointDesc.Body2, out var boneIndex2 ) ) continue;

			var bone1 = bones.AllBones[boneIndex1];
			var bone2 = bones.AllBones[boneIndex2];

			if ( !_boneBodies.TryGetValue( bone1, out var body1 ) ) continue;
			if ( !_boneBodies.TryGetValue( bone2, out var body2 ) ) continue;

			var point1 = new PhysicsPoint( body1, jointDesc.Frame1.Position, jointDesc.Frame1.Rotation );
			var point2 = new PhysicsPoint( body2, jointDesc.Frame2.Position, jointDesc.Frame2.Rotation );

			switch ( jointDesc.Type )
			{
				case PhysicsGroupDescription.JointType.Hinge:
					{
						var hingeJoint = PhysicsJoint.CreateHinge( point1, point2 );

						if ( jointDesc.EnableTwistLimit )
						{
							hingeJoint.MinAngle = jointDesc.TwistMin - extraAngleLimit;
							hingeJoint.MaxAngle = jointDesc.TwistMax + extraAngleLimit;
						}

						break;
					}
				case PhysicsGroupDescription.JointType.Ball:
					{
						var ballJoint = PhysicsJoint.CreateBallSocket( point1, point2 );

						if ( jointDesc.EnableSwingLimit )
						{
							ballJoint.SwingLimitEnabled = true;
							ballJoint.SwingLimit = new Vector2( jointDesc.SwingMin - extraAngleLimit, jointDesc.SwingMax + extraAngleLimit );
						}

						if ( jointDesc.EnableTwistLimit )
						{
							ballJoint.TwistLimitEnabled = true;
							ballJoint.TwistLimit = new Vector2( jointDesc.TwistMin - extraAngleLimit, jointDesc.TwistMax + extraAngleLimit );
						}

						break;
					}
				case PhysicsGroupDescription.JointType.Fixed:
					{
						var fixedJoint = PhysicsJoint.CreateFixed( point1, point2 );

						fixedJoint.SpringLinear = new PhysicsSpring( 0f, 1f );
						fixedJoint.SpringAngular = new PhysicsSpring( 0f, 1f );
						break;
					}
				case PhysicsGroupDescription.JointType.Slider:
					{
						PhysicsJoint.CreateSlider( point1, point2, jointDesc.LinearMin, jointDesc.LinearMax );
						break;
					}

				default:
					continue;
			}

			joints.Add( (bone1.Index, bone2.Index) );
			joints.Add( (bone2.Index, bone1.Index) );
		}

		// Add any missing joints so the skeleton is fully connected

		foreach ( var bone in bones.AllBones )
		{
			if ( bone.Parent is null ) continue;

			if ( !_activeBones.Contains( bone ) ) continue;
			if ( !_activeBones.Contains( bone.Parent ) ) continue;

			if ( !_boneBodies.TryGetValue( bone, out var body1 ) ) continue;
			if ( !_boneBodies.TryGetValue( bone.Parent, out var body2 ) ) continue;

			if ( !joints.Add( (bone.Index, bone.Parent.Index) ) ) continue;
			if ( !joints.Add( (bone.Parent.Index, bone.Index) ) ) continue;

			var parentLocalTransform = bone.Parent.LocalTransform.ToLocal( bone.LocalTransform );

			var joint = PhysicsJoint.CreateFixed(
				new PhysicsPoint( body1 ),
				new PhysicsPoint( body2, parentLocalTransform.Position, parentLocalTransform.Rotation ) );

			joint.SpringLinear = new PhysicsSpring( 0f, 1f );
			joint.SpringAngular = new PhysicsSpring( 0f, 1f );
		}
	}

	private void StopDraggingBone()
	{
		_dragTarget = null;

		_boneBodies.Clear();
		_initialLocalBoneTransforms.Clear();

		_ikWorld?.Delete();
		_ikWorld = null;
	}

	private void CancelDraggingBone( SkinnedModelRenderer renderer )
	{
		_boneDragCancelled = true;

		ApplyBoneTransforms( renderer, bone => _initialLocalBoneTransforms.TryGetValue( bone, out var transform ) ? transform : null );
	}
}
