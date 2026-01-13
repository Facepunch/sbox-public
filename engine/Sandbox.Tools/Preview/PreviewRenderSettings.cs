namespace Editor.Preview;

/// <summary>
/// Configuration for throttled preview rendering.
/// </summary>
public readonly struct PreviewRenderSettings
{
	/// <summary>FPS when actively changing</summary>
	public float ActiveFps { get; init; }

	/// <summary>FPS when idle</summary>
	public float IdleFps { get; init; }

	/// <summary>FPS when unfocused</summary>
	public float UnfocusedFps { get; init; }

	/// <summary>Maximum render width</summary>
	public int MaxRenderWidth { get; init; }

	/// <summary>Maximum render height</summary>
	public int MaxRenderHeight { get; init; }

	/// <summary>Frame budget in ms</summary>
	public float FrameBudgetMs { get; init; }

	/// <summary>Skip when unfocused</summary>
	public bool SkipWhenUnfocused { get; init; }

	/// <summary>Use async GPU readback (non-blocking)</summary>
	public bool UseAsyncReadback { get; init; }

	/// <summary>Use double buffering for smoother display</summary>
	public bool UseDoubleBuffering { get; init; }

	// Lightweight Render Options (reduces GPU cost dramatically)

	/// <summary>Enable post-processing effects</summary>
	public bool EnablePostProcessing { get; init; }

	/// <summary>Enable shadow rendering</summary>
	public bool EnableShadows { get; init; }

	/// <summary>Enable indirect/global illumination</summary>
	public bool EnableIndirectLighting { get; init; }

	/// <summary>Enable anti-aliasing</summary>
	public bool EnableAntiAliasing { get; init; }

	/// <summary>Default settings - lightweight for good performance</summary>
	public static PreviewRenderSettings Default => new()
	{
		ActiveFps = 10f,
		IdleFps = 2f,
		UnfocusedFps = 0.5f,
		MaxRenderWidth = 640,
		MaxRenderHeight = 360,
		FrameBudgetMs = 16f,
		SkipWhenUnfocused = false,
		UseAsyncReadback = true,
		UseDoubleBuffering = true,
		EnablePostProcessing = false,
		EnableShadows = false,
		EnableIndirectLighting = false,
		EnableAntiAliasing = false
	};

	/// <summary>High quality - full render features</summary>
	public static PreviewRenderSettings HighQuality => new()
	{
		ActiveFps = 30f,
		IdleFps = 10f,
		UnfocusedFps = 2f,
		MaxRenderWidth = 1280,
		MaxRenderHeight = 720,
		FrameBudgetMs = 32f,
		SkipWhenUnfocused = false,
		UseAsyncReadback = true,
		UseDoubleBuffering = true,
		EnablePostProcessing = true,
		EnableShadows = true,
		EnableIndirectLighting = true,
		EnableAntiAliasing = true
	};

	/// <summary>Minimal (thumbnails) - lowest resource usage</summary>
	public static PreviewRenderSettings Minimal => new()
	{
		ActiveFps = 5f,
		IdleFps = 0f,
		UnfocusedFps = 0f,
		MaxRenderWidth = 320,
		MaxRenderHeight = 180,
		FrameBudgetMs = 8f,
		SkipWhenUnfocused = true,
		UseAsyncReadback = false,
		UseDoubleBuffering = false,
		EnablePostProcessing = false,
		EnableShadows = false,
		EnableIndirectLighting = false,
		EnableAntiAliasing = false
	};

	/// <summary>Sync mode (legacy compatibility)</summary>
	public static PreviewRenderSettings Synchronous => new()
	{
		ActiveFps = 10f,
		IdleFps = 2f,
		UnfocusedFps = 0.5f,
		MaxRenderWidth = 640,
		MaxRenderHeight = 360,
		FrameBudgetMs = 16f,
		SkipWhenUnfocused = false,
		UseAsyncReadback = false,
		UseDoubleBuffering = false,
		// Lightweight
		EnablePostProcessing = false,
		EnableShadows = false,
		EnableIndirectLighting = false,
		EnableAntiAliasing = false
	};

	internal float GetInterval( float fps ) => fps > 0 ? 1f / fps : float.MaxValue;
}
