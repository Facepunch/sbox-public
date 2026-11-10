using Editor.MapDoc;
using NativeHammer;

namespace Editor.MapEditor;

/// <summary>
/// Create prop_static entities from a Package model.
/// </summary>
[CanDrop( "model" )]
[Expose]
class ModelDropTarget : IMapViewDropTarget
{
	MapEntity Entity { get; set; }

	public void DragEnter( Package package, MapView view )
	{
		// Get this early before any async shit ( only really needed cause of free drag mode )
		Vector3 normal = Vector3.Zero;
		Vector3 position = Vector3.Zero;
		view.native.GetDropTarget( ref normal, ref position, view.MousePosition );

		Entity = CreateModelEntityFromPackage( view.native, package, position );

		// Letting native hammer deal with this shit, it's not great though..
		view.native.EnterFreeDragMode( view.MousePosition, Entity, Vector3.Up, true );
	}

	public void DragMove( MapView view )
	{
		if ( !Entity.IsValid() )
			return;

		//
		// Free drag mode keeps a copy of the baseline entity that it restores from every drag
		// So just fucking hack around it and restore it
		// As we get more and more Hammer C# API exposed we can do our own free drag and ignore this shit
		//
		var model = Entity.GetKeyValue( "model" );
		view.native.UpdateFreeDragMode( view.MousePosition, false );
		Entity.SetKeyValue( "model", model );

	}

	public void DragDropped( MapView view )
	{
		view.native.ExitFreeDragMode( false );

		History.MarkUndoPosition( "Dropped Model" );
		History.KeepNew( Entity );
	}

	public void DragLeave( MapView view )
	{
		view.native.ExitFreeDragMode( true );

		if ( Entity.IsValid() )
			view.MapDoc.DeleteNode( Entity );
	}

	static MapEntity CreateModelEntityFromPackage( CMapView view, Package package, Vector3 position )
	{
		var entity = new MapEntity( view.GetMapDoc() );
		entity.ClassName = "prop_static";
		entity.Position = position;

		//
		// If we already have the package installed, no need to query any remote shit
		// Set the model immediately and exit
		//
		var installedPackage = AssetSystem.CloudDirectory.FindPackage( package.FullIdent );
		if ( installedPackage != null )
		{
			var modelName = installedPackage.PrimaryAsset;
			if ( FileSystem.Cloud.FileExists( modelName ) )
			{
				entity.SetKeyValue( "model", modelName );
				return entity;
			}
		}

		//
		// Async install the model package, and then update the entity's model
		// We're passing the entity node id for safety
		//
		SetEntityToPackageModelAsync( entity, package.FullIdent );

		return entity;
	}

	/// <summary>
	/// Installs the package and sets the model on the entity when that's done
	/// </summary>
	static async void SetEntityToPackageModelAsync( MapEntity entity, string packageIdent )
	{
		ThreadSafe.AssertIsMainThread();

		//
		// Fetch full package for the meta
		//
		var package = await Package.Fetch( packageIdent, false );

		//
		// Set some default bounds so the user knows the right size whilst it loads
		//
		{
			if ( !entity.IsValid() )
				return;

			var mins = package.GetMeta( "RenderMins", Vector3.Zero );
			var maxs = package.GetMeta( "RenderMaxs", Vector3.Zero );

			entity.SetDefaultBounds( mins, maxs );
		}

		//
		// Install our package
		//
		Asset asset;
		try
		{
			asset = await AssetSystem.InstallAsync( package, loading: ( float progress ) =>
			{
				if ( !entity.IsValid() ) return;
				LoadingProgress[entity] = progress;
				Hammer.DirtyViewHuds(); // ?
			} );
		}
		catch ( System.AggregateException ex )
		{
			Log.Error( ex, $"Failed to install model package: {package.FullIdent}" );
			return;
		}
		finally
		{
			LoadingProgress.Remove( entity );
			Hammer.DirtyViewHuds();
		}

		if ( asset == null ) return;

		//
		// Package is installed, update the model and we're done.
		//
		{
			if ( entity.IsValid() )
			{
				entity.SetKeyValue( "model", asset.Path );
			}
		}
	}

	static Dictionary<MapEntity, float> LoadingProgress = new();

	[Event( "hammer.rendermapview" )]
	protected static void RenderLoadProgress( MapView oniichan )
	{
		// You could do some lovely stuff here if you really wanted
		// But just showing the % is plenty
		foreach ( var kvp in LoadingProgress )
		{
			if ( !kvp.Key.IsValid() ) continue;
			Gizmo.Draw.Sprite( kvp.Key.Position + Vector3.Up * 16, 16, "materials/gizmo/downloads.png" );
			Gizmo.Draw.Text( $"Downloading {kvp.Value * 100}%", new Transform( kvp.Key.Position ), size: 14 * Application.DpiScale );
		}
	}
}
