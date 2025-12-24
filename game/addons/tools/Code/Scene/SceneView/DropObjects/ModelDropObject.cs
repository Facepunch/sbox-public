using Sandbox.ModelEditor.Nodes;
using System.Threading;

namespace Editor;

[DropObject( "model", "vmdl", "vmdl_c" )]
partial class ModelDropObject : BaseDropObject
{
	Model model;
	bool physicsArchetype;
	bool physics;

	protected override async Task Initialize( string dragData, CancellationToken token )
	{
		Asset asset = await InstallAsset( dragData, token );

		if ( asset is null )
			return;

		if ( token.IsCancellationRequested )
			return;

		var archetype = asset.FindStringEditInfo( "model_archetype_id" );

		PackageStatus = "Loading Model";
		model = await Model.LoadAsync( asset.Path );
		PackageStatus = null;

		Bounds = model.Bounds;
		PivotPosition = Bounds.ClosestPoint( Vector3.Down * 10000 );
		physics = (model.Physics?.Parts.Count ?? 0) > 0;
		if ( physics && model.Physics.Parts.Any( p => p.Meshes.Any() ) ) // can't do rigid body with meshes
			physics = false;
		physicsArchetype = archetype == "physics_prop_model" || archetype == "jointed_physics_model" || archetype == "breakable_prop_model";
	}

	public override void OnUpdate()
	{
		using var scope = Gizmo.Scope( "DropObject", traceTransform );

		if ( model is not null )
		{
			if ( physics && physicsArchetype != Gizmo.IsShiftPressed )
				Gizmo.Draw.Color = Theme.Blue.WithAlpha( 0.6f );
			else
				Gizmo.Draw.Color = Color.White.WithAlpha( 0.3f );
			Gizmo.Draw.LineBBox( Bounds );

			Gizmo.Draw.Color = Color.White;
			var so = Gizmo.Draw.Model( model );
			if ( so.IsValid() )
			{
				so.Flags.CastShadows = true;
			}
		}

		if ( !string.IsNullOrWhiteSpace( PackageStatus ) )
		{
			Gizmo.Draw.Text( PackageStatus, new Transform( Bounds.Center ), "Inter", 14 * Application.DpiScale );

			Gizmo.Draw.Color = Color.White.WithAlpha( 0.3f );
			Gizmo.Draw.Sprite( Bounds.Center + Vector3.Up * 12, 16, "materials/gizmo/downloads.png" );
		}
	}

	private bool HasPropData()
	{
		if ( model.Data.Explosive )
			return true;
		if ( model.Data.Flammable )
			return true;
		if ( model.Data.Health > 0 )
			return true;
		if ( model.HasData<ModelBreakPiece[]>() )
			return true;
		return false;
	}

	public override async Task OnDrop()
	{
		await WaitForLoad();

		if ( model is null )
			return;

		using var scene = SceneEditorSession.Scope();

		using ( SceneEditorSession.Active.UndoScope( "Drop Model" ).WithGameObjectCreations().Push() )
		{
			GameObject = new GameObject( false );
			GameObject.Name = model.ResourceName;
			GameObject.WorldTransform = traceTransform;

			var rigidbody = physics && physicsArchetype != Gizmo.IsShiftPressed;
			if ( rigidbody || HasPropData() )
			{
				var prop = GameObject.Components.Create<Prop>();
				prop.Model = model;
				prop.IsStatic = !rigidbody;
			}
			else if ( model.BoneCount > 0 )
			{
				var renderer = GameObject.Components.Create<SkinnedModelRenderer>();
				renderer.Model = model;
			}
			else
			{
				var renderer = GameObject.Components.Create<ModelRenderer>();
				renderer.Model = model;
				if ( (model.Physics?.Parts.Count ?? 0) > 0 )
				{
					var collider = GameObject.Components.Create<ModelCollider>();
					collider.Static = true;
					collider.Model = model;
				}
			}

			GameObject.Enabled = true;

			EditorScene.Selection.Clear();
			EditorScene.Selection.Add( GameObject );
		}
	}
}
