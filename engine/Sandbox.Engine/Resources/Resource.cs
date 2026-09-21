using System.Text.Json.Serialization;
using static Sandbox.BytePack;

namespace Sandbox;

/// <summary>
/// A resource loaded in the engine, such as a <see cref="Model"/> or <see cref="Material"/>.
/// </summary>
[Expose]
public abstract partial class Resource : IValid, IJsonConvert, BytePack.ISerializer
{
	/// <summary>
	/// ID of this resource,
	/// </summary>
	[Hide, JsonIgnore]
	[Obsolete( "ResourceId is obsolete and will be removed in the future." )]
	public int ResourceId { get; protected set; }

	/// Internal use only — a stable 64-bit hash of ResourcePath, used for networking and resource lookup.
	internal ulong ResourceIdLong { get; set; }

	/// <summary>
	/// GUID for this resource, if any.
	/// </summary>
	internal Guid Guid { get; set; }

	/// <summary>
	/// Path to this resource.
	/// </summary>
	[Hide, JsonIgnore]
	public string ResourcePath { get; protected set; }

	/// <summary>
	/// File name of the resource without the extension.
	/// </summary>
	[Hide, JsonIgnore]
	public string ResourceName { get; protected set; }

	/// <summary>
	/// This is what loads the resource. While this is alive the resource will be loaded.
	/// </summary>
	internal AsyncResourceLoader Manifest { get; set; }


	[Hide, JsonIgnore] public abstract bool IsValid { get; }
	[Hide, JsonIgnore] public virtual bool IsError => false;

	/// <summary>
	/// True if this resource has been changed but the changes aren't written to disk
	/// </summary>
	[Hide, JsonIgnore] public virtual bool HasUnsavedChanges => false;

	internal virtual void Destroy()
	{
		// Unregister on main thread
		// Null check because resource lirbary may already be gone.
		MainThread.Queue( () => { Game.Resources?.Unregister( this ); } );

		if ( Manifest != default ) MainThread.QueueDispose( Manifest );
		Manifest = default;

		GC.SuppressFinalize( this );
	}

	~Resource()
	{
		Destroy();
	}

	internal static string FixPath( string filename )
	{
		if ( filename == null )
			return "";

		filename = filename.NormalizeFilename( false );
		if ( filename.EndsWith( "_c" ) ) filename = filename[..^2];
		filename = filename.TrimStart( '/' );

		return filename;
	}

	/// <summary>
	/// Sets the ResourcePath, ResourceName and ResourceId from a resource path.
	/// Registers in the WeakIndex so the resource can be found by ResourceId for networking,
	/// without preventing garbage collection.
	/// This is intended for runtime/native resources only. Disk-based resources (GameResource)
	/// should use <see cref="ResourceSystem.Register"/> instead.
	/// </summary>
	internal void RegisterWeakResourceId( string resourcePath, Guid? guid = null )
	{
		ResourcePath = FixPath( resourcePath );
		ResourceName = System.IO.Path.GetFileNameWithoutExtension( ResourcePath );

#pragma warning disable CS0618 // Type or member is obsolete
		ResourceId = ResourcePath.FastHash();
#pragma warning restore CS0618 // Type or member is obsolete
		ResourceIdLong = ResourcePath.FastHash64();

		if ( guid is Guid g && g != default )
			Guid = g;

		Game.Resources.RegisterWeak( this );
	}

	/// <summary>
	/// Accessor for loading native resources, not great, doesn't need to handle GameResource
	/// </summary>
	internal static Resource LoadNative( Type t, ResourceId id )
	{
		if ( t == typeof( Material ) ) return Material.Load( id );
		if ( t == typeof( Texture ) ) return Texture.Load( id );
		if ( t == typeof( Model ) ) return Model.Load( id );
		if ( t == typeof( SoundFile ) ) return SoundFile.Load( id.Path ); // todo: guid me
		if ( t == typeof( AnimationGraph ) ) return AnimationGraph.Load( id );
		if ( t == typeof( Shader ) ) return Shader.Load( id );

		return null;
	}

	/// <summary>
	/// Called by OnResourceReloaded when a resource has been reloaded
	/// </summary>
	internal virtual void OnReloaded()
	{
	}

	internal static void OnResourceReloaded( string resourceName, IntPtr nativePointer )
	{
		Log.Trace( $"Resource Reloaded: '{resourceName}'" );

		if ( NativeResourceCache.TryGetValue( nativePointer.ToInt64(), out Resource resource ) )
		{
			var library = Engine.GlobalContext.Game.ResourceSystem ?? Engine.GlobalContext.Menu.ResourceSystem;

			library.MoveResource( resource, resourceName );

			Log.Trace( $" - '{resource}'" );
			resource?.OnReloaded();
		}
	}

	/// <summary>
	/// Called when this resource's file data has been loaded (or reloaded), while
	/// that data is still in memory. This is our chance to read anything we want
	/// out of the compiled file - custom blocks written by managed resource
	/// compilers, for example. The context is only valid during this call, so copy
	/// out what you need. Main thread.
	/// </summary>
	internal virtual void OnLoaded( ResourceLoadContext context )
	{
	}

	/// <summary>
	/// Called by the resource system when any resource's file data has been loaded,
	/// while that data is still in memory. Almost all resource loads are initiated
	/// from managed, so usually the wrapper already exists - find it by identity
	/// and hand it the file data via <see cref="OnLoaded"/>. Runs on the main
	/// thread, fires on reloads too.
	/// </summary>
	internal static void OnResourceLoaded( string resourceName, IntPtr header )
	{
		// This fires from the engine frame, outside any context scope - the wrapper
		// could be registered in either context's resource system, so check both.
		var resource = Engine.GlobalContext.Game.ResourceSystem.Get( typeof( Resource ), resourceName )
			?? Engine.GlobalContext.Menu.ResourceSystem.Get( typeof( Resource ), resourceName );

		resource?.OnLoaded( new ResourceLoadContext( resourceName, header ) );
	}

	/// <summary>
	/// Native dataabse has updated a GUID for a resource, update this side to match.
	/// </summary>
	internal static void OnGuidChanged( string resourceName, Guid guid )
	{
		// This fires from the engine frame, outside any context scope - the wrapper
		// could be registered in either context's resource system, so check both.
		var lib = Engine.GlobalContext.Game.ResourceSystem ?? Engine.GlobalContext.Menu.ResourceSystem;

		var resource = lib.Get( typeof( Resource ), resourceName );
		if ( resource.IsValid() )
		{
			lib.AssignGuid( resource, guid );
		}
	}

	/// <summary>
	/// Native has discovered that a resident resource's path has changed, update this side to match.
	/// </summary>
	internal static void OnResourcePathChanged( Guid guid, string newPath )
	{
		// not guaranteed to fire on the main thread - defer the actual index mutation, and
		// re-resolve the resource at that point rather than capturing it now, so this can't
		// race a more recent rename/unregister that happens before this runs.
		MainThread.Queue( () =>
		{
			var lib = Engine.GlobalContext.Game.ResourceSystem ?? Engine.GlobalContext.Menu.ResourceSystem;
			if ( lib.Get<Resource>( guid ) is { } resource )
			{
				lib.MoveResource( resource, newPath );
			}
		} );
	}

	public override string ToString()
	{
		return $"{GetType().Name}:{ResourceName}";
	}

	/// <summary>
	/// Should be called after the resource has been edited by the inspector
	/// </summary>
	public virtual void StateHasChanged()
	{

	}

	static object BytePack.ISerializer.BytePackRead( ref ByteStream bs, Type targetType )
	{
		var id = bs.Read<ulong>();
		// Fast path: already loaded in one of the indexes, garantueed for GameResources
		var resource = Game.Resources.GetByIdLong<Resource>( id );
		if ( resource != null ) return resource;

		// Slow path: native resource exists on disk but hasn't been loaded yet (common on connecting clients).
		var path = Game.Resources.LookupPath( id );
		if ( path == null ) return null;
		return Load( targetType, path );
	}

	static void BytePack.ISerializer.BytePackWrite( object value, ref ByteStream bs )
	{
		if ( value is not Resource resource )
		{
			bs.Write( 0UL );
			return;
		}

		bs.Write( resource.ResourceIdLong );
	}
}
