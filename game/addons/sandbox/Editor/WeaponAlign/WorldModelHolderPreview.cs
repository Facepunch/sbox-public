using System;
using System.Text.Json.Nodes;
using Editor;

namespace Sandbox.WeaponAlign;

/// <summary>
/// While a weapon world model prefab (a prefab whose root has a <see cref="BaseWeaponModel"/>) is open in the
/// prefab editor, draws the human player model holding it the way the game does: posed with the owning weapon's
/// hold type and handedness, with its hold bone pinned to the prefab root. Move or rotate the model child with the
/// normal gizmos until it sits in the hands, then save the prefab.
/// </summary>
internal static class WorldModelHolderPreview
{
	static RealTimeSince _timeSinceRefresh;
	static WorldModelHolder _lastHolder;

	[EditorEvent.Frame]
	static void Frame()
	{
		// A prefab scene is the prefab's root object.
		var scene = SceneEditorSession.Active?.Scene as PrefabScene;
		var prefabPath = scene?.Source?.ResourcePath;

		if ( prefabPath is null || scene.Components.Get<BaseWeaponModel>() is null )
			return;

		// The preview lives in the scene it previews, so closing the prefab or hotloading this code
		// can never leave an orphaned copy behind.
		var holder = scene.Children.Select( x => x.Components.Get<WorldModelHolder>( true ) ).FirstOrDefault( x => x is not null );
		if ( holder is not null && holder == _lastHolder && _timeSinceRefresh < 0.5f )
			return;

		_timeSinceRefresh = 0;
		var hold = FindHoldSettings( prefabPath );
		if ( hold is null ) return;

		if ( holder is null )
		{
			using ( scene.Push() )
			{
				var go = new GameObject( true, "world model holder preview" );
				go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.Hidden | GameObjectFlags.EditorOnly;
				holder = go.AddComponent<WorldModelHolder>();
			}
		}

		_lastHolder = holder;
		holder.HoldType = hold.HoldType;
		holder.Handedness = hold.Handedness;
		holder.HoldBone = hold.HoldBone;
		holder.HoldGraph = hold.HoldGraph;
	}

	record HoldSettings( string HoldType, int Handedness, string HoldBone, string HoldGraph );

	/// <summary>
	/// Hold settings from the weapon prefab whose WorldModelPrefab is <paramref name="worldModelPath"/>.
	/// Read from the prefab JSON so no weapon has to be instantiated.
	/// </summary>
	static HoldSettings FindHoldSettings( string worldModelPath )
	{
		foreach ( var prefab in ResourceLibrary.GetAll<PrefabFile>() )
		{
			var root = prefab.RootObject;
			if ( root?["Components"] is not JsonArray components ) continue;

			foreach ( var component in components )
			{
				var worldModel = component?["WorldModelPrefab"]?["prefab"]?.GetValue<string>();
				if ( !string.Equals( worldModel, worldModelPath, StringComparison.OrdinalIgnoreCase ) ) continue;

				var holdType = component["HoldType"]?.GetValue<string>() ?? "none";
				var handedness = component["Handedness"]?.GetValue<string>() switch
				{
					"Right" => 1,
					"Left" => 2,
					_ => 0
				};
				var holdBone = component["HoldBone"]?.GetValue<string>() ?? "hold_R";
				var graph = component["HoldGraph"];
				var graphPath = graph is JsonValue ? graph.GetValue<string>() : graph?["Path"]?.GetValue<string>();

				return new HoldSettings( holdType.ToLowerInvariant(), handedness, holdBone, graphPath );
			}
		}

		return null;
	}
}

/// <summary>
/// Animates the holder model and keeps its hold bone on the prefab root.
/// </summary>
internal sealed class WorldModelHolder : Component, Component.ExecuteInEditor
{
	const string HolderModelPath = "models/citizen_human/citizen_human_male.vmdl";

	public string HoldType { get; set; }
	public int Handedness { get; set; }
	public string HoldBone { get; set; }
	public string HoldGraph { get; set; }

	SceneModel _model;
	BoneCollection.Bone _bone;
	string _resolvedHoldBone;

	protected override void OnEnabled()
	{
		_model = new SceneModel( Scene.SceneWorld, HolderModelPath, global::Transform.Zero );
	}

	protected override void OnDisabled()
	{
		_model?.Delete();
		_model = null;
		_bone = null;
		_resolvedHoldBone = null;
	}

	protected override void OnUpdate()
	{
		if ( !_model.IsValid() ) return;
		var graph = string.IsNullOrEmpty( HoldGraph ) ? null : global::Sandbox.Animation.Graph.Load( HoldGraph );
		if ( !string.IsNullOrEmpty( HoldGraph ) && graph?.IsValid != true ) return;
		if ( _model.Animation.Graph != graph ) _model.Animation.Graph = graph;

		// Resolved here rather than in OnEnabled, which runs before HoldBone is assigned.
		if ( _resolvedHoldBone != HoldBone )
		{
			_bone = null;
			_resolvedHoldBone = HoldBone;
		}
		_bone ??= _model.Model.Bones.AllBones.FirstOrDefault( b => string.Equals( b.Name, HoldBone, StringComparison.OrdinalIgnoreCase ) );
		if ( _bone is null ) return;

		if ( _model.Animation.Graph is not null )
		{
			if ( !_model.Animation.IsValid ) return;
			var parameters = _model.Animation.Parameters;
			parameters.TrySet( "b_grounded", true );
			parameters.TrySet( "holdtype", HoldType );
			parameters.TrySet( "holdtype_handedness", Handedness );
		}
		else
		{
#pragma warning disable CS0618
			_model.SetAnimParameter( "b_grounded", true );
			_model.SetAnimParameter( "holdtype", HoldType );
			_model.SetAnimParameter( "holdtype_handedness", Handedness );
#pragma warning restore CS0618
		}
		_model.Update( RealTime.Delta );

		// Hold bone in model space, built from parent-relative bone transforms so it doesn't depend on
		// where the model was placed - deriving it from world transforms feeds last frame's placement
		// back in and the model drifts and jitters.
		var rotation = Rotation.Identity;
		var position = Vector3.Zero;
		for ( var bone = _bone; bone is not null; bone = bone.Parent )
		{
			var local = _model.GetBoneLocalTransform( bone.Index );
			position = local.Position + local.Rotation * position;
			rotation = local.Rotation * rotation;
		}

		// Place the model so that bone lands on the prefab root, which is exactly where
		// CreateWorldModel parents the world model in game.
		var root = Scene.WorldTransform;
		var modelRotation = root.Rotation * rotation.Inverse;
		_model.Transform = new global::Transform( root.Position - modelRotation * position, modelRotation );
	}
}
