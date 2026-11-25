using System.Threading;

namespace Editor;

[DropObject( null, "vmap" )]
partial class MapDropObject : BaseDropObject
{
	private string MapName;

	protected override Task Initialize( string dragData, CancellationToken token )
	{
		MapName = AssetSystem.FindByPath( dragData )?.Path;
		return Task.CompletedTask;
	}

	public override async Task OnDrop()
	{
		if ( string.IsNullOrEmpty( MapName ) )
			return;

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
