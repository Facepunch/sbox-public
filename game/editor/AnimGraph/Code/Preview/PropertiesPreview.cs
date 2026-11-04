namespace Editor.AnimGraph;

/// <summary>
/// Independent clip or fixed-pose playback in the properties dock, using the same viewport
/// and display controls as the full-page previews.
/// </summary>
internal sealed class PropertiesPreview : PreviewSceneWidget
{
	private readonly ClipPreview _clip = new();
	private string _animation;
	private Vector2 _lastSize;

	public Nodes.AnimationPoseNode Pose { get; set; }
	public Data Graph { get; set; }

	public PropertiesPreview( Widget parent, Data graph ) : base( parent, "Properties Preview" )
	{
		Graph = graph;
		Toolbar.Add( new PreviewOptions( this, "properties", PreviewOption.All & ~PreviewOption.RootMotion ).CreateBar( this ), 1 );
	}

	public void SetAnimation( string animation )
	{
		_animation = animation;
		Pose = null;
	}

	protected override void OnScenePreFrame()
	{
		SetModel( Graph?.TargetModel );

		if ( _lastSize != Size )
		{
			_lastSize = Size;
			FrameCamera();
		}

		_clip.Looping = Pose is null;
		_clip.Play( Renderer, _animation );

		if ( Pose is { } pose )
		{
			var time = pose.SelectedData.ResolveFixedTime( pose.FixedTime );
			if ( pose.UseFramesAsInput )
				_clip.Time = time / _clip.FrameRate;
			else
				_clip.TimeNormalized = time.Clamp( 0f, 1f );
		}
		else
		{
			_clip.Advance( RealTime.Delta );
		}
	}

	public override void OnDestroyed()
	{
		_clip.Dispose();
		base.OnDestroyed();
	}
}
