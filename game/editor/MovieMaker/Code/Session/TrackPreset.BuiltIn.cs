using Sandbox.MovieMaker.Properties;

namespace Editor.MovieMaker;

#nullable enable

public partial record TrackPreset
{
	private static TrackPreset TransformPreset { get; } = new(
		new TrackPresetMetadata( "Transform", "Common",
			Description: "Includes the LocalPosition, LocalRotation, and Enabled properties for this GameObject." ),
		new TrackPresetNode( "Object", typeof(GameObject),
			new TrackPresetNode( nameof(GameObject.Enabled), typeof(bool) ),
			new TrackPresetNode( nameof(GameObject.LocalPosition), typeof(Vector3) ),
			new TrackPresetNode( nameof(GameObject.LocalRotation), typeof(Rotation) ) ) );

	private static TrackPreset PlayerControllerPreset { get; } = new(
		new TrackPresetMetadata( "Player Controller - Procedural", "Common",
			Description: "Includes all properties needed to procedurally animate a player controller, like its velocity and eye angles." ),
		new TrackPresetNode( "Player Controller", typeof(GameObject),
		[
			..TransformPreset.Root.Children,

			new TrackPresetNode( nameof(PlayerController), typeof(PlayerController),
				new TrackPresetNode( nameof(PlayerController.EyeAngles), typeof(Angles) ),
				new TrackPresetNode( nameof(PlayerController.WishVelocity), typeof(Vector3) ),
				new TrackPresetNode( nameof(PlayerController.IsSwimming), typeof(bool) ),
				new TrackPresetNode( nameof(PlayerController.IsClimbing), typeof(bool) ),
				new TrackPresetNode( nameof(PlayerController.IsDucking), typeof(bool) ) ),

			new TrackPresetNode( nameof(Rigidbody), typeof(Rigidbody),
				new TrackPresetNode( nameof(Rigidbody.Velocity), typeof(Vector3) ) )
		] ) );

	private static TrackPreset PlayerControllerBonesPreset { get; } = new(
		new TrackPresetMetadata( "Player Controller - Bones", "Common",
			Description: "Includes all the bone tracks of a player controller, useful for recording how it animates in-game." ),
		new TrackPresetNode( "Player Controller", typeof( GameObject ),
		[
			..TransformPreset.Root.Children,

			new TrackPresetNode( "Body", typeof(GameObject),
			[
				new TrackPresetNode( nameof(GameObject.LocalRotation), typeof(Rotation) ),
				new TrackPresetNode( nameof(SkinnedModelRenderer), typeof(SkinnedModelRenderer),
				[
					new TrackPresetNode( "Bones", typeof(BoneAccessor), AllChildren: true )
				] )
			] )
		] ) );

	private static TrackPreset BonesPreset { get; } = new(
		new TrackPresetMetadata( "Bones", "Common",
	Description: "Includes all the bone tracks of a SkinnedModelRenderer." ),
		new TrackPresetNode( "Body", typeof(GameObject),
		[
			new TrackPresetNode( nameof(SkinnedModelRenderer), typeof(SkinnedModelRenderer),
			[
				new TrackPresetNode( "Bones", typeof(BoneAccessor), AllChildren: true )
			] )
		] ) );

	public static IReadOnlyList<TrackPreset> BuiltInPresets { get; } =
	[
		TransformPreset,
		PlayerControllerPreset,
		PlayerControllerBonesPreset,
		BonesPreset
	];
}
