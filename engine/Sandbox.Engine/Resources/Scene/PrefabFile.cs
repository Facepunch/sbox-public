using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// A GameObject which is saved to a file.
/// </summary>
[Expose]
[AssetType( Name = "Prefab", Extension = "prefab", Category = "World", Flags = AssetTypeFlags.NoEmbedding )]
public partial class PrefabFile : GameResource
{
	/// <summary>
	/// Contains the original JSON read from File.
	/// </summary>
	public JsonObject RootObject { get; set; }

	public override int ResourceVersion => 2;

	/// <summary>
	/// This is used as a reference
	/// </summary>
	[JsonIgnore]
	internal PrefabCacheScene CachedScene { get; set; }

	/// <summary>
	/// Get the actual scene scene
	/// </summary>
	public PrefabScene GetScene()
	{
		if ( CachedScene is not null )
			return CachedScene;

		CachedScene = new PrefabCacheScene()
		{
			Source = this,
			Name = ResourceName
		};

		CachedScene.Load( this );
		return CachedScene;
	}

	protected override void PostLoad()
	{
		PostReload();
	}

	protected override void PostReload()
	{
		// Make sure our RootObjects name is consistent with the file name.
		// In case of renames or duplicated prefabs.
		if ( RootObject is not null && RootObject[GameObject.JsonKeys.Name]?.GetValue<string>() != ResourceName )
		{
			RootObject[GameObject.JsonKeys.Name] = ResourceName;
		}


		// Load the cached scene
		if ( CachedScene is PrefabCacheScene cachedScene )
		{
			cachedScene.Refresh( this );
		}

		Register();
	}

	protected override void OnDestroy()
	{
		CachedScene?.DestroyInternal();
		CachedScene = null;

		Unregister();
	}

	/// <summary>
	/// If true then we'll show this in the right click menu, so people can create it
	/// </summary>
	public bool ShowInMenu { get; set; }

	/// <summary>
	/// If ShowInMenu is true, this is the path in the menu for this prefab
	/// </summary>
	public string MenuPath { get; set; }

	/// <summary>
	/// Icon to show to the left of the option in the menu
	/// </summary>
	[IconName]
	public string MenuIcon { get; set; }

	/// <summary>
	/// If true then the prefab will not be broken when created as a template
	/// </summary>
	public bool DontBreakAsTemplate { get; set; }

	[Hide, JsonIgnore]
	protected override Type ActionGraphTargetType => null;

	[Hide, JsonIgnore]
	protected override object ActionGraphTarget => null;

	/// <summary>
	/// Read metadata saved using a ISceneMetadata based component, such as SceneInformation
	/// </summary>
	public string GetMetadata( string title, string defaultValue = null )
	{
		if ( RootObject is null ) return defaultValue;
		if ( RootObject["__properties"] is not JsonObject properties ) return defaultValue;
		if ( properties["Metadata"] is not JsonObject metadata ) return defaultValue;

		return metadata.GetPropertyValue( title, defaultValue );
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		var svg = "<svg viewBox=\"-2.4 -2.4 28.80 28.80\" fill=\"none\" xmlns=\"http://www.w3.org/2000/svg\" stroke=\"#000000\" stroke-width=\"0.00024000000000000003\"><g id=\"SVGRepo_bgCarrier\" stroke-width=\"0\"></g><g id=\"SVGRepo_tracerCarrier\" stroke-linecap=\"round\" stroke-linejoin=\"round\"></g><g id=\"SVGRepo_iconCarrier\"> <path d=\"M17.5777 4.43152L15.5777 3.38197C13.8221 2.46066 12.9443 2 12 2C11.0557 2 10.1779 2.46066 8.42229 3.38197L6.42229 4.43152C4.64855 5.36234 3.6059 5.9095 2.95969 6.64132L12 11.1615L21.0403 6.64132C20.3941 5.9095 19.3515 5.36234 17.5777 4.43152Z\" fill=\"#86c4fe\"></path> <path d=\"M21.7484 7.96435L12.75 12.4635V21.904C13.4679 21.7252 14.2848 21.2965 15.5777 20.618L17.5777 19.5685C19.7294 18.4393 20.8052 17.8748 21.4026 16.8603C22 15.8458 22 14.5833 22 12.0585V11.9415C22 10.0489 22 8.86558 21.7484 7.96435Z\" fill=\"#86c4fe\"></path> <path d=\"M11.25 21.904V12.4635L2.25164 7.96434C2 8.86557 2 10.0489 2 11.9415V12.0585C2 14.5833 2 15.8458 2.5974 16.8603C3.19479 17.8748 4.27063 18.4393 6.42229 19.5685L8.42229 20.618C9.71524 21.2965 10.5321 21.7252 11.25 21.904Z\" fill=\"#86c4fe\"></path> </g></svg>";
		return Bitmap.CreateFromSvgString( svg, width, height );
	}

	#region ObjectsById

	/// <summary>
	/// If this instance is stored in <see cref="ObjectsById"/>, what's the key?
	/// </summary>
	private Guid? _objectDictKey;

	/// <summary>
	/// Add this instance to <see cref="ObjectsById"/>, indexed by Guid. If the Guid has changed,
	/// removes the old entry.
	/// </summary>
	private void Register()
	{
		//
		// Get the guid of the root object. We'll reference this over the network
		// to look up this prefab via GameObjectDirectory.FindByGuid.
		//
		if ( RootObject.GetPropertyValue<Guid?>( "__guid", null ) is not { } guid )
		{
			Unregister();
			return;
		}

		if ( ObjectsById.TryGetValue( guid, out var existing ) )
		{
			if ( existing == this ) return;

			existing.Unregister();
		}

		_objectDictKey = guid;
		ObjectsById[guid] = this;
	}

	/// <summary>
	/// Remove this instance from <see cref="ObjectsById"/>.
	/// </summary>
	private void Unregister()
	{
		if ( _objectDictKey is not { } guid ) return;

		_objectDictKey = null;

		if ( ObjectsById.TryGetValue( guid, out var registered ) && registered != this )
		{
			Log.Warning( $"Another prefab was registered with this Guid: \"{ResourcePath}\" vs \"{registered.ResourcePath}\"" );
			return;
		}

		ObjectsById.Remove( guid );
	}

	/// <summary>
	/// We store each prefabfile in here indexed by their root object id, allowing
	/// us to discuss them over the network, because the net system will be able to
	/// look the GameObject up.
	/// </summary>
	private static Dictionary<Guid, PrefabFile> ObjectsById { get; } = new();

	/// <summary>
	/// We can look up prefabfile by their object guid
	/// </summary>
	internal static PrefabFile FindByGuid( Guid guid )
	{
		return ObjectsById.GetValueOrDefault( guid );
	}

	#endregion ObjectsById
}

/// <summary>
/// A prefab variable definition
/// </summary>
[Expose]
[Obsolete]
public class PrefabVariable
{
	/// <summary>
	/// A unique id for this variable. This is what it will be referred to in code.
	/// </summary>
	[ReadOnly]
	public string Id { get; set; }

	/// <summary>
	/// A user friendly title for this variable
	/// </summary>
	public string Title { get; set; }

	/// <summary>
	/// A user friendly description for this variable
	/// </summary>
	[TextArea]
	public string Description { get; set; }

	/// <summary>
	/// An optional group for this variable to belong to
	/// </summary>
	public string Group { get; set; }

	/// <summary>
	/// Lower numbers appear first
	/// </summary>
	public int Order { get; set; }

	/// <summary>
	/// Component variables that are being targetted
	/// </summary>
	[Hide]
	public List<PrefabVariableTarget> Targets { get; set; }

	/// <summary>
	/// Add a target property
	/// </summary>
	public void AddTarget( Guid id, string propertyName )
	{
		Targets.Add( new PrefabVariableTarget { Id = id, Property = propertyName } );
	}

	/// <summary>
	/// Targets a property in a component or gameobject.
	/// </summary>
	/// <param name="Id">The Id of the gameobject or component.</param>
	/// <param name="Property">The name of the parameter on the target.</param>
	public record struct PrefabVariableTarget( Guid Id, string Property );
}
