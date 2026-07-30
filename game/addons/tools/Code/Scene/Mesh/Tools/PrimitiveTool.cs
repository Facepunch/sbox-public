namespace Editor.MeshEditor;

/// <summary>
/// Create different types of primitive meshes.
/// </summary>
[Title( "Primitive Tool" )]
[Icon( "meshtools/primitve_tools/create.png" )]
[Alias( "tools.primitive-tool" )]
public partial class PrimitiveTool( MeshTool tool ) : EditorTool
{
	const string EditorTypeCookie = "PrimitiveTool.EditorType";

	public MeshTool MeshTool { get; private init; } = tool;

	public PrimitiveEditor Editor { get; private set; }

	public Material ActiveMaterial => MeshTool.ActiveMaterial;

	public override void OnEnabled()
	{
		var savedType = EditorCookie.GetString( EditorTypeCookie, null );
		var type = EditorTypeLibrary.GetTypes<PrimitiveEditor>()
			.FirstOrDefault( x => !x.IsAbstract && x.FullName == savedType )
			?.TargetType ?? typeof( BlockEditor );

		SelectEditor( type );
	}

	public override void OnDisabled()
	{
		if ( Game.IsPlaying )
			Cancel();
		else
			Create();

		Editor = null;
	}

	internal void SelectEditor( Type type )
	{
		if ( Editor?.GetType() == type )
			return;

		Editor = EditorTypeLibrary.Create<PrimitiveEditor>( type, [this] );

		if ( Editor is not null )
			EditorCookie.SetString( EditorTypeCookie, type.FullName );
	}

	public void Create()
	{
		if ( Editor is null ) return;
		if ( !Editor.CanBuild )
		{
			Cancel();
			return;
		}

		var mesh = Editor.Build();
		if ( mesh is null ) return;

		var name = Editor.Title;

		using var scope = SceneEditorSession.Scope();
		using ( SceneEditorSession.Active.UndoScope( $"Create {name}" )
			.WithGameObjectCreations()
			.Push() )
		{
			var bounds = mesh.CalculateBounds();
			mesh.ApplyTransform( new Transform( -bounds.Center ) );

			var go = new GameObject( true, name );
			go.WorldPosition = bounds.Center;
			var c = go.Components.Create<MeshComponent>( false );
			c.Mesh = mesh;
			c.SmoothingAngle = 40.0f;

			Editor.OnCreated( c );

			c.Enabled = true;
		}
	}

	public override void OnUpdate()
	{
		Editor?.OnUpdate( MeshTrace );
	}

	public void Cancel()
	{
		Editor?.OnCancel();
	}
}
