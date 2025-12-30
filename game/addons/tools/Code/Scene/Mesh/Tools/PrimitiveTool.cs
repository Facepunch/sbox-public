namespace Editor.MeshEditor;

/// <summary>
/// Create different types of primitive meshes.
/// </summary>
[Title( "Primitive Tool" )]
[Icon( "view_in_ar" )]
[Alias( "tools.primitive-tool" )]
public partial class PrimitiveTool( MeshTool tool ) : EditorTool
{
	public MeshTool MeshTool { get; private init; } = tool;

	public PrimitiveEditor Editor { get; private set; }

	public Material ActiveMaterial => MeshTool.ActiveMaterial;

	public override void OnEnabled()
	{
		Editor = EditorTypeLibrary.Create<PrimitiveEditor>( typeof( BlockEditor ), [this] );
	}

	public override void OnDisabled()
	{
		Create();

		Editor = null;
	}

	public void Create()
	{
		if ( Editor is null ) return;
		if ( !Editor.CanBuild ) return;

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
