using System;

namespace SceneTests.Components;

[TestClass]
public class SkinnedRendererTest
{
	private static Model CitizenModel => Model.Load( "models/citizen/citizen.vmdl" );

	/// <summary>
	/// Creates a GameObject with a SkinnedModelRenderer using the citizen model - the
	/// standard setup used by the tests in this class. The component is created disabled,
	/// given the model, then enabled so the SceneModel spawns with the citizen straight away.
	/// </summary>
	static SkinnedModelRenderer CreateCitizenRenderer( Scene scene, out GameObject go )
	{
		go = scene.CreateObject();
		var smr = go.Components.Create<SkinnedModelRenderer>( false );
		smr.Model = CitizenModel;
		smr.Enabled = true;
		return smr;
	}

	/// <summary>
	/// Walks the parent chain of a GameObject to determine whether it descends from
	/// the given ancestor.
	/// </summary>
	static bool IsDescendantOf( GameObject child, GameObject ancestor )
	{
		for ( var p = child.Parent; p is not null; p = p.Parent )
		{
			if ( p == ancestor )
				return true;
		}

		return false;
	}

	/// <summary>
	/// Picks the citizen's "Jump_Standing" sequence from the runtime sequence list. The
	/// authored citizen animation list ships this animation (with AE_FOOTSTEP events at
	/// frames 1 and 13), so it must be present on the compiled model.
	/// </summary>
	static string FindJumpStandingSequence( SkinnedModelRenderer smr )
	{
		var name = smr.Sequence.SequenceNames.FirstOrDefault( x => string.Equals( x, "Jump_Standing", StringComparison.OrdinalIgnoreCase ) );
		Assert.IsNotNull( name, "The citizen model should compile the authored Jump_Standing animation into its sequence list" );
		return name;
	}

	/// <summary>
	/// Enabling a SkinnedModelRenderer creates a SceneModel scene object carrying the
	/// assigned model, with the embedded citizen animgraph live and the component defaults
	/// (UseAnimGraph on, PlaybackRate 1) pushed through. Disabling deletes the scene object
	/// and nulls the SceneModel property, re-enabling creates a fresh one, and destroying
	/// the GameObject tears everything down.
	/// </summary>
	[TestMethod]
	public void SceneModelLifecycle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var smr = CreateCitizenRenderer( scene, out var go );

		var so = smr.SceneModel;
		Assert.IsNotNull( so, "Enabling should create a SceneModel" );
		Assert.IsTrue( so.IsValid() );
		Assert.AreSame( so, smr.SceneObject, "SceneModel is the renderer's scene object" );
		Assert.AreEqual( CitizenModel, so.Model, "The citizen model should be on the scene object" );
		Assert.IsNotNull( so.AnimationGraph, "The citizen model embeds citizen.vanmgrph, available as soon as the scene model spawns" );

		Assert.IsTrue( smr.UseAnimGraph, "UseAnimGraph defaults on" );
		Assert.IsTrue( so.UseAnimGraph, "The default is synced to the scene model" );
		Assert.AreEqual( 1.0f, smr.PlaybackRate );
		Assert.AreEqual( 1.0f, so.PlaybackRate, 0.001f, "Playback rate is applied on creation" );

		smr.Enabled = false;

		Assert.IsNull( smr.SceneModel, "Disabling should null the scene model" );
		Assert.IsFalse( so.IsValid(), "Disabling should delete the scene object" );

		smr.Enabled = true;

		var second = smr.SceneModel;
		Assert.IsNotNull( second, "Re-enabling should create a new scene model" );
		Assert.IsTrue( second.IsValid() );
		Assert.AreNotSame( so, second );

		go.Destroy();
		scene.ProcessDeletes();

		Assert.IsFalse( second.IsValid(), "Destroying the GameObject should delete the scene model" );
	}

	/// <summary>
	/// PlaybackRate and UseAnimGraph write straight through to the live SceneModel, and
	/// values set while the component is disabled are applied when the scene model is
	/// recreated on enable.
	/// </summary>
	[TestMethod]
	public void PlaybackRateAndUseAnimGraphPropagation()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var smr = CreateCitizenRenderer( scene, out var go );

		smr.PlaybackRate = 2.0f;
		Assert.AreEqual( 2.0f, smr.PlaybackRate );
		Assert.AreEqual( 2.0f, smr.SceneModel.PlaybackRate, 0.001f, "Playback rate should write through to the scene model" );

		smr.UseAnimGraph = false;
		Assert.IsFalse( smr.UseAnimGraph );
		Assert.IsFalse( smr.SceneModel.UseAnimGraph, "UseAnimGraph should write through to the scene model" );

		smr.Enabled = false;

		smr.PlaybackRate = 0.25f;
		smr.UseAnimGraph = true;

		smr.Enabled = true;

		Assert.AreEqual( 0.25f, smr.SceneModel.PlaybackRate, 0.001f, "Values set while disabled are applied to the new scene model" );
		Assert.IsTrue( smr.SceneModel.UseAnimGraph );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Enabling CreateBoneObjects on a renderer that has no live SceneModel (here, because
	/// its GameObject is disabled) must still place the generated bone GameObjects at their
	/// bind-pose transforms. With no SceneModel the per-frame bone sync never runs, so without
	/// seeding the objects would sit at identity and anything parented to a bone (e.g. a
	/// collider) would be offset. Regression test for sbox-public issue #593.
	/// </summary>
	[TestMethod]
	public void CreateBoneObjectsWhileDisabledUsesBindPose()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Enabled = false;

		var smr = go.Components.Create<SkinnedModelRenderer>();
		smr.Model = CitizenModel;
		smr.CreateBoneObjects = true;

		Assert.IsNull( smr.SceneModel, "A renderer on a disabled GameObject has no live scene model to drive bones" );

		var offsetBones = 0;

		foreach ( var bone in CitizenModel.Bones.AllBones )
		{
			var boneObject = smr.GetBoneObject( bone.Index );
			Assert.IsNotNull( boneObject, $"A bone object should be created for '{bone.Name}'" );

			var expected = bone.Parent is { } parent
				? parent.LocalTransform.ToLocal( bone.LocalTransform )
				: bone.LocalTransform;

			Assert.IsTrue( boneObject.LocalTransform.AlmostEqual( expected, 0.01f ),
				$"Bone '{bone.Name}' should be at its bind pose, was {boneObject.LocalTransform.Position} expected {expected.Position}" );

			if ( expected.Position.Length > 0.1f )
				offsetBones++;
		}

		Assert.IsTrue( offsetBones > 0, "The citizen bind pose has plenty of offset bones - if none registered the test isn't proving anything (all-identity would trivially pass)" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// The citizen animgraph parameter defaults are readable as soon as the renderer is
	/// enabled (b_grounded true, duck 0, hit_bone 20, aim_eyes (100,0,0), identity ik
	/// rotation), every parameter type round trips through Set/Get against the live graph,
	/// and unknown parameter names read back as zero / identity from the native side while
	/// still being stored in the component's parameter dictionary.
	/// </summary>
	[TestMethod]
	public void AnimGraphParameterDefaultsAndRoundTrips()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var smr = CreateCitizenRenderer( scene, out var go );

		// Authored defaults from citizen.vanmgrph
		Assert.IsTrue( smr.GetBool( "b_grounded" ), "b_grounded defaults to true in the citizen graph" );
		Assert.AreEqual( 0.0f, smr.GetFloat( "duck" ), "duck defaults to 0" );
		Assert.AreEqual( 20, smr.GetInt( "hit_bone" ), "hit_bone defaults to 20" );
		Assert.AreEqual( new Vector3( 100, 0, 0 ), smr.GetVector( "aim_eyes" ), "aim_eyes defaults to (100,0,0)" );
		Assert.AreEqual( Rotation.Identity, smr.GetRotation( "ik.hand_right.rotation" ), "ik rotations default to identity" );

		// Round trips for every parameter type
		smr.Set( "b_grounded", false );
		Assert.IsFalse( smr.GetBool( "b_grounded" ) );

		smr.Set( "duck", 0.5f );
		Assert.AreEqual( 0.5f, smr.GetFloat( "duck" ) );

		smr.Set( "move_x", -25.0f );
		Assert.AreEqual( -25.0f, smr.GetFloat( "move_x" ) );

		smr.Set( "hit_bone", 5 );
		Assert.AreEqual( 5, smr.GetInt( "hit_bone" ) );

		smr.Set( "aim_eyes", new Vector3( 1, 2, 3 ) );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), smr.GetVector( "aim_eyes" ) );

		var rot = Rotation.From( 10, 20, 30 );
		smr.Set( "ik.hand_right.rotation", rot );
		Assert.IsTrue( smr.GetRotation( "ik.hand_right.rotation" ).Distance( rot ) < 0.1f, "Rotation parameters should round trip" );

		// Enum parameters accept ints
		smr.Set( "holdtype", 2 );
		Assert.AreEqual( 2, smr.GetInt( "holdtype" ), "Enum parameters round trip through the int accessors" );

		// Unknown parameters: nothing on the native side, getters return defaults
		Assert.IsFalse( smr.GetBool( "not_a_parameter" ) );
		Assert.AreEqual( 0.0f, smr.GetFloat( "not_a_parameter" ) );
		Assert.AreEqual( 0, smr.GetInt( "not_a_parameter" ) );
		Assert.AreEqual( Vector3.Zero, smr.GetVector( "not_a_parameter" ) );
		Assert.AreEqual( Rotation.Identity, smr.GetRotation( "not_a_parameter" ) );

		// Setting an unknown parameter still stores it in the component dictionary, but
		// the native graph never sees it so the getter keeps returning zero.
		smr.Parameters.Set( "not_a_parameter", 2.5f );
		Assert.IsTrue( smr.Parameters.Contains( "not_a_parameter" ), "Unknown parameters are stored for a later graph" );
		Assert.AreEqual( 0.0f, smr.GetFloat( "not_a_parameter" ), "The graph has no such parameter, so the native value stays zero" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Parameters set while the component is disabled are stored in the component and
	/// applied to the animgraph when the scene model spawns on enable, pinning the
	/// "set before spawn" path through ApplyStoredAnimParameters.
	/// </summary>
	[TestMethod]
	public void ParametersStoredBeforeEnableApplyOnSpawn()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var smr = go.Components.Create<SkinnedModelRenderer>( false );
		smr.Model = CitizenModel;

		smr.Set( "duck", 0.5f );
		smr.Set( "b_grounded", false );

		Assert.IsTrue( smr.Parameters.Contains( "duck" ), "Parameters set while disabled are stored" );
		Assert.AreEqual( 0.0f, smr.GetFloat( "duck" ), "With no scene model the getters fall back to defaults" );

		smr.Enabled = true;

		Assert.AreEqual( 0.5f, smr.GetFloat( "duck" ), "Stored parameters should be applied when the scene model spawns" );
		Assert.IsFalse( smr.GetBool( "b_grounded" ) );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// ClearParameters wipes the component's stored parameter dictionary and resets the
	/// native graph parameters back to their authored defaults.
	/// </summary>
	[TestMethod]
	public void ClearParametersResetsGraphState()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var smr = CreateCitizenRenderer( scene, out var go );

		smr.Parameters.Set( "b_grounded", false );
		smr.Parameters.Set( "duck", 1.5f );

		Assert.IsFalse( smr.GetBool( "b_grounded" ) );
		Assert.AreEqual( 1.5f, smr.GetFloat( "duck" ) );

		smr.Parameters.Clear();

		Assert.IsFalse( smr.Parameters.Contains( "b_grounded" ), "Clearing should empty the stored dictionary" );
		Assert.IsFalse( smr.Parameters.Contains( "duck" ) );
		Assert.IsTrue( smr.GetBool( "b_grounded" ), "Clearing should reset the graph back to authored defaults" );
		Assert.AreEqual( 0.0f, smr.GetFloat( "duck" ) );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// The Sequence accessor: an unknown sequence name is kept on the component but fails
	/// the native lookup (null native name, zero duration), a valid sequence resolves with
	/// a real duration, TimeNormalized round trips, scene ticks advance playback while
	/// UseAnimGraph is off, and PlaybackRate zero freezes it. Sequence.PlaybackRate aliases
	/// the renderer's PlaybackRate.
	/// </summary>
	[TestMethod]
	public void SequenceStateAndPlayback()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var smr = CreateCitizenRenderer( scene, out var go );
		smr.UseAnimGraph = false;

		Assert.IsTrue( smr.Sequence.SequenceNames.Count > 0, "The citizen model should list its sequences" );

		smr.Sequence.Name = "not_a_sequence";
		Assert.AreEqual( "not_a_sequence", smr.Sequence.Name, "The accessor keeps whatever name was set" );
		Assert.IsNull( smr.SceneModel.CurrentSequence.Name, "The native lookup fails for an unknown sequence" );
		Assert.AreEqual( 0.0f, smr.Sequence.Duration, "An unknown sequence has no duration" );

		var seqName = FindJumpStandingSequence( smr );
		smr.Sequence.Name = seqName;

		Assert.AreEqual( seqName, smr.SceneModel.CurrentSequence.Name, "A valid sequence resolves natively" );
		Assert.IsTrue( smr.Sequence.Duration > 0.2f, "Jump_Standing should have a real duration" );

		smr.Sequence.TimeNormalized = 0.25f;
		Assert.AreEqual( 0.25f, smr.Sequence.TimeNormalized, 0.001f, "The cycle should round trip" );

		scene.GameTick();

		Assert.IsTrue( smr.Sequence.TimeNormalized > 0.25f, "A scene tick should advance sequence playback" );
		Assert.IsFalse( smr.Sequence.IsFinished, "A looping sequence never finishes" );

		smr.PlaybackRate = 0.0f;
		var frozen = smr.Sequence.TimeNormalized;

		scene.GameTick();

		Assert.AreEqual( frozen, smr.Sequence.TimeNormalized, 0.0001f, "PlaybackRate zero should freeze playback" );

		smr.Sequence.PlaybackRate = 1.5f;
		Assert.AreEqual( 1.5f, smr.PlaybackRate, "Sequence.PlaybackRate aliases the renderer's PlaybackRate" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Playing the citizen's Jump_Standing sequence headless delivers its authored
	/// AE_FOOTSTEP events through OnFootstepEvent: the events are queued by the animation
	/// update during GameTick and dispatched on the main thread, so a couple of seconds of
	/// looping sim time is guaranteed to cross the footstep frames.
	/// </summary>
	[TestMethod]
	public void FootstepEventsFromSequencePlayback()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var smr = CreateCitizenRenderer( scene, out var go );
		smr.UseAnimGraph = false;
		smr.Sequence.Name = FindJumpStandingSequence( smr );

		var count = 0;
		smr.OnFootstepEvent = e =>
		{
			count++;
			Assert.IsTrue( e.FootId == 0 || e.FootId == 1, "Footstep events carry a left/right foot id" );
		};

		for ( int i = 0; i < 20 && count == 0; i++ )
		{
			scene.GameTick();
		}

		Assert.IsTrue( count > 0, "Jump_Standing has AE_FOOTSTEP events that should dispatch during ticks" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// CreateBoneObjects builds a GameObject hierarchy mirroring the citizen skeleton:
	/// bones resolve by name, index and bone reference to the same flagged GameObject under
	/// the renderer's GameObject, out-of-range and unknown lookups return null, and turning
	/// the option back off clears the lookup and strips the Bone flag while leaving the
	/// GameObjects themselves alive.
	/// </summary>
	[TestMethod]
	public void BoneObjectLifecycle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var smr = CreateCitizenRenderer( scene, out var go );

		Assert.IsFalse( smr.CreateBoneObjects, "Bone objects are off by default" );
		Assert.IsNull( smr.GetBoneObject( "pelvis" ), "No bone objects exist until CreateBoneObjects is enabled" );

		smr.CreateBoneObjects = true;

		var pelvis = smr.GetBoneObject( "pelvis" );
		Assert.IsNotNull( pelvis, "The citizen pelvis bone should have a GameObject" );
		Assert.IsTrue( pelvis.IsValid() );
		Assert.IsTrue( string.Equals( "pelvis", pelvis.Name, StringComparison.OrdinalIgnoreCase ), "Bone objects take their bone's name" );
		Assert.IsTrue( pelvis.Flags.HasFlag( GameObjectFlags.Bone ), "Bone objects are flagged as bones" );
		Assert.IsTrue( IsDescendantOf( pelvis, go ), "Bone objects live under the renderer's GameObject" );

		var bone = CitizenModel.Bones.GetBone( "pelvis" );
		Assert.IsNotNull( bone );
		Assert.AreSame( pelvis, smr.GetBoneObject( bone.Index ), "Index lookup should agree with name lookup" );
		Assert.AreSame( pelvis, smr.GetBoneObject( bone ), "Bone reference lookup should agree too" );

		Assert.IsNull( smr.GetBoneObject( -1 ), "Negative indices return null" );
		Assert.IsNull( smr.GetBoneObject( CitizenModel.Bones.AllBones.Count ), "Out of range indices return null" );
		Assert.IsNull( smr.GetBoneObject( "not_a_bone" ), "Unknown bone names return null" );

		smr.CreateBoneObjects = false;

		Assert.IsNull( smr.GetBoneObject( "pelvis" ), "Disabling clears the bone lookup" );
		Assert.IsTrue( pelvis.IsValid(), "Clearing bone proxies does not destroy the GameObjects" );
		Assert.IsFalse( pelvis.Flags.HasFlag( GameObjectFlags.Bone ), "Clearing strips the Bone flag" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Bone transform queries work as soon as the renderer is enabled - the citizen's
	/// hand bone resolves in world and local space, the bulk accessors return one entry per
	/// model bone, unknown bone names fail the Try pattern - and ticking the scene plays
	/// the citizen's idle graph headless, visibly moving the hand bone over two seconds of
	/// sim time.
	/// </summary>
	[TestMethod]
	public void BoneTransformsAnimateOverTime()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var smr = CreateCitizenRenderer( scene, out var go );

		Assert.IsTrue( smr.TryGetBoneTransform( "hand_R", out var initial ), "The citizen hand bone should resolve" );
		Assert.IsTrue( smr.TryGetBoneTransformLocal( "hand_R", out _ ), "Local-space bone transforms should resolve too" );
		Assert.IsFalse( smr.TryGetBoneTransform( "not_a_bone", out _ ), "Unknown bones fail the lookup" );

		Assert.AreEqual( CitizenModel.BoneCount, smr.GetBoneTransforms( true ).Length, "One world transform per bone" );
		Assert.AreEqual( CitizenModel.BoneCount, smr.GetBoneTransforms( false ).Length, "One local transform per bone" );
		Assert.AreEqual( CitizenModel.BoneCount, smr.GetBoneVelocities().Length, "One velocity per bone" );

		var maxDelta = 0.0f;

		for ( int i = 0; i < 20; i++ )
		{
			scene.GameTick();

			if ( smr.TryGetBoneTransform( "hand_R", out var tx ) )
			{
				maxDelta = MathF.Max( maxDelta, tx.Position.Distance( initial.Position ) );
			}
		}

		Assert.IsTrue( maxDelta > 0.01f, "The idle animation should move the hand bone over two seconds of sim time" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// GetAttachment resolves the citizen's authored hand_R attachment near the model in
	/// world space, returns local space when asked, follows the GameObject when it moves,
	/// returns null for unknown attachment names, and returns null once the component is
	/// disabled because the scene model is gone.
	/// </summary>
	[TestMethod]
	public void AttachmentLookup()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var smr = CreateCitizenRenderer( scene, out var go );

		var attachment = smr.GetAttachment( "hand_R" );
		Assert.IsTrue( attachment.HasValue, "The citizen ships a hand_R attachment" );
		Assert.IsTrue( attachment.Value.Position.Length < 200.0f, "With the GameObject at the origin the attachment should be near the model" );

		Assert.IsNull( smr.GetAttachment( "not_an_attachment" ), "Unknown attachments return null" );

		var offset = new Vector3( 1000, 500, 0 );
		go.WorldPosition = offset;
		scene.GameTick();

		var moved = smr.GetAttachment( "hand_R" );
		Assert.IsTrue( moved.HasValue );
		Assert.IsTrue( moved.Value.Position.Distance( offset ) < 200.0f, "World-space attachments follow the GameObject" );

		var local = smr.GetAttachment( "hand_R", false );
		Assert.IsTrue( local.HasValue );
		Assert.IsTrue( local.Value.Position.Length < 200.0f, "Local-space attachments stay relative to the scene object" );

		smr.Enabled = false;

		Assert.IsNull( smr.GetAttachment( "hand_R" ), "No scene model means no attachments" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Bone merging: assigning self as BoneMergeTarget is ignored, assigning a target
	/// merges immediately (the child's scene model snaps to the target's transform), the
	/// merge is maintained by the animation system every tick so the child's scene model
	/// follows the target GameObject around while the child GameObject stays put, and
	/// clearing the target releases the child back to its own transform.
	/// </summary>
	[TestMethod]
	public void BoneMergeFollowsTarget()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var target = CreateCitizenRenderer( scene, out var targetGo );
		var child = CreateCitizenRenderer( scene, out var childGo );

		child.BoneMergeTarget = child;
		Assert.IsNull( child.BoneMergeTarget, "Assigning self as the merge target is ignored" );

		child.BoneMergeTarget = target;
		Assert.AreEqual( target, child.BoneMergeTarget );
		Assert.IsTrue( target.HasBoneMergeChildren, "The target should track its merge children" );
		Assert.IsTrue( child.SceneModel.Transform.Position.Distance( target.SceneModel.Transform.Position ) < 0.001f, "Assigning a target merges immediately" );

		var offset = new Vector3( 500, 0, 0 );
		targetGo.WorldPosition = offset;
		scene.GameTick();

		Assert.IsTrue( child.SceneModel.Transform.Position.Distance( offset ) < 0.1f, "The merged scene model follows the target's transform" );
		Assert.IsTrue( childGo.WorldPosition.Length < 0.001f, "The child GameObject itself does not move" );

		child.BoneMergeTarget = null;
		Assert.IsFalse( target.HasBoneMergeChildren, "Clearing the target releases the child" );

		scene.GameTick();

		Assert.IsTrue( child.SceneModel.Transform.Position.Length < 0.1f, "An unmerged scene model returns to its own GameObject transform" );

		targetGo.Destroy();
		childGo.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Morph overrides on the citizen face: the model ships flex controllers, Set stores
	/// an override on the component and writes it through to the scene model immediately,
	/// Clear removes the component override - but the native side keeps the last override
	/// target (only the blend alpha fades back to animation), so Get falls through to that
	/// stale native value. Unknown morph names read zero.
	/// </summary>
	[TestMethod]
	public void MorphOverrideStateAndQuirks()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var smr = CreateCitizenRenderer( scene, out var go );

		Assert.IsTrue( CitizenModel.MorphCount > 0, "The citizen has face morphs" );
		Assert.IsTrue( CitizenModel.Morphs.Names.Any( x => string.Equals( x, "lipcornerpullerL", StringComparison.OrdinalIgnoreCase ) ), "The citizen face ships the lipcornerpullerL flex controller" );

		Assert.IsFalse( smr.Morphs.ContainsOverride( "lipcornerpullerL" ), "No overrides initially" );
		Assert.AreEqual( 0.0f, smr.Morphs.Get( "lipcornerpullerL" ), "The native override value starts at zero" );

		smr.Morphs.Set( "lipcornerpullerL", 0.75f );

		Assert.IsTrue( smr.Morphs.ContainsOverride( "lipcornerpullerL" ) );
		Assert.AreEqual( 0.75f, smr.Morphs.Get( "lipcornerpullerL" ) );
		Assert.AreEqual( 0.75f, smr.SceneModel.Morphs.Get( "lipcornerpullerL" ), 0.001f, "The override writes through to the scene model immediately" );

		smr.Morphs.Clear( "lipcornerpullerL" );

		Assert.IsFalse( smr.Morphs.ContainsOverride( "lipcornerpullerL" ), "Clear removes the component override" );
		Assert.AreEqual( 0.75f, smr.Morphs.Get( "lipcornerpullerL" ), 0.001f, "Quirk: clearing only fades the blend alpha - the native override target survives and Get falls through to it" );

		Assert.AreEqual( 0.0f, smr.SceneModel.Morphs.Get( "not_a_morph" ), "Unknown morphs read zero" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// A SkinnedModelRenderer with a non-default model, playback rate, animgraph
	/// parameters, morph override and sequence settings survives a serialize/deserialize
	/// round trip: the accessor dictionaries are restored, the stored parameters are
	/// applied to the freshly spawned scene model, and the playback rate carries over to
	/// the native side.
	/// </summary>
	[TestMethod]
	public void SerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var smr = CreateCitizenRenderer( scene, out var go );
		smr.PlaybackRate = 0.5f;
		smr.Parameters.Set( "duck", 0.5f );
		smr.Parameters.Set( "b_grounded", false );
		smr.Parameters.Set( "hit_bone", 5 );
		smr.Parameters.Set( "aim_eyes", new Vector3( 1, 2, 3 ) );
		smr.Morphs.Set( "lipcornerpullerL", 0.75f );
		smr.Sequence.Name = "Jump_Standing";
		smr.Sequence.Looping = false;

		var clone = RenderTestUtility.SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<SkinnedModelRenderer>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a SkinnedModelRenderer" );
		Assert.AreEqual( CitizenModel.Name, loaded.Model?.Name, "Model should round trip by path" );
		Assert.AreEqual( 0.5f, loaded.PlaybackRate );
		Assert.IsTrue( loaded.UseAnimGraph, "UseAnimGraph default round trips" );

		Assert.IsNotNull( loaded.SceneModel, "The enabled clone should spawn its scene model" );
		Assert.AreEqual( 0.5f, loaded.SceneModel.PlaybackRate, 0.001f, "Playback rate is applied to the new scene model" );

		Assert.IsTrue( loaded.Parameters.Contains( "duck" ), "Parameters round trip into the stored dictionary" );
		Assert.AreEqual( 0.5f, loaded.GetFloat( "duck" ), "Restored parameters are applied to the new graph instance" );
		Assert.IsFalse( loaded.GetBool( "b_grounded" ) );
		Assert.AreEqual( 5, loaded.GetInt( "hit_bone" ) );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), loaded.GetVector( "aim_eyes" ) );

		Assert.IsTrue( loaded.Morphs.ContainsOverride( "lipcornerpullerL" ), "Morph overrides round trip" );
		Assert.AreEqual( 0.75f, loaded.Morphs.Get( "lipcornerpullerL" ) );

		Assert.AreEqual( "Jump_Standing", loaded.Sequence.Name, "The sequence name round trips" );
		Assert.IsFalse( loaded.Sequence.Looping, "The looping flag round trips" );

		clone.Destroy();
		scene.ProcessDeletes();
	}
}
