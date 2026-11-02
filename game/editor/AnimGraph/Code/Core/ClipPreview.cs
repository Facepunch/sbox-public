using System;

namespace Editor.AnimGraph;

/// <summary>
/// Plays a single animation clip on a preview model.
///
/// A clip is its own resource rather than something baked into a model, so a model renderer
/// can't play one on its own - the animation graph runtime is what samples clips. This wraps a
/// throwaway one clip graph whose time is driven from here, which gives previews the scrubbing
/// and frame stepping they used to get from model sequences.
/// </summary>
public sealed class ClipPreview : IDisposable
{
	private const string TimeParameter = "__clip_preview_time";

	private Graph _graph;
	private SkinnedModelRenderer _renderer;
	private float _time;

	/// <summary>
	/// Resource path of the clip being played, if any.
	/// </summary>
	public string ClipName { get; private set; }

	/// <summary>
	/// The clip resource, so previews can read its duration and frame rate rather than assuming.
	/// </summary>
	public Clip Clip { get; private set; }

	/// <summary>
	/// Whether a clip is bound and posing the renderer.
	/// </summary>
	public bool HasClip => _graph is not null;

	/// <summary>
	/// Length of the clip in seconds. Zero when nothing is playing.
	/// </summary>
	public float Duration => Clip?.Duration ?? 0f;

	/// <summary>
	/// The clip's frame rate, falling back to 30 for clips that don't have one.
	/// </summary>
	public float FrameRate => Clip is { FrameRate: > 0f } clip ? clip.FrameRate : 30f;

	/// <summary>
	/// How many frames of pose data the clip holds.
	/// </summary>
	public int FrameCount => Clip?.FrameCount ?? 0;

	/// <summary>
	/// Whether playback wraps around at the end of the clip.
	/// </summary>
	public bool Looping { get; set; } = true;

	/// <summary>
	/// Speed multiplier applied while advancing.
	/// </summary>
	public float PlaybackRate { get; set; } = 1f;

	/// <summary>
	/// Whether a non looping clip has run to its end.
	/// </summary>
	public bool IsFinished => !Looping && _time >= 1f;

	/// <summary>
	/// Position through the clip, from 0 to 1.
	/// </summary>
	public float TimeNormalized
	{
		get => _time;
		set
		{
			_time = Looping ? value - MathF.Floor( value ) : value.Clamp( 0f, 1f );
			Apply();
		}
	}

	/// <summary>
	/// Position through the clip, in seconds.
	/// </summary>
	public float Time
	{
		get => _time * Duration;
		set => TimeNormalized = Duration > 0f ? value / Duration : 0f;
	}

	/// <summary>
	/// Play a clip on a renderer. Passing no clip, or one that won't load, leaves the renderer in
	/// its reference pose - the model's legacy animgraph is never played here.
	/// </summary>
	/// <param name="renderer">The model to pose.</param>
	/// <param name="clipName">Resource path of the .anim to play.</param>
	public void Play( SkinnedModelRenderer renderer, string clipName )
	{
		// Nothing to do when we're already showing this, but a clip that failed to bind is
		// retried in case it has compiled since
		if ( _renderer == renderer && ClipName == clipName && ( _graph is not null || string.IsNullOrWhiteSpace( clipName ) ) )
			return;

		Detach();

		_renderer = renderer;
		ClipName = clipName;
		_time = 0f;

		if ( !renderer.IsValid() || string.IsNullOrWhiteSpace( clipName ) )
			return;

		Clip = Sandbox.Animation.Clip.Load( clipName );
		if ( Clip is null )
			return;

		var builder = Graph.Builder;

		// A clip only plays on the skeleton it was compiled against, and it knows which that is
		builder.Skeleton = Clip.SkeletonName;

		var time = builder.FloatParameter( TimeParameter, 0f, 1f );
		_graph = builder.Create( builder.TimeControlledClip( clipName, time ), "animclip_preview" );

		if ( _graph is null )
		{
			Clip = null;
			return;
		}

		renderer.Sequence.Name = "";
		renderer.UseAnimGraph = true;
		renderer.Animation.Graph = _graph;

		Apply();
	}

	/// <summary>
	/// Move playback on by a time step, wrapping or stopping at the end depending on
	/// <see cref="Looping"/>.
	/// </summary>
	public void Advance( float delta )
	{
		if ( _graph is null || Duration <= 0f )
			return;

		TimeNormalized = _time + delta * PlaybackRate / Duration;
	}

	/// <summary>
	/// Stop previewing. The renderer is left in its reference pose rather than handed back to
	/// the model's legacy animgraph.
	/// </summary>
	public void Clear()
	{
		Detach();

		_renderer = null;
		ClipName = null;
	}

	private void Detach()
	{
		if ( _renderer.IsValid() )
		{
			_renderer.Animation.Graph = null;
			_renderer.UseAnimGraph = false;
		}

		// The graph is procedural, so nothing else can be holding it
		_graph?.Dispose();
		_graph = null;
		Clip = null;
	}

	private void Apply()
	{
		if ( _graph is null || !_renderer.IsValid() )
			return;

		_renderer.Animation.Parameters.Set( TimeParameter, _time, persist: true );
	}

	public void Dispose() => Clear();
}
