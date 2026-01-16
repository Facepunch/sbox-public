namespace Editor.MeshEditor;

public abstract class PrimitiveEditor
{
	private readonly TypeDescription _type;

	protected PrimitiveTool Tool { get; private init; }

	public string Title => _type.Title;
	public string Icon => _type.Icon;

	public virtual bool CanBuild => false;
	public virtual bool InProgress => false;

	protected PrimitiveEditor( PrimitiveTool tool )
	{
		Tool = tool;
		_type = EditorTypeLibrary.GetType( GetType() );
	}

	public abstract void OnUpdate( SceneTrace trace );
	public abstract void OnCancel();
	public abstract PolygonMesh Build();

	public virtual void OnCreated( MeshComponent component )
	{
	}

	public virtual Widget CreateWidget() => null;
}
