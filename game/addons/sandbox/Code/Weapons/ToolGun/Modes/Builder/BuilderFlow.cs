internal abstract class BuilderFlow
{
	protected BuilderTool Tool { get; }

	protected BuilderFlow( BuilderTool tool )
	{
		Tool = tool;
	}

	public bool HasBox { get; protected set; }

	public bool IsValid { get; protected set; }

	public Transform Transform { get; protected set; }
	public BuilderShapeKey Key { get; protected set; }
	public Vector3 Size { get; protected set; }
	public string Dimensions { get; protected set; }

	public virtual bool StretchesPreview => true;

	public virtual bool AbsorbsMouse => false;

	public virtual bool SupportsSymmetry => false;

	public abstract void Begin( in BuilderPlane plane, Vector2 start );

	public abstract void Update( Ray eyes, int grid );

	public abstract bool Advance();

	public abstract bool Back();

	public virtual void Reload( in BuilderPlane hover, bool drawing ) { }

	/// <summary>The scroll wheel while drawing: one step per notch, positive scrolling up.</summary>
	public virtual void Scroll( int steps ) { }

	public virtual string StartHint => "#tool.hint.builder.start";

	public abstract string PrimaryHint { get; }
	public abstract string SecondaryHint { get; }
	public virtual string GetReloadHint( bool drawing ) => null;
	public virtual string HudHint => null;

	public virtual bool TryGetFallbackPreview( out Model model, out Vector3 size )
	{
		model = null;
		size = default;
		return false;
	}

	protected void Clear()
	{
		HasBox = false;
		IsValid = false;
	}
}
