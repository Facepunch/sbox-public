using System.Text.Json.Nodes;

/// <summary>
/// Holds a bunch of GameObject json, a bounding box, and some preview models for a
/// duplication. This is what gets serialized to a string and stored in the Duplicator tool.
/// The objects and the bounds are created in selection space. Where the user right clicked to 
/// select is 0,0,0, and the player's view yaw is the rotation identity.
/// </summary>
public class DuplicationData
{
	/// <summary>
	/// An array of JsonObject objects, which are serialzed GameObjects
	/// </summary>
	public JsonArray Objects { get; set; }

	/// <summary>
	/// The bounds are used to work out where to place the duplication, so it
	/// doesn't clip through the floor.
	/// </summary>
	public BBox Bounds { get; set; }

	/// <summary>
	/// Describes where to draw a model for the preview. Building blocks have no model resource to
	/// save, so they store their <paramref name="Builder"/> recipe and are regenerated to draw.
	/// </summary>
	public record struct PreviewModel( Model Model, Transform Transform, Transform[] Bones, BBox Bounds, BuilderRecipe Builder = null );

	/// <summary>
	/// A list of preview models to help visualze where the duplication will be placed
	/// </summary>
	public List<PreviewModel> PreviewModels { get; set; }

	/// <summary>
	/// Packages used in this
	/// </summary>
	public List<string> Packages { get; set; }

	/// <summary>
	/// Create DuplicationData from a bunch of objects.
	/// center is the transform to use as the origin for the duplication.
	/// The rotation of center should be the player's view yaw when they made the selection.
	/// </summary>
	public static DuplicationData CreateFromObjects( IEnumerable<GameObject> objects, Transform center )
	{
		var dupe = new DuplicationData();
		dupe.Objects = new JsonArray();
		dupe.Bounds = BBox.FromPositionAndSize( 0, 0.01f );
		dupe.PreviewModels = new();

		List<BBox> worldBounds = new List<BBox>();

		foreach ( var obj in objects )
		{
			var entry = obj.Serialize();
			DrivenProp.RemoveDriveTags( obj, entry );
			worldBounds.Add( GetWorldBounds( obj ) );

			var localized = center.ToLocal( obj.WorldTransform );
			entry["Position"] = JsonValue.Create( localized.Position );
			entry["Rotation"] = JsonValue.Create( localized.Rotation );
			entry["Scale"] = JsonValue.Create( localized.Scale );

			dupe.Objects.Add( entry );

			foreach ( var renderer in obj.GetComponentsInChildren<ModelRenderer>() )
			{
				var model = renderer.Model ?? Model.Cube;

				if ( model.IsError ) continue;

				Transform[] bones = null;

				if ( renderer is SkinnedModelRenderer skinned )
				{
					bones = skinned.GetBoneTransforms( false );
				}

				var modelTx = center.ToLocal( renderer.WorldTransform );

				if ( renderer.GetComponent<BuilderProp>() is { } block && block.Renderer == renderer )
				{
					dupe.PreviewModels.Add( new DuplicationData.PreviewModel( null, modelTx, null, model.Bounds, block.Recipe ) );
					continue;
				}

				dupe.PreviewModels.Add( new DuplicationData.PreviewModel( model, modelTx, bones, model.Bounds ) );
			}
		}

		if ( worldBounds.Count > 0 )
		{
			var txi = new Transform( -center.Position, center.Rotation.Inverse );

			dupe.Bounds = BBox.FromBoxes( worldBounds.Select( x => x.Transform( txi ) ) );
		}

		var packages = Cloud.ResolvePrimaryAssetsFromJson( dupe.Objects );
		dupe.Packages = packages.Select( x => x.FullIdent ).ToList();


		return dupe;
	}

	public static BBox GetWorldBounds( GameObject go )
	{
		BBox box = BBox.FromPositionAndSize( 0, 0.01f );

		var rb = go.GetComponentsInChildren<Collider>( false, true ).ToArray();
		if ( rb.Length > 0 )
		{
			box = rb[0].GetWorldBounds();

			foreach ( var b in rb )
			{
				box = box.AddBBox( b.GetWorldBounds() );
			}
		}

		return box;
	}
}
