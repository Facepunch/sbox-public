using Sandbox.Animation;

namespace Editor.Assets;

[AssetPreview( "anim" )]
public class PreviewClip( Asset asset ) : AssetPreview( asset )
{
	public override float PreviewWidgetCycleSpeed => _clip is not null && _clip.Duration > 0f ? 1f / _clip.Duration : 1f;
	public override bool IsAnimatedPreview => true;

	Clip _clip;
	IReadOnlyList<Skeleton.Bone> _bones;

	Transform[] _pose;

	public override async Task InitializeAsset()
	{
		await Task.Yield();

		_clip = Clip.Load( Asset.Path );
		if ( _clip is null || _clip.FrameCount == 0 )
			return;

		var skeleton = _clip.Skeleton;
		if ( skeleton is null || skeleton.BoneCount == 0 )
			return;

		_bones = skeleton.Bones;
		_pose = new Transform[_bones.Count];

		// Size the camera off the reference pose - sampling every frame to get a tight fit
		// isn't worth it, and the clip's skeleton isn't necessarily bound yet at this point
		var bounds = new BBox();
		foreach ( var bone in _bones )
			bounds = bounds.AddPoint( bone.Transform.Position );

		using ( Scene.Push() )
		{
			PrimaryObject = new GameObject( true, "preview animation clip" );
			PrimaryObject.WorldTransform = Transform.Zero;

			SceneSize = bounds.Size;
			SceneCenter = bounds.Center;
		}
	}

	public override void UpdateScene( float cycle, float timeStep )
	{
		// Not calling the base here - it spins PrimaryObject by the cycle, and the cycle is
		// what's driving the animation. Watching a clip play on a turntable is useless.
		using ( Scene.Push() )
		{
			if ( PrimaryObject.IsValid() )
			{
				FrameScene();
			}
		}

		TickScene( timeStep );

		if ( Gizmo.Settings is null || _clip is null || _clip.FrameCount == 0
			|| _bones is null || !_clip.IsBound || _clip.TrackCount < _bones.Count )
			return;

		SamplePose( _clip, cycle - MathF.Floor( cycle ), _bones, _pose );
		DrawBones();
	}

	public static void SamplePose( Clip clip, float position, IReadOnlyList<Skeleton.Bone> bones, Transform[] pose )
	{
		if ( bones is null )
			return;

		var additive = clip.IsAdditive;
		for ( var i = 0; i < bones.Count; i++ )
		{
			var local = clip.GetLocalTransform( position, i );
			if ( additive )
			{
				var reference = bones[i].LocalTransform;
				// Additive channels are parent-space deltas, not ordinary transforms to compose.
				local = new Transform( reference.Position + local.Position,
					reference.Rotation * local.Rotation, reference.Scale + local.Scale );
			}
			var parent = bones[i].ParentIndex;

			pose[i] = parent >= 0 ? pose[parent].ToWorld( local ) : local;
		}
	}

	void DrawBones()
	{
		if ( _bones is null || !PrimaryObject.IsValid() )
			return;

		var root = PrimaryObject.WorldTransform;

		var boneColor = Theme.Blue;
		var jointColor = Theme.Yellow;

		foreach ( var bone in _bones )
		{
			var tx = root.ToWorld( _pose[bone.Index] );
			var parentTx = bone.ParentIndex >= 0 ? root.ToWorld( _pose[bone.ParentIndex] ) : root;

			using var scope = Gizmo.Scope( $"Bone{bone.Index}", tx );

			var radius = Math.Min( 1f, tx.Position.Distance( Gizmo.CameraTransform.Position ) / 64f );

			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.LineThickness = 1f;

			Gizmo.Draw.Color = boneColor.WithAlpha( 0.6f );
			Gizmo.Draw.Line( 0f, tx.PointToLocal( parentTx.Position ) );

			Gizmo.Draw.Color = jointColor;
			Gizmo.Draw.LineCircle( 0f, radius );
		}
	}
}
