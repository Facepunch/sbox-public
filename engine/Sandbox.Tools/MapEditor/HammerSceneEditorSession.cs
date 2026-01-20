using Editor.MapDoc;
using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;
using System;

namespace Editor.MapEditor;

public partial class HammerSceneEditorSession : Scene.ISceneEditorSession
{
	public static List<HammerSceneEditorSession> All { get; } = new();

	public Scene Scene { get; }
	public MapWorld MapWorld { get; }

	// unused, but required by interface
	public SelectionSystem Selection { get; } = new SelectionSystem();

	public ISourceLocation SourceLocation { get; }

	private bool _destroyed;

	public bool HasUnsavedChanges
	{
		get => throw new NotImplementedException();
		set
		{
			if ( !value )
			{
				throw new NotImplementedException();
			}

			MapWorld.worldNative.SetModifiedFlag();
		}
	}

	public HammerSceneEditorSession( Scene scene, MapWorld mapWorld )
	{
		ArgumentNullException.ThrowIfNull( scene );
		ArgumentNullException.ThrowIfNull( mapWorld );

		Scene = scene;
		MapWorld = mapWorld;

		Scene.Editor = this;

		SourceLocation = new HammerSourceLocation( this );

		All.Add( this );
	}

	public void Destroy()
	{
		_destroyed = true;

		All.Remove( this );
	}

	public void Focus()
	{
		if ( _destroyed )
		{
			Log.Error( $"Editor session destroyed!" );
			return;
		}

		Hammer.Window.Focus();
		// TODO
	}

	void Scene.ISceneEditorSession.AddSelectionUndo()
	{

	}

	void Scene.ISceneEditorSession.OnEditLog( string name, object source )
	{
		HasUnsavedChanges = true;
	}

	void Scene.ISceneEditorSession.FrameTo( in BBox box )
	{

	}

	void Scene.ISceneEditorSession.Save( bool forceSaveAs )
	{
		throw new NotImplementedException();
	}

	void Scene.ISceneEditorSession.RecordChange( SerializedProperty property )
	{
		HasUnsavedChanges = true;
	}

	void Scene.ISceneEditorSession.AddUndo( string name, Action undo, Action redo )
	{

	}

	/// <summary>
	/// Resolve a map path name to an editor session.
	/// </summary>
	public static HammerSceneEditorSession Resolve( string mapPathName )
	{
		return All.FirstOrDefault( x => string.Equals( x.MapWorld.MapPathName, mapPathName, StringComparison.OrdinalIgnoreCase ) );
	}

	public static HammerSceneEditorSession Resolve( ISourceLocation sourceLocation )
	{
		return sourceLocation switch
		{
			MapSourceLocation { MapPathName: { } mapPathName } => Resolve( mapPathName ),
			HammerSourceLocation { EditorSession: { } session } => session,
			_ => null
		};
	}

	public IEnumerable<object> GetSelection()
	{
		yield break;
	}

	BaseFileSystem Scene.ISceneEditorSession.TransientFilesystem => FileSystem.Transient;
}

/// <summary>
/// Source location for graphs created in a Hammer editor session.
/// </summary>
public record HammerSourceLocation( HammerSceneEditorSession EditorSession ) : ISerializationOptionProvider
{
	public override string ToString()
	{
		return $"Map:{EditorSession.MapWorld.MapPathName}";
	}

	public SerializationOptions SerializationOptions =>
		MapSourceLocation.Get( EditorSession.MapWorld.MapPathName )
			.SerializationOptions with
		{ SourceLocation = this };
}
