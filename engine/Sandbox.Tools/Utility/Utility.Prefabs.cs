using System;

namespace Editor;

public static partial class EditorUtility
{
	public static partial class Prefabs
	{
		/// <summary>
		/// Returns the name of the prefab file that this GameObject or Component is an instance of.
		/// </summary>

		public static bool IsOuterMostPrefabRoot( object obj )
		{
			var go = ResolveGameObject( obj );

			if ( !go.IsValid() ) return false;
			if ( !go.IsPrefabInstance ) return false;

			return go.IsOutermostPrefabInstanceRoot;
		}

		/// <summary>
		/// Returns the name of the prefab file that this GameObject or Component is an instance of.
		/// </summary>

		public static string GetOuterMostPrefabName( object obj )
		{
			var go = ResolveGameObject( obj );

			if ( !go.IsValid() ) return null;
			if ( !go.IsPrefabInstance ) return null;

			var prefabName = System.IO.Path.GetFileNameWithoutExtension( go.OutermostPrefabInstanceRoot.PrefabInstanceSource );

			return prefabName.ToTitleCase();
		}

		/// <summary>
		/// <see cref="PrefabInstanceData.IsPropertyOverridden"/>
		/// </summary>
		public static bool IsPropertyOverridden( SerializedProperty prop )
		{
			var obj = prop.Parent;

			if ( obj is null ) return false;

			var go = obj.Targets.Select( ResolveGameObject ).FirstOrDefault();

			var owner = obj.Targets.FirstOrDefault();

			if ( !go.IsValid() ) return false;
			if ( !go.IsPrefabInstance ) return false;

			if ( !prop.IsEditable ) return false;

			return go.OutermostPrefabInstanceRoot.PrefabInstance.IsPropertyOverridden( owner, prop.Name, go.IsPrefabInstanceRoot );
		}

		/// <summary>
		/// <see cref="PrefabInstanceData.IsAddedGameObject"/>
		/// </summary>
		public static bool IsGameObjectAddedToInstance( GameObject go )
		{
			if ( !go.IsValid() ) return false;
			if ( !go.IsPrefabInstance ) return false;

			if ( go.IsOutermostPrefabInstanceRoot && go.Parent is not null && go.Parent.IsPrefabInstance )
			{
				return go.Parent.OutermostPrefabInstanceRoot.PrefabInstance.IsAddedGameObject( go );
			}

			return go.OutermostPrefabInstanceRoot.PrefabInstance.IsAddedGameObject( go );
		}

		/// <summary>
		/// <see cref="PrefabInstanceData.IsAddedComponent"/>
		/// </summary>
		public static bool IsComponentAddedToInstance( Component comp )
		{
			if ( !comp.IsValid() ) return false;
			if ( !comp.GameObject.IsPrefabInstance ) return false;

			return comp.GameObject.OutermostPrefabInstanceRoot.PrefabInstance.IsAddedComponent( comp );
		}

		/// <summary>
		/// <see cref="PrefabInstanceData.IsModified"/>
		/// </summary>
		public static bool IsInstanceModified( GameObject prefabInstance )
		{
			if ( !prefabInstance.IsValid() ) return false;
			if ( !prefabInstance.IsPrefabInstanceRoot ) return false;

			return prefabInstance.IsOutermostPrefabInstanceRoot && prefabInstance.OutermostPrefabInstanceRoot.PrefabInstance.IsModified();
		}

		/// <summary>
		/// <see cref="PrefabInstanceData.IsGameObjectModified"/>
		/// </summary>
		public static bool IsGameObjectInstanceModified( GameObject go )
		{
			if ( !go.IsValid() ) return false;
			if ( !go.IsPrefabInstance ) return false;

			return go.OutermostPrefabInstanceRoot.PrefabInstance.IsGameObjectModified( go, go.IsOutermostPrefabInstanceRoot );
		}

		/// <summary>
		/// <see cref="PrefabInstanceData.IsComponentModified"/>
		/// </summary>
		public static bool IsComponentInstanceModified( Component comp )
		{
			if ( !comp.GameObject.IsValid() ) return false;
			if ( !comp.GameObject.IsPrefabInstance ) return false;

			return comp.GameObject.OutermostPrefabInstanceRoot.PrefabInstance.IsComponentModified( comp );
		}

		/// <summary>
		/// Returns true if the owning GameObject is part of a prefab instance.
		/// </summary>
		public static bool IsComponentPartOfInstance( Component comp )
		{
			if ( !comp.IsValid() ) return false;
			if ( !comp.GameObject.IsValid() ) return false;
			if ( !comp.GameObject.IsPrefabInstance ) return false;

			return comp.GameObject.OutermostPrefabInstanceRoot.IsPrefabInstance;
		}

		/// <summary>
		/// <see cref="PrefabInstanceData.RevertPropertyChange"/>
		/// </summary>
		public static void RevertPropertyChange( SerializedProperty prop )
		{
			var obj = prop.Parent;

			if ( obj is null ) return;

			var go = obj.Targets.Select( ResolveGameObject ).FirstOrDefault();

			var owner = obj.Targets.FirstOrDefault();

			if ( !go.IsValid() ) return;
			if ( !go.IsPrefabInstance ) return;

			go.OutermostPrefabInstanceRoot.PrefabInstance.RevertPropertyChange( owner, prop.Name );
		}

		/// <summary>
		/// <see cref="PrefabInstanceData.ApplyPropertyChangeToPrefab"/>
		/// </summary>
		public static void ApplyPropertyChange( SerializedProperty prop )
		{
			var obj = prop.Parent;

			if ( obj is null ) return;

			var go = obj.Targets.Select( ResolveGameObject ).FirstOrDefault();
			var session = SceneEditorSession.Resolve( go );
			using var scene = session.Scene.Push();

			var owner = obj.Targets.FirstOrDefault();

			if ( !go.IsValid() ) return;
			if ( !go.IsPrefabInstance ) return;

			// Store go id we use it to restore inspector
			// Need to use id to restore inspector because prefab refresh may destroy references
			var goId = go.Id;

			go.OutermostPrefabInstanceRoot.PrefabInstance.ApplyPropertyChangeToPrefab( owner, prop.Name );

			UpdatePrefabAfterModification( go.OutermostPrefabInstanceRoot.PrefabInstanceSource );

			EditorUtility.InspectorObject = session.Scene.Directory.FindByGuid( goId );
		}

		/// <summary>
		/// <see cref="PrefabInstanceData.RevertComponentChanges"/>
		/// </summary>
		public static void RevertComponentInstanceChanges( Component comp )
		{
			if ( !comp.IsValid() ) return;
			if ( !comp.GameObject.IsPrefabInstance ) return;

			comp.GameObject.OutermostPrefabInstanceRoot.PrefabInstance.RevertComponentChanges( comp );
		}

		/// <summary>
		/// <see cref="PrefabInstanceData.RevertGameObjectChanges"/>
		/// </summary>
		public static void RevertGameObjectInstanceChanges( GameObject go )
		{
			if ( !go.IsValid() ) return;
			if ( !go.IsPrefabInstance ) return;

			go.OutermostPrefabInstanceRoot.PrefabInstance.RevertGameObjectChanges( go );
		}

		/// <summary>
		/// <see cref="PrefabInstanceData.ApplyComponentChangesToPrefab"/>
		/// </summary>
		public static void ApplyComponentInstanceChangesToPrefab( Component comp )
		{
			var session = SceneEditorSession.Resolve( comp );
			using var scene = session.Scene.Push();

			if ( !comp.IsValid() ) return;
			if ( !comp.GameObject.IsPrefabInstance ) return;

			// Store go id we use it to restore inspector
			// Need to use id to restore inspector because prefab refresh may destroy references
			var goId = comp.GameObject.Id;

			comp.GameObject.OutermostPrefabInstanceRoot.PrefabInstance.ApplyComponentChangesToPrefab( comp );

			UpdatePrefabAfterModification( comp.GameObject.OutermostPrefabInstanceRoot.PrefabInstanceSource );

			EditorUtility.InspectorObject = session.Scene.Directory.FindByGuid( goId );
		}

		/// <summary>
		/// <see cref="PrefabInstanceData.AddGameObjectToPrefab"/>
		/// </summary>
		public static void AddInstanceAddedGameObjectToPrefab( GameObject go )
		{
			var session = SceneEditorSession.Resolve( go );
			using var scene = session.Scene.Push();

			if ( !go.IsValid() ) return;
			if ( !go.IsPrefabInstance ) return;

			// Store go id we use it to restore inspector
			// Need to use id to restore inspector because prefab refresh may destroy references
			var goId = go.Id;

			// Call AddGameObject on the correct prefab instance
			// If go is an Added Prefab Instance
			// we need to add it to the parent prefab instance
			if ( go.IsOutermostPrefabInstanceRoot && go.Parent.IsValid() && go.Parent.IsPrefabInstance )
			{
				go.Parent.OutermostPrefabInstanceRoot.PrefabInstance.AddGameObjectToPrefab( go );
			}
			else
			{
				go.OutermostPrefabInstanceRoot.PrefabInstance.AddGameObjectToPrefab( go );
			}

			UpdatePrefabAfterModification( go.OutermostPrefabInstanceRoot.PrefabInstanceSource );

			EditorUtility.InspectorObject = session.Scene.Directory.FindByGuid( goId );
		}


		/// <summary>
		/// <see cref="PrefabInstanceData.ApplyGameObjectChangesToPrefab"/>
		/// </summary>
		public static void ApplyGameObjectInstanceChangesToPrefab( GameObject go )
		{
			var session = SceneEditorSession.Resolve( go );
			using var scene = session.Scene.Push();

			if ( !go.IsValid() ) return;
			if ( !go.IsPrefabInstance ) return;

			// Store go id we use it to restore inspector
			// Need to use id to restore inspector because prefab refresh may destroy references
			var goId = go.Id;

			go.OutermostPrefabInstanceRoot.PrefabInstance.ApplyGameObjectChangesToPrefab( go );

			UpdatePrefabAfterModification( go.OutermostPrefabInstanceRoot.PrefabInstanceSource );

			EditorUtility.InspectorObject = session.Scene.Directory.FindByGuid( goId );
		}

		/// <summary>
		/// Revert a prefab instance to the state of the prefab.
		/// </summary>
		public static void RevertInstanceToPrefab( GameObject go )
		{
			if ( !go.IsPrefabInstance )
			{
				return;
			}

			go.OutermostPrefabInstanceRoot.PrefabInstance.ClearPatch( true );
			go.UpdateFromPrefab();
		}

		/// <summary>
		/// Write a prefab instance back to the prefab file and save it to disk.
		/// </summary>
		public static void WriteInstanceToPrefab( GameObject go, bool skipDiskWrite = false )
		{
			if ( !go.IsValid() ) return;
			if ( !go.IsPrefabInstance ) return;

			var prefabSource = go.OutermostPrefabInstanceRoot.PrefabInstanceSource;

			WriteGameObjectToPrefab( go, prefabSource, skipDiskWrite );
		}

		private static (PrefabFile, Dictionary<Guid, Guid>) WriteGameObjectToPrefab( GameObject go, string saveLocation, bool skipDiskWrite = false )
		{
			if ( !go.IsValid() )
			{
				Log.Warning( "Failed to write GameObject to prefab, GameObject is invalid." );
				return (null, null);
			}

			// We cannot allow writing prefabs to disk that contain themselves as instance.
			if ( go.IsPrefabInstanceRoot )
			{
				foreach ( var prefabInstanceRoot in go.GetAllObjects( false ).Where( x => x.IsPrefabInstanceRoot ) )
				{
					if ( prefabInstanceRoot != go && prefabInstanceRoot.PrefabInstanceSource == go.PrefabInstanceSource )
					{
						Log.Warning( $"Failed to write GameObject to prefab, {prefabInstanceRoot.PrefabInstanceSource} occurs more than once in hierarchy." );
						EditorUtility.PlayRawSound( "sounds/editor/fail.wav" );
						return (null, null);
					}
				}
			}

			// Save instance transform for later
			var instanceTransform = go.LocalTransform;

			var prefabFile = ResourceLibrary.Get<PrefabFile>( saveLocation );
			if ( !prefabFile.IsValid() )
			{
				prefabFile = new PrefabFile();
				prefabFile.RegisterWeakResourceId( saveLocation );
				prefabFile.Register( saveLocation );
			}

			Dictionary<Guid, Guid> instanceToPrefabGuid = null;

			bool isWritingBackToExistingInstance = go.IsOutermostPrefabInstanceRoot && prefabFile.ResourcePath == go.PrefabInstanceSource;
			// Some special care when writing back existing prefab instances
			// Need to keep ids up to date
			if ( isWritingBackToExistingInstance )
			{
				go.OutermostPrefabInstanceRoot.PrefabInstance.OverridePrefabWithInstance();
			}
			else
			{
				// Zero instance location for serialization to prefab file
				go.LocalPosition = Vector3.Zero;

				prefabFile.RootObject = go.SerializeStandard( new GameObject.SerializeOptions() );
				instanceToPrefabGuid = SceneUtility.MakeIdGuidsUnique( prefabFile.RootObject );

				// Reset transform
				go.LocalTransform = instanceTransform;
			}

			UpdatePrefabAfterModification( prefabFile.ResourcePath, skipDiskWrite );

			if ( isWritingBackToExistingInstance )
			{
				go.PrefabInstance.RefreshPatch();
			}

			return (prefabFile, instanceToPrefabGuid);
		}

		private static void WritePrefabToDisk( PrefabFile prefabFile, string saveLocation )
		{
			var asset = AssetSystem.FindByPath( saveLocation );
			if ( asset is null )
			{
				asset = AssetSystem.CreateResource( "prefab", saveLocation );
			}
			asset.SaveToDisk( prefabFile );
		}

		private static void UpdatePrefabAfterModification( string prefabSource, bool skipFileWrite = false )
		{
			var prefabFile = ResourceLibrary.Get<PrefabFile>( prefabSource );

			// Need to write back the changes to the editor session of this prefab if it exists, before updating the instances
			var openSession = SceneEditorSession.All.OfType<PrefabEditorSession>().FirstOrDefault( session => session.Scene.Source.ResourcePath == prefabSource );
			if ( openSession is not null )
			{
				using var undo = openSession.UndoScope( "Apply Prefab Instance Changes" )
					.WithGameObjectChanges( openSession.Scene, GameObjectUndoFlags.All )
					.Push();

				openSession.Scene.Clear();
				openSession.Scene.Deserialize( prefabFile.RootObject );
			}

			// TODO this only reason for skipFileWrite exists is because we cannot easily spinup an asset system in tests
			if ( !skipFileWrite )
			{
				WritePrefabToDisk( prefabFile, prefabFile.ResourcePath );
			}
		}

		/// <summary>
		/// Convert a GameObject to a prefab. This will write the newly created prefab to disk and set the prefab source on the GameObject.
		/// </summary>
		public static void ConvertGameObjectToPrefab( GameObject go, string saveLocation, bool skipDiskWrite = false )
		{
			// We cannot convert the existing go in-place if it's part of a prefab instance.
			if ( go.IsPrefabInstance )
			{
				var oldGo = go;
				var oldGoSibling = go.GetNextSibling( false );
				go = go.Clone();
				if ( oldGoSibling != null )
				{
					oldGoSibling.AddSibling( go, true );
				}
				else
				{
					go.Parent = oldGo.Parent;
				}
				oldGo.OutermostPrefabInstanceRoot.PrefabInstance.RemoveHierarchyFromLookup( oldGo );
				oldGo.Destroy();

			}

			var (prefabFile, instanceToPrefabGuid) = WriteGameObjectToPrefab( go, saveLocation, skipDiskWrite );

			if ( prefabFile is null )
			{
				Log.Warning( "Failed to convert GameObject to prefab, could not write file." );
				return;
			}

			// set or change prefab source
			go.InitPrefabInstance( prefabFile.ResourcePath, false );

			// Invert lookup
			var prefabToInstanceGuid = new Dictionary<Guid, Guid>( instanceToPrefabGuid.Count );
			foreach ( var kvp in instanceToPrefabGuid )
			{
				prefabToInstanceGuid[kvp.Value] = kvp.Key;
			}
			go.PrefabInstance.InitLookups( prefabToInstanceGuid );
			go.PrefabInstance.RefreshPatch();
		}

		private static GameObject ResolveGameObject( object target )
		{
			if ( target is GameTransform gt ) return gt.GameObject;
			if ( target is GameObject go ) return go;
			if ( target is Component c ) return c.GameObject;

			return default;
		}

		/// <summary>
		/// Get a SerializedProperty representing variable targets. Will return null if there are no targets
		/// </summary>
		[Obsolete]
		public static SerializedProperty GetTargets( GameObject root, PrefabVariable variable )
		{
			return null;
		}

		[Obsolete]
		public static PrefabScene.VariableCollection GetVariables( SerializedObject obj )
		{
			return new PrefabScene.VariableCollection();
		}

		/// <summary>
		/// Create a prefab out of any GameObject
		/// </summary>
		[Obsolete]
		public static PrefabFile CreateAsset( GameObject clone )
		{
			return null;
		}

		/// <summary>
		/// Fetches all prefab templates to show in Create GameObject menus
		/// </summary>
		public static IEnumerable<PrefabFile> GetTemplates()
		{
			return ResourceLibrary.GetAll<PrefabFile>()
				.Where( x => x.IsValid() && x.ShowInMenu )
				.GroupBy( x => x.MenuPath ?? string.Empty )
				.Select( g => g.First() )
				.OrderByDescending( x => (x.MenuPath ?? string.Empty).Count( c => c == '/' ) )
				.ThenBy( x => x.MenuPath ?? string.Empty );
		}
	}
}
