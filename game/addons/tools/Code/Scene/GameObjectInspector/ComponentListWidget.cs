using Sandbox.Diagnostics;

public class ComponentListWidget : Widget
{
	SerializedObject SerializedObject { get; init; }

	Guid GameObjectId;

	public ComponentListWidget( SerializedObject so ) : base( null )
	{
		SerializedObject = so;

		GameObjectId = so.IsMultipleTargets ? Guid.Empty : so.Targets.OfType<GameObject>().Select( x => x.Id ).Single();
		Layout = Layout.Column();
		HorizontalSizeMode = SizeMode.Flexible;
		Frame();
	}

	Dictionary<Component, ComponentSheet> current = new Dictionary<Component, ComponentSheet>();

	int hotloadCount;

	[EditorEvent.Hotload]
	private void Hotload()
	{
		hotloadCount++;
		current.Clear();
	}

	void Rebuild()
	{
		var gobs = SerializedObject.Targets.OfType<GameObject>().ToArray();
		if ( gobs.Length == 0 ) return;

		var newLayout = Layout.Column();

		var old = current;
		current = new();

		// A list of components we've already seen
		HashSet<Component> used = new HashSet<Component>();
		HashSet<Component> toRemove = new HashSet<Component>();

		//
		// Here we build a list of all components that are on all the selected game objects
		// This is for multi-select editing. This allows users to select multiple GameObjects
		// and edit the components that all the GameObjects have in common.
		//

		//
		// Foreach component on the first selected object
		// look on all the other selected objects for the same component
		//  - that are the same type
		//  - that we haven't used before
		// add them into a multiselect object
		// create a component sheet for them
		//
		foreach ( var component in gobs[0].Components.GetAll() )
		{
			if ( !component.IsValid() ) continue;
			if ( component.Flags.HasFlag( ComponentFlags.Hidden ) ) continue;
			var baseType = EditorTypeLibrary.GetType( component.GetType() )?.BaseType?.TargetType;
			if ( !(baseType == typeof( Component ) || (baseType?.IsSubclassOf( typeof( Component ) ) ?? false)) )
			{
				toRemove.Add( component );
				continue;
			}

			// Get all the components of this type on all the selected game objects
			var allGobs = gobs.Select( x => x.Components.GetAll()
										.Where( y => !used.Contains( y ) )
										.Where( y => y?.GetType() == component?.GetType() )
										.FirstOrDefault() )
									.ToArray();

			// Must be one on every go to show up
			if ( allGobs.Length != gobs.Length ) continue;
			if ( allGobs.Any( x => !x.IsValid() ) ) continue;

			if ( old.TryGetValue( component, out var existingSheet ) )
			{
				newLayout.Add( existingSheet );
				current[component] = existingSheet;
				old.Remove( component );
				used.Add( component );

				continue;
			}

			MultiSerializedObject mso = new MultiSerializedObject();

			foreach ( var entry in allGobs )
			{
				var serialized = entry.GetSerialized();
				mso.Add( serialized );

				used.Add( entry );
			}

			mso.Rebuild();

			mso.OnPropertyStartEdit += p => PropertyStartEdit( p, mso.Targets.OfType<Component>() );
			mso.OnPropertyFinishEdit += p => PropertyFinishEdit( p, mso.Targets.OfType<Component>() );
			mso.OnPropertyChanged += p => PropertyChanged( p, mso.Targets.OfType<Component>() );

			var sheet = new ComponentSheet( GameObjectId, mso );
			sheet.ReadOnly = component.Flags.Contains( ComponentFlags.NotEditable );
			sheet.Header.ContextMenu += ( menu ) => ContextMenu( component, menu, mso.TypeName );
			newLayout.Add( sheet );

			current[component] = sheet;
		}

		foreach ( var t in toRemove )
		{
			t?.Destroy();
		}

		Layout.Clear( true );
		Layout.Add( newLayout );
	}

	// We unfortunatly need to store the full serialized component for undo
	// Storing individual properties would fail if their owning component is deleted post creating the undo
	private IDisposable undoScope;

	void PropertyStartEdit( SerializedProperty property, IEnumerable<Component> components )
	{
		ActivateSession();

		var propertyDisplayName = property.Parent.ParentProperty is null
			? property.Name
			: $"{property.Parent.ParentProperty.Name}.{property.Name}";
		var undoName = $"Edit {propertyDisplayName} on {components.First().GetType().Name}";

		undoScope = SceneEditorSession.Active.UndoScope( undoName ).WithComponentChanges( components ).Push();

		property.DispatchPreEdited();
	}

	void PropertyChanged( SerializedProperty property, IEnumerable<Component> components )
	{
		ActivateSession();

		property.DispatchEdited();
	}

	void PropertyFinishEdit( SerializedProperty property, IEnumerable<Component> components )
	{
		ActivateSession();

		property.DispatchEdited();

		undoScope?.Dispose();
		undoScope = null;
	}

	private void ActivateSession()
	{
		var gameObject = SerializedObject.Targets.OfType<GameObject>().FirstOrDefault();
		ArgumentNullException.ThrowIfNull( gameObject );

		// you can lock the inspector to an object from a non-active scene session, so for now just make sure we're
		// making active the right scene when we start changing shit
		// TODO: we should really resolve undo etc from the currently pushed scene or something, so we can just push that scope wherever (?)

		var session = SceneEditorSession.Resolve( gameObject.Scene );
		if ( session is null || SceneEditorSession.Active == session )
			return;

		session.MakeActive();
	}

	void ContextMenu( Component component, Menu menu, string title )
	{
		if ( SerializedObject.IsMultipleTargets )
		{
			ContextMenuMultiple( component, menu, title );
			return;
		}

		var gameObject = SerializedObject.Targets.OfType<GameObject>().Single();
		var componentList = gameObject.Components;

		var editable = !component.Flags.Contains( ComponentFlags.NotEditable );

		if ( editable )
		{
			menu.AddOption( "Reset", "restart_alt", action: () =>
			{
				ActivateSession();
				using var scene = SceneEditorSession.Scope();

				using ( SceneEditorSession.Active.UndoScope( $"Reset {component.GetType().Name}" ).WithComponentChanges( component ).Push() )
				{
					component.Reset();
				}
			} );

			menu.AddSeparator();

			var componentIndex = componentList.GetAll().ToList().IndexOf( component );
			var componentId = component.Id;
			var canMoveUp = componentList.Count > 1 && componentIndex > 0;
			var canMoveDown = componentList.Count > 1 && componentIndex < componentList.Count - 1;

			menu.AddOption( "Move Up", "expand_less", action: () =>
			{
				ActivateSession();
				using var scene = SceneEditorSession.Scope();

				using ( SceneEditorSession.Active.UndoScope( "Change Component Order" ).WithGameObjectChanges( component.GameObject, GameObjectUndoFlags.Components ).Push() )
				{
					component.Components.Move( component, -1 );
				}

				Rebuild();
			} ).Enabled = canMoveUp;

			menu.AddOption( "Move Down", "expand_more", action: () =>
			{
				ActivateSession();
				using var scene = SceneEditorSession.Scope();

				using ( SceneEditorSession.Active.UndoScope( "Change Component Order" ).WithGameObjectChanges( component.GameObject, GameObjectUndoFlags.Components ).Push() )
				{
					component.Components.Move( component, +1 );
				}
				Rebuild();
			} ).Enabled = canMoveDown;

			menu.AddSeparator();

			menu.AddOption( $"Cut {title}", "content_cut", action: () =>
			{
				ActivateSession();
				using var scene = SceneEditorSession.Scope();

				using ( SceneEditorSession.Active.UndoScope( $"Cut {component.GetType().Name} Component" ).WithComponentDestructions( component ).Push() )
				{
					component.CopyToClipboard();
					component.Destroy();
				}
			} );
		}

		menu.AddOption( $"Copy {title}", "copy_all", action: () => component.CopyToClipboard() );

		if ( editable )
		{
			bool clipboardComponent = SceneEditor.HasComponentInClipboard();
			menu.AddOption( "Paste Values", "content_paste", action: () => component.PasteValues() ).Enabled = clipboardComponent;
			menu.AddOption( "Paste As New", "content_paste_go", action: () => component.GameObject.PasteComponent() ).Enabled = clipboardComponent;
		}

		menu.AddSeparator();

		if ( component.GameObject.IsPrefabInstance )
		{
			var isComponentModified = EditorUtility.Prefabs.IsComponentInstanceModified( component );

			var prefabName = EditorUtility.Prefabs.GetOuterMostPrefabName( component );

			var revertChangesActionName = "Revert Changes";
			menu.AddOption( revertChangesActionName, "history", action: () =>
			{
				ActivateSession();
				using var scene = SceneEditorSession.Scope();

				using ( SceneEditorSession.Active.UndoScope( revertChangesActionName ).WithComponentChanges( component ).Push() )
				{
					EditorUtility.Prefabs.RevertComponentInstanceChanges( component );
				}
			} ).Enabled = isComponentModified;


			menu.AddOption( "Apply to Prefab", "save", action: () =>
			{
				ActivateSession();
				using var scene = SceneEditorSession.Scope();

				EditorUtility.Prefabs.ApplyComponentInstanceChangesToPrefab( component );

			} ).Enabled = isComponentModified;

			menu.AddSeparator();
		}

		menu.AddOption( "Remove Component", "remove", action: () =>
		{
			ActivateSession();
			using var scene = SceneEditorSession.Scope();

			using ( SceneEditorSession.Active.UndoScope( $"Remove {component.GetType().Name} Component" ).WithComponentDestructions( component ).Push() )
			{
				component.Destroy();
			}
		} );

		var replace = menu.AddMenu( "Replace Component", "find_replace" );
		replace.AddWidget( new MenuComponentTypeSelectorWidget( replace )
		{
			OnSelect = ( t ) =>
			{
				ActivateSession();
				using var scene = SceneEditorSession.Scope();

				using ( SceneEditorSession.Active.UndoScope( $"Replace {component.GetType().Name} Component" ).WithComponentDestructions( component ).WithComponentCreations().Push() )
				{
					var go = component.GameObject;
					var jso = component.Serialize().AsObject();
					component.Destroy();
					var newComponent = go.Components.Create( t );
					newComponent.DeserializeImmediately( jso );
				}
			}
		} );

		menu.AddSeparator();

		var t = EditorTypeLibrary.GetType( component.GetType() );
		if ( t.SourceFile is not null )
		{
			bool isPackage = component.GetType().Assembly.IsPackage();
			var filename = System.IO.Path.GetFileName( t.SourceFile );
			menu.AddOption( $"Open {filename}", "code", action: () => CodeEditor.OpenFile( t ) ).Enabled = isPackage;
		}
	}

	void ContextMenuMultiple( Component component, Menu menu, string title )
	{
		var componentList = component.GameObject.Components;
		var index = componentList.GetAll( component.GetType(), FindMode.EverythingInSelf ).ToList().IndexOf( component );
		var components = new List<Component>();
		if ( index >= 0 )
		{
			foreach ( var obj in SerializedObject.Targets.OfType<GameObject>() )
			{
				var foundComponent = obj.Components.GetAll( component.GetType(), FindMode.EverythingInSelf ).ElementAtOrDefault( index );
				if ( foundComponent is not null )
				{
					components.Add( foundComponent );
				}
			}
		}
		else
		{
			components.Add( component );
		}

		if ( components.Count == 0 )
			return;

		var editable = !components.Any( x => x.Flags.Contains( ComponentFlags.NotEditable ) );

		if ( editable )
		{
			menu.AddOption( "Reset All", "restart_alt", action: () =>
			{
				ActivateSession();
				using var scene = SceneEditorSession.Scope();

				using ( SceneEditorSession.Active.UndoScope( $"Reset Component(s)" ).WithComponentChanges( components ).Push() )
				{
					foreach ( var c in components )
					{
						c.Reset();
					}
				}
			} );

			menu.AddSeparator();

			menu.AddOption( "Remove Components", "remove", action: () =>
			{
				ActivateSession();
				using var scene = SceneEditorSession.Scope();

				using ( SceneEditorSession.Active.UndoScope( $"Removed Component(s)" ).WithComponentDestructions( components ).Push() )
				{
					foreach ( var c in components )
					{
						c.Destroy();
					}
				}
			} );

			{
				var replace = menu.AddMenu( "Replace Components", "find_replace" );
				replace.AddWidget( new MenuComponentTypeSelectorWidget( replace )
				{
					OnSelect = ( t ) =>
					{
						ActivateSession();
						using var scene = SceneEditorSession.Scope();

						using ( SceneEditorSession.Active.UndoScope( $"Replace Component(s)" ).WithComponentDestructions( components ).WithComponentCreations().Push() )
						{
							foreach ( var c in components )
							{
								var go = c.GameObject;
								var jso = c.Serialize().AsObject();
								c.Destroy();
								var newComponent = go.Components.Create( t );
								newComponent.DeserializeImmediately( jso );
							}
						}
					}
				} );
			}

			if ( SceneEditor.HasComponentInClipboard() )
			{
				menu.AddOption( "Paste Values", "content_paste", action: () =>
				{
					ActivateSession();
					foreach ( var c in components )
					{
						c.PasteValues();
					}
				} );
				menu.AddOption( "Paste As New", "content_paste_go", action: () =>
				{
					ActivateSession();
					foreach ( var c in components )
					{
						c.GameObject.PasteComponent();
					}
				} );
			}
		}

		menu.AddSeparator();

		var t = EditorTypeLibrary.GetType( component.GetType() );
		if ( t.SourceFile is not null )
		{
			bool isPackage = component.GetType().Assembly.IsPackage();
			var filename = System.IO.Path.GetFileName( t.SourceFile );
			menu.AddOption( $"Open {filename}", "open_in_new", action: () => CodeEditor.OpenFile( t ) ).Enabled = isPackage;
		}
	}

	public void Frame()
	{
		if ( SetContentHash( BuildComponentHash, 0.2f ) )
		{
			Rebuild();
		}
	}

	int BuildComponentHash()
	{
		HashCode hc = default;

		hc.Add( hotloadCount );

		foreach ( var c in SerializedObject.Targets.OfType<GameObject>().SelectMany( x => x.Components.GetAll().Where( c => c.IsValid() && !c.Flags.HasFlag( ComponentFlags.Hidden ) ) ) )
		{
			hc.Add( c );
		}

		return hc.ToHashCode();
	}
}

file class MenuComponentTypeSelectorWidget : ComponentTypeSelectorWidget
{
	private bool Closed = false;

	public MenuComponentTypeSelectorWidget( Widget parent ) : base( parent )
	{
		OnDestroy += () => Closed = true;
	}

	protected override void OnVisibilityChanged( bool visible )
	{
		base.OnVisibilityChanged( visible );

		if ( visible )
		{
			Search.Focus();
		}
	}

	// Stops the context menu from closing!!
	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );

		e.Accepted = !Closed;
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		base.OnKeyPress( e );

		e.Accepted = !Closed;
	}
}
