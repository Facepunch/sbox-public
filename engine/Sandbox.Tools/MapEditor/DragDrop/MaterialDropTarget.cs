using Editor.MapDoc;
using NativeHammer;

namespace Editor.MapEditor;

// [CanDrop( "vmat" )] // We can do this and stomp the native stuff once face selection works
[CanDrop( "material" )]
[Expose]
class MaterialDropTarget : IMapViewDropTarget
{
	MapMesh MapMesh { get; set; }
	Material Material { get; set; }
	CSavedObjects SavedObjects { get; set; }

	MapDocument doc;

	MapNode PlacedNode { get; set; }

	public void DragEnter( Package package, MapView view )
	{
		doc = view.MapDoc;

		// Start fetching the material, what we could maybe do is make a fake material from the thumbnail
		_ = GetMaterialFromPackage( package );

		SavedObjects = CSavedObjects.Create();
	}

	/// <summary>
	/// Calculate the angles required to align the entity to the plane perpendicular to the given normal
	/// Now rotate the angle 90 degrees so that it matches the orientation of the texture as it is authored
	/// </summary>
	private static Angles DeriveOverlayAnglesFromNormal( Vector3 hitNormal )
	{
		return (Rotation.From( hitNormal.EulerAngles - Vector3.Up.EulerAngles ) *
				 Rotation.FromAxis( Vector3.Up, 90.0f )).Angles();
	}

	public void DragMove( MapView view )
	{
		// Pointless for us to do any "iterative" work on a pending material
		if ( Material == null )
			return;

		if ( PlacedNode.IsValid() )
		{
			view.BuildRay( out Vector3 rayStart, out Vector3 rayEnd );
			var tr = Trace.Ray( rayStart, rayEnd ).SkipToolsMaterials().MeshesOnly().Run( view.MapDoc.World );

			PlacedNode.Position = tr.HitPosition;
			PlacedNode.Angles = DeriveOverlayAnglesFromNormal( tr.Normal );
		}
		else
		{
			view.BuildRay( out Vector3 rayStart, out Vector3 rayEnd );
			var tr = Trace.Ray( rayStart, rayEnd ).Run( view.MapDoc.World );

			RevertIterativeDragWork();

			if ( tr.MapNode is MapMesh mesh )
			{
				MapMesh = mesh;
				SavedObjects.SaveObject( mesh );
				mesh.SetMaterial( Material );
			}
			else
			{
				MapMesh = null;
			}
		}
	}

	public void DragLeave( MapView view )
	{
		doc = null;

		if ( PlacedNode.IsValid() )
		{
			view.MapDoc.DeleteNode( PlacedNode );
		}

		RevertIterativeDragWork();
	}

	public void DragDropped( MapView view )
	{
		if ( PlacedNode.IsValid() )
		{
			History.MarkUndoPosition( $"Drop {PlacedNode}" );
			History.KeepNew( PlacedNode );
			return;
		}

		RevertIterativeDragWork();

		if ( MapMesh == null || Material == null )
			return;

		History.MarkUndoPosition( "Drop material" );
		History.Keep( MapMesh );

		// This doesn't work in faces mode, which sucks - probably needs glue
		MapMesh.SetMaterial( Material );

		SavedObjects.DeleteThis();
	}

	private void RevertIterativeDragWork()
	{
		if ( !SavedObjects.IsValid )
			return;

		SavedObjects.RestoreObjects();
		SavedObjects.RemoveAll();
	}

	async Task GetMaterialFromPackage( Package package )
	{
		// Install our package ( Do we need any sort of loading indicator, it's gonna be fast surely )
		Asset asset;
		try
		{
			asset = await AssetSystem.InstallAsync( package );
		}
		catch ( System.AggregateException ex )
		{
			Log.Error( ex, $"Failed to install material package: {package.FullIdent}" );
			return;
		}

		if ( asset is null ) return;
		Material = Material.Load( asset.Path );

		CreateNode();
	}

	public void CreateNode()
	{
		if ( !doc.IsValid() ) return;

		// This fucking stupid shit is defined from shaders wtf
		if ( Material.Attributes.GetInt( "decal", 0 ) == 1 )
		{
			var overlayEnt = new MapEntity( doc );
			overlayEnt.ClassName = "info_overlay";
			overlayEnt.SetKeyValue( "material", Material.ResourcePath );

			PlacedNode = overlayEnt;
		}
		else if ( Material.Attributes.GetInt( "overlay", 0 ) == 1 )
		{
			var width = NativeHammer.Global.MaterialGetMappingWidth( Material.native );
			var height = NativeHammer.Global.MaterialGetMappingHeight( Material.native );

			var staticOverlay = new MapStaticOverlay( doc );
			staticOverlay.CreateCenteredQuad( new Vector2( width, height ), Material );

			PlacedNode = staticOverlay;
		}
		else if ( Material.Attributes.GetInt( "sky", 0 ) == 1 )
		{
			var skyEnt = new MapEntity( doc );
			skyEnt.ClassName = "env_sky";
			skyEnt.SetKeyValue( "skyname", Material.ResourcePath );

			PlacedNode = skyEnt;
		}
	}
}
