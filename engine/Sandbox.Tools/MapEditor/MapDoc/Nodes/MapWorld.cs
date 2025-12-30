using Editor.MapEditor;
using Facepunch.ActionGraphs;
using NativeMapDoc;
using Sandbox.ActionGraphs;
using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

namespace Editor.MapDoc;

/// <summary>
/// MapWorld is the root node of a <see cref="MapDocument"/>, however it can have multiple sub <see cref="MapWorld"/> of prefabs.
/// </summary>
[Display( Name = "World" ), Icon( "view_in_ar" )]
public sealed class MapWorld : MapNode
{
	internal CMapWorld worldNative;

	internal MapWorld( HandleCreationData _ ) { }

	public Scene Scene { get; private set; }

	public HammerSceneEditorSession EditorSession { get; private set; }

	private string _mapPathName;

	public string MapPathName => _mapPathName ??= FindMapPathName();

	private string FindMapPathName()
	{
		if ( worldNative.GetRootDocument( MapNodeGetRootDocument.MayBeLoading ) is not { } mapDoc )
		{
			return null;
		}

		if ( GetMapAssetPath( mapDoc.PathName ) is { } assetPath )
		{
			return assetPath;
		}

		Log.Warning( $"Unable to find asset path to map: {mapDoc.PathName}" );
		return null;
	}

	private static string GetMapAssetPath( string fullPath )
	{
		// actual file might not exist yet, so we can't just search the asset database

		fullPath = fullPath.Replace( '/', '\\' );

		foreach ( var proj in Project.All.Where( x => x.Active ) )
		{
			if ( !proj.Active ) continue;
			if ( !proj.HasAssetsPath() ) continue;

			var assetsPath = $"{proj.GetAssetsPath()}\\";

			if ( fullPath.StartsWith( assetsPath, StringComparison.OrdinalIgnoreCase ) )
			{
				return fullPath[assetsPath.Length..].NormalizeFilename( false, false );
			}
		}

		return null;
	}

	private void InvalidateMapPathName()
	{
		_mapPathName = null;
	}

	internal override void OnNativeInit( CMapNode ptr )
	{
		base.OnNativeInit( ptr );

		worldNative = (CMapWorld)ptr;

		Scene = Scene.CreateEditorScene();
		Scene.Name = worldNative.GetName();

		EditorSession = new HammerSceneEditorSession( Scene, this );
		Scene.OverrideSourceLocation = EditorSession.SourceLocation;
	}

	internal override void PreSaveToFile()
	{
		InvalidateMapPathName();

		worldNative.SetSerializedScene( Scene.Serialize().ToString() );
	}

	internal override void PostLoadFromFile()
	{
		InvalidateMapPathName();

		var json = worldNative.GetSerializedScene();
		if ( string.IsNullOrEmpty( json ) )
			return;

		var jso = JsonNode.Parse( json ) as JsonObject;
		if ( jso is null ) return;

		var optionProvider = Scene.OverrideSourceLocation as ISerializationOptionProvider ?? throw new Exception( "Scene must have an OverrideSourceLocation" );
		using var agSerializationScope = ActionGraph.PushSerializationOptions( optionProvider.SerializationOptions );

		Scene.Deserialize( jso, new GameObject.DeserializeOptions { } );
	}

	internal override void OnNativeDestroy()
	{
		base.OnNativeDestroy();

		worldNative = default;
		if ( Scene.IsValid() )
		{
			Scene.Destroy();
			Scene = null;
		}

		EditorSession?.Destroy();
		EditorSession = null;
	}

	/// <summary>
	/// All children nodes of this world.
	/// </summary>
	/// <remarks>
	/// This returns nested descendants currently, that might change?
	/// </remarks>
	public new IEnumerable<MapNode> Children
	{
		get
		{
			NativeMapDoc.EnumChildrenPos pos = new();
			var node = native.GetFirstDescendent( ref pos );
			while ( node != null )
			{
				yield return node;
				node = native.GetNextDescendent( ref pos );
			}
		}
	}

	internal void GetWorldResourceReferencesAndDependencies( CUtlSymbolTable references )
	{
		var referencedResources = Scene.GetAllObjects( false )
			.SelectMany( x => x.Components.GetAll() )
			.SelectMany( x => x.GetSerialized().Where( x => x.PropertyType.IsSubclassOf( typeof( Resource ) ) ) )
			.Select( x => x.GetValue<Resource>() )
			.Where( x => x is not null );

		foreach ( var x in referencedResources )
		{
			references.AddString( x.ResourcePath );
		}

		var referencedPrefabs = Scene.GetAllObjects( false )
			.Where( x => x.IsPrefabInstanceRoot );

		foreach ( var x in referencedPrefabs )
		{
			references.AddString( x.PrefabInstanceSource );
		}
	}

}
