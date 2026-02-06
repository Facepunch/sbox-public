namespace Sandbox
{
	public partial class Scene : GameObject
	{
		/// <summary>
		/// Allows access to the scene's editor session from the game. This will be null if there is no
		/// editor session active on this scene.
		/// </summary>
		public ISceneEditorSession Editor { get; internal set; }

		public interface ISceneEditorSession
		{
			/// <summary>
			/// The scene for this session
			/// </summary>
			Scene Scene { get; }

			/// <summary>
			/// True if this scene has unsaved changes
			/// </summary>
			bool HasUnsavedChanges { get; set; }

			/// <summary>
			/// Selection system for this session
			/// </summary>
			SelectionSystem Selection { get; }

			/// <summary>
			/// If we have any gameobjects selected, return the first one
			/// </summary>
			public GameObject SelectedGameObject => Selection.OfType<GameObject>().FirstOrDefault();

			/// <summary>
			/// You have changed the editor's selection, add a new undo entry
			/// </summary>
			[Obsolete]
			void AddSelectionUndo();

			[Obsolete]
			void OnEditLog( string name, object source );

			/// <summary>
			/// Focus the editor camera onto this box
			/// </summary>
			void FrameTo( in BBox box );

			/// <summary>
			/// Save this scene to disk
			/// </summary>
			void Save( bool forceSaveAs );

			/// <summary>
			/// Tell undo about this property change
			/// </summary>
			[Obsolete]
			void RecordChange( SerializedProperty property );

			/// <summary>
			/// Add a new undo entry
			/// </summary>
			void AddUndo( string name, Action undo, Action redo );

			ISceneUndoScope UndoScope( string name );

			/// <summary>
			/// Gets the current selection from the editor
			/// </summary>
			[Obsolete( "Use Selection" )]
			public IEnumerable<object> GetSelection();

			/// <summary>
			/// Get the filesystem in which temporary files can be created. These files can be used (and shipped) by a package, but won't be stored in source control.
			/// This is usually used for files that are expected to be re-generated at runtime by the package itself.
			/// </summary>
			public BaseFileSystem TransientFilesystem { get; }

			/// <summary>
			/// Get the folder for this scene. This is a folder in which we can store assets that are referenced by this scene. Things like envmap textures, lightmaps, baked data, etc.
			/// </summary>
			public Editor.SceneFolder GetSceneFolder() => null;
		}
	}
}

[Flags]
public enum GameObjectUndoFlags
{
	Properties = 0,
	Components = 1,
	Children = 2,
	All = Properties | Components | Children
}

public interface ISceneUndoScope
{
	ISceneUndoScope WithGameObjectCreations();
	ISceneUndoScope WithGameObjectDestructions( IEnumerable<GameObject> gameObjects );
	ISceneUndoScope WithGameObjectDestructions( GameObject gameObject );
	ISceneUndoScope WithGameObjectChanges( IEnumerable<GameObject> objects, GameObjectUndoFlags flags );
	ISceneUndoScope WithGameObjectChanges( GameObject gameObject, GameObjectUndoFlags flags );
	ISceneUndoScope WithComponentCreations();
	ISceneUndoScope WithComponentDestructions( IEnumerable<Component> components );
	ISceneUndoScope WithComponentDestructions( Component component );
	ISceneUndoScope WithComponentChanges( IEnumerable<Component> components );
	ISceneUndoScope WithComponentChanges( Component component );
	IDisposable Push();
}


namespace Editor
{
	public abstract class SceneFolder
	{
		/// <summary>
		/// Write a file to the path relative to the scene folder, return the path relative to the root.
		/// </summary>
		public abstract string WriteFile( string filename, byte[] data );
	}
}
