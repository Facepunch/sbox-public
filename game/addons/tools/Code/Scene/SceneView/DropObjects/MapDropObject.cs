using System.Threading;

namespace Editor;

[DropObject( "map", "vmap" )]
partial class MapDropObject : BaseDropObject
{
	private string MapName;

	protected override async Task Initialize( string dragData, CancellationToken token )
	{
		var asset = await InstallAsset( dragData, token );
		if ( asset is null )
		{
			if ( Package.TryParseIdentParts( dragData, out var ident ) )
				MapName = $"{ident.org}.{ident.package}";

			return;
		}

		if ( token.IsCancellationRequested )
			return;

		MapName = asset.Path;
	}

	public override async Task OnDrop()
	{
		var mapInstance = SceneEditorSession.Active.Scene.GetAllComponents<MapInstance>()
			.FirstOrDefault();

		GameObject gameObject;

		using var scene = SceneEditorSession.Scope();

		using ( SceneEditorSession.Active.UndoScope( "Drop Map" ).WithComponentChanges( mapInstance is null ? Array.Empty<Component>() : new[] { mapInstance } ).WithGameObjectCreations().Push() )
		{
			if ( mapInstance.IsValid() )
			{
				gameObject = mapInstance.GameObject;
			}
			else
			{
				gameObject = new GameObject( true, "Map" );
				mapInstance = gameObject.Components.Create<MapInstance>();
			}

			mapInstance.MapName = MapName;

			EditorScene.Selection.Clear();
			EditorScene.Selection.Add( gameObject );
		}

		await Task.CompletedTask;
	}
}
