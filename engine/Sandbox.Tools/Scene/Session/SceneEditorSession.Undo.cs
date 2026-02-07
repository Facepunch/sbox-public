using Sandbox.Helpers;
using System;
using System.Text.Json.Nodes;

namespace Editor;

public partial class SceneEditorSession
{
	public UndoSystem UndoSystem { get; } = new UndoSystem();

	internal bool IsUndoScopeOpen = false;

	private void InitUndo()
	{
		UndoSystem.Initialize();

		// annoy everyone as much as possible
		UndoSystem.OnUndo = ( x ) =>
		{
			if ( EditorPreferences.UndoSounds )
			{
				EditorUtility.PlayRawSound( "sounds/editor/success.wav" );
			}

			HasUnsavedChanges = true;
		};
		UndoSystem.OnRedo = ( x ) =>
		{
			if ( EditorPreferences.UndoSounds )
			{
				EditorUtility.PlayRawSound( "sounds/editor/success.wav" );
			}

			HasUnsavedChanges = true;
		};
	}

	/// <summary>
	/// Take a full scene snapshot for the undo system. This is usually a last resort, if you can't do anything more incremental.
	/// </summary>
	[Obsolete( "Manual full scene undo snapshots are no longer working use UndoScope or AddUndo" )]
	public void FullUndoSnapshot( string title )
	{
	}

	/// <summary>
	/// Push the current selection into the undo system
	/// </summary>
	[Obsolete( "Manual selections snapshots are no longer working use UndoScope or AddUndo" )]
	public void PushUndoSelection()
	{
	}

	[Obsolete( "EditLog is no longer working use UndoScope or AddUndo" )]
	void Scene.ISceneEditorSession.OnEditLog( string name, object source )
	{
	}

	public void AddUndo( string name, Action undo, Action redo )
	{
		UndoSystem.Insert( name, undo, redo );
	}

	public ISceneUndoScope UndoScope( string name )
	{
		return new SceneUndoScope( this, name );
	}
}

internal sealed class SceneUndoSnapshot : IDisposable
{
	sealed record ScopeSnapshot( JsonObject Scene, SelectionSnapshot Selection, ComponentSnapshot ComponentSnapshot, GameObjectSnapshot GameObjectSnapshot )
	{
		public bool Equals( ScopeSnapshot other )
		{
			if ( other == null )
				return false;
			if ( Scene == other.Scene &&
				Selection == other.Selection &&
				ComponentSnapshot == other.ComponentSnapshot &&
				GameObjectSnapshot == other.GameObjectSnapshot )
			{
				return true;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine( Scene, Selection, ComponentSnapshot, GameObjectSnapshot );
		}
	}

	sealed record SelectionSnapshot
	{
		public readonly GameObjectReference[] SelectedGoRefs;
		public readonly ComponentReference[] SelectedComponentRefs;

		public struct ObjectReference
		{
			public Type Type;
			public JsonNode Node;
		}

		public readonly ObjectReference[] SelectedObjectRefs;
		private readonly SceneEditorSession _session;

		public SelectionSnapshot( SceneEditorSession session )
		{
			_session = session;

			SelectedGoRefs = _session.Selection.OfType<GameObject>().Select( GameObjectReference.FromInstance ).ToArray();
			SelectedComponentRefs = _session.Selection.OfType<Component>().Select( ComponentReference.FromInstance ).ToArray();
			SelectedObjectRefs = _session.Selection
				.Where( x => x is not GameObject and not Component and not null )
				.Select( x => new ObjectReference { Type = x.GetType(), Node = Json.ToNode( x ) } )
				.ToArray();
		}

		public void Restore( Scene scene )
		{
			_session.Selection.Clear();
			foreach ( var goRef in SelectedGoRefs )
			{
				var go = goRef.Resolve( scene, true );
				if ( go.IsValid() )
				{
					_session.Selection.Add( go );
				}
			}

			foreach ( var compRef in SelectedComponentRefs )
			{
				var comp = compRef.Resolve( scene );
				if ( comp.IsValid() )
				{
					_session.Selection.Add( comp );
				}
			}

			// We have not garantuee those are still exist and are valid but we try
			foreach ( var objRef in SelectedObjectRefs )
			{
				var obj = Json.FromNode( objRef.Node, objRef.Type );
				if ( obj is IValid x && !x.IsValid() )
				{
					continue;
				}
				_session.Selection.Add( obj );
			}
		}

		public bool Equals( SelectionSnapshot other )
		{
			if ( other == null )
				return false;

			if ( SelectedGoRefs.Select( x => x.GameObjectId ).SequenceEqual( other.SelectedGoRefs.Select( x => x.GameObjectId ) ) &&
				SelectedComponentRefs.Select( x => x.GameObjectId ).SequenceEqual( other.SelectedComponentRefs.Select( x => x.GameObjectId ) ) &&
				SelectedObjectRefs.SequenceEqual( other.SelectedObjectRefs ) )
			{
				return true;
			}

			return false;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine( SelectedGoRefs, SelectedComponentRefs, SelectedObjectRefs );
		}
	}

	sealed record ComponentSnapshot
	{
		public readonly JsonNode[] State;
		public readonly ComponentReference[] ComponentRefs;

		public ComponentSnapshot( IEnumerable<Component> components )
		{
			ComponentRefs = components.Select( ComponentReference.FromInstance ).ToArray();

			var serializeOptions = new GameObject.SerializeOptions { };
			State = components.Select( comp => comp.Serialize( serializeOptions ) ).ToArray();
		}

		public void Restore( Scene scene )
		{
			for ( int i = 0; i < State.Length; i++ )
			{
				Component comp = null;
				comp = ComponentRefs[i].Resolve( scene );

				if ( comp is null )
				{
					continue;
				}

				comp.Deserialize( State[i].AsObject() );
			}
		}

		public void PostRestore( Scene scene )
		{
			foreach ( var compRef in ComponentRefs )
			{
				var comp = compRef.Resolve( scene );
				if ( comp.IsValid() )
				{
					comp.PostDeserialize();
				}
			}

			foreach ( var compRef in ComponentRefs )
			{
				var comp = compRef.Resolve( scene );
				if ( comp.IsValid() )
				{
					comp.Validate();
				}
			}
		}

		public bool Equals( ComponentSnapshot other )
		{
			if ( other == null )
				return false;
			if ( State == other.State &&
				ComponentRefs.Select( x => x.ComponentId ).SequenceEqual( other.ComponentRefs.Select( x => x.ComponentId ) ) )
			{
				return true;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine( State, ComponentRefs );
		}
	}

	sealed record GameObjectSnapshot
	{
		public readonly List<JsonObject> State;
		public readonly List<GameObjectReference> GameObjectRefs;
		public readonly List<GameObjectReference> GameObjectNextSiblingRefs;
		public readonly List<GameObjectReference> GameObjectParentRefs;

		public GameObjectSnapshot( Dictionary<GameObject, GameObjectUndoFlags> gameObjects )
		{
			var goCount = gameObjects.Count;
			GameObjectRefs = new( goCount );
			State = new( goCount );
			GameObjectNextSiblingRefs = new( goCount );
			GameObjectParentRefs = new( goCount );

			foreach ( var (go, flags) in gameObjects )
			{
				if ( !go.IsValid() )
				{
					Log.Info( $"Undo GameObjectSnapshot: GameObject queued for snapshot is not valid" );
					continue;
				}
				var serializeOptions = new GameObject.SerializeOptions { IgnoreChildren = !flags.HasFlag( GameObjectUndoFlags.Children ), IgnoreComponents = !flags.HasFlag( GameObjectUndoFlags.Components ) };
				GameObjectRefs.Add( GameObjectReference.FromInstance( go ) );
				if ( go.IsOutermostPrefabInstanceRoot ) go.PrefabInstance.RefreshPatch();
				State.Add( go.Serialize( serializeOptions ) );
				GameObjectNextSiblingRefs.Add( go.GetNextSibling( false ).IsValid() ? GameObjectReference.FromInstance( go.GetNextSibling( false ) ) : GameObjectReference.FromId( Guid.Empty ) );
				GameObjectParentRefs.Add( go.Parent.IsValid() ? GameObjectReference.FromInstance( go.Parent ) : GameObjectReference.FromId( Guid.Empty ) );
			}
		}

		public void Restore( Scene scene, HashSet<GameObjectReference> createdGos = null )
		{
			for ( int i = 0; i < State.Count; i++ )
			{
				GameObject go = null;
				if ( createdGos is not null && createdGos.Contains( GameObjectRefs[i] ) )
				{
					go = new GameObject();
					// just need the id for now to ensure references are resolved when doing full deserialize later
					go.DeserializeId( State[i] );
				}
				else
				{
					go = GameObjectRefs[i].Resolve( scene );
				}

				if ( go is null )
				{
					continue;
				}
			}
		}

		public void PostRestore( Scene scene )
		{
			// second pass fully desiarizes and restores hierachy
			for ( int i = 0; i < State.Count; i++ )
			{
				GameObject go = GameObjectRefs[i].Resolve( scene );

				if ( go is null )
				{
					continue;
				}

				go.Deserialize( State[i], new GameObject.DeserializeOptions { IsRefreshing = true } );
			}


			RestoreHierachy( scene );
		}

		private void RestoreHierachy( Scene scene )
		{
			// You could probably also sort the siblings in a smart way, but we will just brought force it in n^2
			for ( int j = 0; j < State.Count; j++ )
			{
				for ( int i = 0; i < State.Count; i++ )
				{
					GameObject go = GameObjectRefs[i].Resolve( scene );

					if ( go is null )
					{
						continue;
					}

					// restore position in scene hierachy
					var prevSibling = GameObjectNextSiblingRefs[i].Resolve( scene );
					var parent = GameObjectParentRefs[i].Resolve( scene );
					if ( prevSibling is not null )
					{
						prevSibling.AddSibling( go, true, false );
					}
					else if ( parent is not null )
					{
						go.SetParent( parent, false );
					}
					else
					{
						go.SetParent( scene );
					}
				}
			}
		}

		public bool Equals( GameObjectSnapshot other )
		{
			if ( other == null )
				return false;

			if ( State == other.State &&
				GameObjectRefs.Select( x => x.GameObjectId ).SequenceEqual( other.GameObjectRefs.Select( x => x.GameObjectId ) ) &&
				GameObjectNextSiblingRefs.Select( x => x.GameObjectId ).SequenceEqual( other.GameObjectNextSiblingRefs.Select( x => x.GameObjectId ) ) &&
				GameObjectParentRefs.Select( x => x.GameObjectId ).SequenceEqual( other.GameObjectParentRefs.Select( x => x.GameObjectId ) ) )
			{
				return true;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine( State, GameObjectNextSiblingRefs, GameObjectParentRefs );
		}
	}

	private readonly SceneEditorSession _session;
	private readonly string _name;

	private ScopeSnapshot _initialState;

	private HashSet<GameObject> _createdGameObjects = new();

	private HashSet<Component> _createdComponents = new();

	private Dictionary<GameObject, GameObjectUndoFlags> _initalCapturedGameObjects = new();

	private HashSet<Component> _initialCapturedComponents = new();

	private HashSet<Component> _capturedComponentsToValidate = new();

	private Dictionary<GameObject, GameObjectReference> _destroyedGameObjects { get; } = new();

	private Dictionary<Component, ComponentReference> _destroyedComponents { get; } = new();

	private bool _captureDestructions = false;

	private bool _captureComponentCreations = false;

	private bool _captureGameObjectCreations = false;

	private bool _captureSelections = false;

	private bool _captureComponentChanges => _initialCapturedComponents.Count > 0;

	private bool _validateComponents => _capturedComponentsToValidate.Count > 0;

	private bool _captureGameObjectChanges => _initalCapturedGameObjects.Count > 0;

	internal SceneUndoSnapshot( SceneUndoScope builder )
	{
		_session = builder.Session;

		using var sceneScope = _session.Scene.Push();

		_name = builder.Name;

		_session.IsUndoScopeOpen = true;

		// resolve builder contents
		foreach ( var (gos, flags) in builder.CapturedGameObjects )
		{
			foreach ( var go in gos )
			{
				// Need to capture the prefab root and only the prefab root if we edited an instance
				if ( go.IsPrefabInstance )
				{
					_initalCapturedGameObjects[go.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
					continue;
				}

				if ( _initalCapturedGameObjects.ContainsKey( go ) )
				{
					_initalCapturedGameObjects[go] |= flags;
				}
				else
				{
					_initalCapturedGameObjects[go] = flags;
				}
			}
		}


		foreach ( var destroyedGo in builder.DestroyedGameObjects )
		{
			// if destroyed go is part of prefab we need to update it's instance cache
			if ( destroyedGo.IsPrefabInstance && !destroyedGo.IsOutermostPrefabInstanceRoot )
			{
				_initalCapturedGameObjects[destroyedGo.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
			}
			// If we delete a nested instance we need to let a potential parent instance know and need to capture it.
			else if ( destroyedGo.IsOutermostPrefabInstanceRoot && destroyedGo.Parent.IsValid() && destroyedGo.Parent.IsPrefabInstance )
			{
				_initalCapturedGameObjects[destroyedGo.Parent.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
			}

			_destroyedGameObjects[destroyedGo] = GameObjectReference.FromInstance( destroyedGo );
		}

		foreach ( var destroyedComp in builder.DestroyedComponents )
		{
			// if destroyed component is part of prefab we need to update it's instance cache
			if ( destroyedComp.GameObject.IsPrefabInstance )
			{
				_initalCapturedGameObjects[destroyedComp.GameObject.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
			}
			_destroyedComponents[destroyedComp] = ComponentReference.FromInstance( destroyedComp );
		}

		foreach ( var comp in builder.CapturedComponents )
		{
			// Only validate components that were specifically captured
			_capturedComponentsToValidate.Add( comp );

			// Need to capture the prefab root and only the prefab root if we edited an instance
			if ( comp.GameObject.IsPrefabInstance )
			{
				_initalCapturedGameObjects[comp.GameObject.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
				continue;
			}

			// only add if parent is not already watched or does not have component flag
			if ( !_initalCapturedGameObjects.ContainsKey( comp.GameObject ) || !_initalCapturedGameObjects[comp.GameObject].Contains( GameObjectUndoFlags.Components ) )
			{
				_initialCapturedComponents.Add( comp );
			}
		}

		_captureDestructions = builder.CaptureDeletions;
		_captureComponentCreations = builder.CaptureComponentCreations;
		_captureGameObjectCreations = builder.CaptureGameObjectCreations;
		_captureSelections = builder.CaptureSelections;

		// Undo snapshots

		JsonObject scene = null;
		// if deletion is requested, we need to capture the whole scene
		if ( _captureDestructions )
		{
			scene = _session.Scene.Serialize();
		}

		SelectionSnapshot selection = null;
		if ( _captureSelections || _captureDestructions )
		{
			selection = new SelectionSnapshot( _session );
		}

		ComponentSnapshot componentSnapshot = null;
		// If deletion was requested we already captured the whole scene, no point in capturing individual components
		if ( !_captureDestructions && _initialCapturedComponents.Count > 0 )
		{
			componentSnapshot = new ComponentSnapshot( _initialCapturedComponents );
		}

		// If deletion was requested we already captured the whole scene, no point in capturing individual Fos
		GameObjectSnapshot gameObjectSnapshot = null;
		if ( !_captureDestructions && _initalCapturedGameObjects.Count > 0 )
		{
			gameObjectSnapshot = new GameObjectSnapshot( _initalCapturedGameObjects );
		}

		_initialState = new ScopeSnapshot( scene, selection, componentSnapshot, gameObjectSnapshot );

		if ( _captureGameObjectCreations )
		{
			_session.Scene.Directory.OnGameObjectAdded += OnGameObjectAdded;
		}
		if ( _captureComponentCreations )
		{
			_session.Scene.Directory.OnComponentAdded += OnComponentAdded;
		}
	}

	private bool _alreadyDisposed = false;

	public void Dispose()
	{
		if ( _alreadyDisposed )
		{
			return;
		}

		using var sceneScope = _session.Scene.Push();

		// Redo snapshots

		// we don't capture the scene here again because that is never needed for redo

		SelectionSnapshot selection = null;
		if ( _captureSelections || _captureDestructions )
		{
			selection = new SelectionSnapshot( _session );
		}

		Dictionary<GameObject, GameObjectUndoFlags> disposeWatchedGameObjects = new();

		// add all gos still valid and not destroyed
		foreach ( var (go, flags) in _initalCapturedGameObjects )
		{
			if ( go.IsPrefabInstance )
			{
				disposeWatchedGameObjects[go.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
			}

			// We may have moved this object to a different prefab instance => update prefabroot instead of it 
			if ( go.Parent.IsValid() && go.Parent.IsPrefabInstance )
			{
				disposeWatchedGameObjects[go.Parent.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
				continue;
			}
			if ( !go.IsValid() || go.IsDestroyed )
			{
				continue;
			}

			if ( !disposeWatchedGameObjects.ContainsKey( go ) )
			{
				disposeWatchedGameObjects.Add( go, flags );
			}
			else
			{
				disposeWatchedGameObjects[go] |= flags;
			}
		}

		// add all created gos
		HashSet<GameObjectReference> createdGameObjectRefs = new();
		foreach ( var go in _createdGameObjects )
		{
			if ( !go.IsValid() || go.IsDestroyed )
			{
				continue;
			}

			// Need to capture the prefab root and only the prefab root if we edited an instance
			if ( go.IsPrefabInstance )
			{
				disposeWatchedGameObjects[go.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
				// If we are a prefabinstanceroot and ahve been added to a prefab we also need to capture the outer prefab
				if ( go.IsOutermostPrefabInstanceRoot && go.Parent.IsValid() && go.Parent.IsPrefabInstance )
				{
					disposeWatchedGameObjects[go.Parent.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
				}
			}
			else
			{
				disposeWatchedGameObjects[go] = GameObjectUndoFlags.All;
			}
			createdGameObjectRefs.Add( GameObjectReference.FromInstance( go ) );
		}

		// for all components that have been created capture their parent go
		HashSet<ComponentReference> createdComponentRefs = new();
		foreach ( var component in _createdComponents )
		{
			if ( !component.IsValid() )
			{
				// can hapen if component was created and immediatly destoryed within undo scope
				continue;
			}

			// Need to capture the prefab root and only the prefab root if we edited an instance
			if ( component.GameObject.IsPrefabInstance )
			{
				disposeWatchedGameObjects[component.GameObject.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
			}
			else if ( disposeWatchedGameObjects.ContainsKey( component.GameObject ) )
			{
				disposeWatchedGameObjects[component.GameObject] |= GameObjectUndoFlags.Components;
			}
			else
			{
				disposeWatchedGameObjects[component.GameObject] = GameObjectUndoFlags.Components;
			}
			createdComponentRefs.Add( ComponentReference.FromInstance( component ) );
		}

		HashSet<Component> disposeWatchedComponents = new();
		foreach ( var comp in _initialCapturedComponents )
		{
			if ( !comp.IsValid() )
			{
				continue;
			}

			// Need to capture the prefab root and only the prefab root if we edited an instance
			if ( comp.GameObject.IsPrefabInstance )
			{
				disposeWatchedGameObjects[comp.GameObject.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
			}
			// only add if parent is not already watched or does not have component flag
			else if ( !_initalCapturedGameObjects.ContainsKey( comp.GameObject ) || !_initalCapturedGameObjects[comp.GameObject].Contains( GameObjectUndoFlags.Components ) )
			{
				disposeWatchedComponents.Add( comp );
			}
		}

		ComponentSnapshot componentSnapshot = null;
		if ( disposeWatchedComponents.Count > 0 )
		{
			componentSnapshot = new ComponentSnapshot( disposeWatchedComponents );
		}

		GameObjectSnapshot gameObjectSnapshot = null;
		if ( disposeWatchedGameObjects.Count > 0 )
		{
			gameObjectSnapshot = new GameObjectSnapshot( disposeWatchedGameObjects );
		}

		var disposeState = new ScopeSnapshot( null, selection, componentSnapshot, gameObjectSnapshot );

		var destroyedGameObjectRefs = _destroyedGameObjects.Select( x => x.Value ).ToArray();
		var destroyedComponentRefs = _destroyedComponents.Select( x => x.Value ).ToArray();

		var prefabInstanceRootsRequiringRefresh = new HashSet<GameObject>();

		_session.Scene.Directory.OnComponentAdded -= OnComponentAdded;
		_session.Scene.Directory.OnGameObjectAdded -= OnGameObjectAdded;

		// if nothing changed, don't add an undo
		if ( _initialState == disposeState )
		{
			return;
		}

		// Run OnValidate on specific components that were captured
		if ( _validateComponents )
		{
			foreach ( var comp in _capturedComponentsToValidate )
			{
				if ( comp.IsValid() )
				{
					comp.OnValidateInternal();
				}
			}
		}

		// check if we have non selection changes
		if ( _captureComponentChanges || _captureGameObjectChanges || _captureDestructions || _captureGameObjectCreations || _captureComponentCreations )
		{
			_session.HasUnsavedChanges = true;
		}

		// copy we want to avoid capture this
		var preChangeStateCopy = _initialState;
		_session.AddUndo( _name,
			() =>
			{
				using var sceneScope = _session.Scene.Push();

				// for undo we need to restore the state to the pre change state
				// first check if we have a scene to restore
				if ( preChangeStateCopy.Scene != null )
				{
					_session.Scene.Clear();

					using ( CallbackBatch.Isolated() )
					{
						_session.Scene.Deserialize( preChangeStateCopy.Scene );
					}

					// do a tick immediately, to make sure everything is up to date
					// if we don't do this we can get flickers when undo is called
					_session.Scene.EditorTick( RealTime.Now, RealTime.Delta );
				}
				else
				{
					using var batch = CallbackBatch.Batch();

					preChangeStateCopy.GameObjectSnapshot?.Restore( _session.Scene );
					preChangeStateCopy.ComponentSnapshot?.Restore( _session.Scene );

					preChangeStateCopy.ComponentSnapshot?.PostRestore( _session.Scene );
					preChangeStateCopy.GameObjectSnapshot?.PostRestore( _session.Scene );

					// delete created components
					foreach ( var compRef in createdComponentRefs )
					{
						compRef.Resolve( _session.Scene )?.Destroy();
					}

					// delete created gos
					foreach ( var goRef in createdGameObjectRefs )
					{
						goRef.Resolve( _session.Scene )?.Destroy();
					}
				}

				// At last restore selection
				preChangeStateCopy.Selection?.Restore( _session.Scene );
			},
			() =>
			{
				if ( disposeState.Scene != null )
				{
					throw new InvalidOperationException( "Redo should never use scene snapshots." );
				}

				using var sceneScope = _session.Scene.Push();

				using var batch = CallbackBatch.Batch();

				// delete destroyed components
				foreach ( var compRef in destroyedComponentRefs )
				{
					compRef.Resolve( _session.Scene )?.Destroy();
				}

				// delete destroyed gos
				foreach ( var goRef in destroyedGameObjectRefs )
				{
					goRef.Resolve( _session.Scene )?.Destroy();
				}

				// Restore Gos and pass created gos which need to be created first
				disposeState.GameObjectSnapshot?.Restore( _session.Scene, createdGameObjectRefs );
				disposeState.ComponentSnapshot?.Restore( _session.Scene );

				// Do actual deserialization
				disposeState.GameObjectSnapshot?.PostRestore( _session.Scene );
				disposeState.ComponentSnapshot?.PostRestore( _session.Scene );

				// restore selection
				if ( disposeState.Selection != null )
				{
					disposeState.Selection.Restore( _session.Scene );
				}
			} );

		_alreadyDisposed = true;
		_session.IsUndoScopeOpen = false;
	}

	private void OnComponentAdded( Component comp )
	{
		_createdComponents.Add( comp );
	}

	private void OnGameObjectAdded( GameObject go )
	{
		_createdGameObjects.Add( go );
	}
}

internal class SceneUndoScope : ISceneUndoScope
{
	internal SceneEditorSession Session { get; }
	internal string Name { get; }
	internal bool CaptureSelections { get; private set; }
	internal bool CaptureGameObjectCreations { get; private set; }
	internal bool CaptureComponentCreations { get; private set; }
	internal List<(GameObject[], GameObjectUndoFlags)> CapturedGameObjects { get; } = new();
	internal List<Component> CapturedComponents { get; } = new();
	internal List<GameObject> DestroyedGameObjects { get; } = new();
	internal List<Component> DestroyedComponents { get; } = new();
	internal bool CaptureDeletions => DestroyedGameObjects.Count > 0 || DestroyedComponents.Count > 0;

	public SceneUndoScope( SceneEditorSession session, string name )
	{
		Session = session;
		Name = name;

		// Always capture selections
		// If we ever want to make selections capture optional,
		// it should be opt out (IngoreSelections) rather than opt in.
		CaptureSelections = true;
	}
	public ISceneUndoScope WithGameObjectCreations()
	{
		CaptureGameObjectCreations = true;
		return this;
	}
	public ISceneUndoScope WithGameObjectDestructions( IEnumerable<GameObject> gameObjects )
	{
		DestroyedGameObjects.AddRange( gameObjects );
		return this;
	}
	public ISceneUndoScope WithGameObjectDestructions( GameObject gameObject )
	{
		DestroyedGameObjects.Add( gameObject );
		return this;
	}
	public ISceneUndoScope WithGameObjectChanges( IEnumerable<GameObject> objects, GameObjectUndoFlags flags )
	{
		CapturedGameObjects.Add( (objects.ToArray(), flags) );
		return this;
	}
	public ISceneUndoScope WithGameObjectChanges( GameObject gameObject, GameObjectUndoFlags flags )
	{
		CapturedGameObjects.Add( (new[] { gameObject }, flags) );
		return this;
	}
	public ISceneUndoScope WithComponentCreations()
	{
		CaptureComponentCreations = true;
		return this;
	}
	public ISceneUndoScope WithComponentDestructions( IEnumerable<Component> components )
	{
		DestroyedComponents.AddRange( components );
		return this;
	}
	public ISceneUndoScope WithComponentDestructions( Component component )
	{
		DestroyedComponents.Add( component );
		return this;
	}
	public ISceneUndoScope WithComponentChanges( IEnumerable<Component> components )
	{
		foreach ( var comp in components )
		{
			CapturedComponents.Add( comp );
		}
		return this;
	}
	public ISceneUndoScope WithComponentChanges( Component component )
	{
		CapturedComponents.Add( component );
		return this;
	}
	public IDisposable Push()
	{
		var snapshot = new SceneUndoSnapshot( this );
		return snapshot;
	}
}
