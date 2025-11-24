using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.UI;

namespace Editor.Inspectors;

[Inspector( typeof( GameObject ) )]
[Inspector( typeof( PrefabScene ) )]
public class GameObjectInspector : InspectorWidget
{
	ComponentListWidget componentList;

	public GameObjectInspector( SerializedObject so ) : base( so )
	{
		SerializedObject.OnPropertyStartEdit += PropertyStartEdit;
		SerializedObject.OnPropertyChanged += PropertyChanged;
		SerializedObject.OnPropertyFinishEdit += PropertyFinishEdit;

		Layout = Layout.Column();
		RebuildUI();
	}

	private void RebuildUI()
	{
		Layout.Clear( true );

		var h = new GameObjectHeader( this, SerializedObject );
		Layout.Add( h );

		BuildComponentList();
	}

	IDisposable undoScope;

	void PropertyStartEdit( SerializedProperty property )
	{
		ActivateSession();

		var propertyDisplayName = property.Parent.ParentProperty is null
			? property.Name
			: $"{property.Parent.ParentProperty.Name}.{property.Name}";
		var undoName = $"Edit {propertyDisplayName} on {SerializedObject.GetProperty( nameof( GameObject.Name ) ).GetValue<string>()}";

		var gameObjects = SerializedObject.Targets.OfType<GameObject>();
		undoScope = SceneEditorSession.Active.UndoScope( undoName ).WithGameObjectChanges( gameObjects, GameObjectUndoFlags.Properties | GameObjectUndoFlags.Components ).Push();

		property.DispatchPreEdited();
	}

	void PropertyChanged( SerializedProperty property )
	{
		ActivateSession();

		property.DispatchEdited();
	}

	void PropertyFinishEdit( SerializedProperty property )
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

	void BuildComponentList()
	{
		var scroller = Layout.Add( new ScrollArea( this ) );
		scroller.Canvas = new Widget( scroller );
		scroller.Canvas.HorizontalSizeMode = SizeMode.Flexible;
		scroller.Canvas.Layout = Layout.Column();

		//
		// GameObject Transform component
		//
		if ( SerializedObject.GetProperty( nameof( GameObject.Transform ) ).TryGetAsObject( out var transform ) )
		{
			var transformWidget = new ComponentSheet( Guid.Empty, transform );
			transformWidget.Header.Title = "Transform";
			transformWidget.Header.Color = InspectorHeader.HeaderColor;
			transformWidget.Header.IsDraggable = false;
			scroller.Canvas.Layout.Add( transformWidget );
		}

		componentList = new ComponentListWidget( SerializedObject );

		scroller.Canvas.Layout.Add( componentList );

		// Add component button
		var row = scroller.Canvas.Layout.AddRow();
		row.AddStretchCell();
		row.Margin = 16;
		var button = row.Add( new Button.Primary( "Add Component", "add" ) );
		button.MinimumWidth = 300;
		button.Clicked = () => AddComponentDialog( button );
		row.AddStretchCell();


		scroller.Canvas.Layout.AddStretchCell( 1 );
	}

	/// <summary>
	/// Pop up a window to add a component to this entity
	/// </summary>
	public void AddComponentDialog( Button source )
	{
		var s = new ComponentTypeSelector( this );
		s.OnSelect += ( t ) => AddComponent( t );
		s.OpenAt( source.ScreenRect.BottomLeft, animateOffset: new Vector2( 0, -4 ) );
		s.FixedWidth = source.Width;
	}

	private void AddComponent( TypeDescription componentType )
	{
		ActivateSession();

		var createdComponents = new List<Component>();

		using var sceneScope = SceneEditorSession.Scope();

		foreach ( var go in SerializedObject.Targets.OfType<GameObject>() )
		{
			using ( SceneEditorSession.Active.UndoScope( "Add Component(s)" ).WithComponentCreations().Push() )
			{
				var component = go.Components.Create( componentType );
				createdComponents.Add( component );
			}
		}
	}

	private void PasteComponent()
	{
		ActivateSession();

		using var sceneScope = SceneEditorSession.Scope();

		foreach ( var go in SerializedObject.Targets.OfType<GameObject>() )
		{
			go.PasteComponent();
		}
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		if ( SceneEditor.HasComponentInClipboard() )
		{
			var menu = new ContextMenu( this );
			menu.AddOption( "Paste Component As New", action: PasteComponent );
			menu.OpenAtCursor( false );
			e.Accepted = true;
			return;
		}

		base.OnContextMenu( e );
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		componentList?.Frame();
	}
}
