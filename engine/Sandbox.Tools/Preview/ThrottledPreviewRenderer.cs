using System;
using Sandbox;

namespace Editor.Preview;

/// <summary>
/// High-performance preview renderer with:
/// - Adaptive throttling (active/idle/unfocused FPS)
/// - Dirty tracking (camera position/rotation/fov)
/// - Resolution optimization (capped render size)
/// - Bitmap caching (zero per-frame allocations)
/// - Double buffering (smooth display)
/// </summary>
public sealed class ThrottledPreviewRenderer : IDisposable
{
	private PreviewRenderSettings _settings;

	// Double-buffered pixmaps
	private Pixmap _frontPixmap;
	private Pixmap _backPixmap;
	private readonly object _swapLock = new();

	// Cached bitmap for rendering
	private Bitmap _stagingBitmap;

	// State tracking
	private RealTimeSince _lastRender;
	private Vector3 _lastPosition;
	private Rotation _lastRotation;
	private float _lastFov;
	private bool _isDirty = true;
	private bool _isDisposed;

	public ThrottledPreviewRenderer( PreviewRenderSettings? settings = null )
	{
		_settings = settings ?? PreviewRenderSettings.Default;
	}

	/// <summary>
	/// Force next frame to render regardless of throttling.
	/// Call when external changes occur (scene load, etc.)
	/// </summary>
	public void MarkDirty() => _isDirty = true;

	/// <summary>
	/// Update settings without full recreation. Only recreates resources if needed.
	/// Zero-alloc for FPS/quality changes, minimal alloc for resolution changes.
	/// </summary>
	public void UpdateSettings( PreviewRenderSettings newSettings )
	{
		bool resolutionChanged =
			newSettings.MaxRenderWidth != _settings.MaxRenderWidth ||
			newSettings.MaxRenderHeight != _settings.MaxRenderHeight;

		_settings = newSettings;

		if ( resolutionChanged )
		{
			// Only recreate if resolution caps changed
			_stagingBitmap?.Dispose();
			_stagingBitmap = null;
		}

		_isDirty = true; // Force re-render with new settings
	}

	/// <summary>
	/// Get pixmap for display (thread-safe)
	/// </summary>
	public Pixmap Pixmap
	{
		get
		{
			lock ( _swapLock )
			{
				return _frontPixmap;
			}
		}
	}

	/// <summary>
	/// Attempt to render. Returns true if new frame is ready for display.
	/// Call this every frame - throttling is handled internally.
	/// </summary>
	/// <param name="camera">The scene camera to render</param>
	/// <param name="displaySize">Display size in pixels</param>
	/// <param name="isWidgetActive">True if widget's window is active (IsActiveWindow)</param>
	public bool TryRender( SceneCamera camera, Vector2 displaySize, bool isWidgetActive = true )
	{
		if ( _isDisposed ) return false;
		if ( camera?.World == null ) return false;
		if ( displaySize.x <= 1 || displaySize.y <= 1 ) return false;

		if ( !ShouldRender( camera, isWidgetActive ) )
			return false;

		_lastRender = 0;

		// Calculate render resolution (may be smaller than display for GPU savings)
		var (renderWidth, renderHeight) = CalculateRenderSize( displaySize );

		// Ensure back pixmap exists at display size
		EnsureBackPixmap( displaySize );

		// Ensure staging bitmap exists at render size
		EnsureStagingBitmap( renderWidth, renderHeight );

		// Apply lightweight settings to reduce GPU cost
		ApplyLightweightSettings( camera );

		// Render (blocking, but throttled so only happens 2-10 times per second)
		camera.OnPreRender( new Vector2( renderWidth, renderHeight ) );
		camera.RenderToBitmap( _stagingBitmap );

		// Copy to back pixmap (Qt handles upscaling if sizes differ)
		_backPixmap.UpdateFromPixels( _stagingBitmap );

		// Swap buffers
		SwapBuffers();

		// Update cached state for dirty tracking
		UpdateCachedState( camera );

		return true;
	}

	private bool ShouldRender( SceneCamera camera, bool isWidgetActive )
	{
		// Force first frame immediately - no pixmap means never rendered
		if ( _frontPixmap is null )
			return true;

		float fps;

		if ( !isWidgetActive )
		{
			// Editor window not focused - use low FPS or skip entirely
			if ( _settings.SkipWhenUnfocused )
				return false;
			fps = _settings.UnfocusedFps;
		}
		else
		{
			// Editor window active - use ActiveFps if camera moving, IdleFps otherwise
			bool hasChanges = HasCameraChanged( camera );
			fps = hasChanges ? _settings.ActiveFps : _settings.IdleFps;
		}

		float interval = _settings.GetInterval( fps );
		return _lastRender >= interval;
	}

	/// <summary>
	/// Apply render settings to camera based on current preset.
	/// </summary>
	private void ApplyLightweightSettings( SceneCamera camera )
	{
		camera.EnablePostProcessing = _settings.EnablePostProcessing;
		camera.AntiAliasing = _settings.EnableAntiAliasing;
		camera.Attributes.Set( "drawShadows", _settings.EnableShadows );
		camera.Attributes.Set( "indirectLighting", _settings.EnableIndirectLighting );
	}

	private bool HasCameraChanged( SceneCamera camera )
	{
		if ( _isDirty ) return true;

		const float posEpsilon = 0.001f;
		const float rotEpsilon = 0.1f;
		const float fovEpsilon = 0.1f;

		bool posChanged = Vector3.DistanceBetween( camera.Position, _lastPosition ) > posEpsilon;
		bool rotChanged = Rotation.Difference( camera.Rotation, _lastRotation ).Angle() > rotEpsilon;
		bool fovChanged = MathF.Abs( camera.FieldOfView - _lastFov ) > fovEpsilon;

		return posChanged || rotChanged || fovChanged;
	}

	private void UpdateCachedState( SceneCamera camera )
	{
		_lastPosition = camera.Position;
		_lastRotation = camera.Rotation;
		_lastFov = camera.FieldOfView;
		_isDirty = false;
	}

	private (int width, int height) CalculateRenderSize( Vector2 targetSize )
	{
		int targetWidth = (int)targetSize.x;
		int targetHeight = (int)targetSize.y;

		// Cap at max render size for GPU savings
		int renderWidth = Math.Min( targetWidth, _settings.MaxRenderWidth );
		int renderHeight = Math.Min( targetHeight, _settings.MaxRenderHeight );

		// Maintain aspect ratio using float math and rounding to avoid truncation issues
		float targetAspect = targetSize.x / targetSize.y;
		float renderAspect = (float)renderWidth / renderHeight;

		if ( renderAspect > targetAspect )
		{
			// Too wide: adjust width
			float adjustedWidth = renderHeight * targetAspect;
			if ( adjustedWidth < 1f ) adjustedWidth = 1f;
			renderWidth = (int)MathF.Round( adjustedWidth );
		}
		else
		{
			// Too tall: adjust height
			float adjustedHeight = renderWidth / targetAspect;
			if ( adjustedHeight < 1f ) adjustedHeight = 1f;
			renderHeight = (int)MathF.Round( adjustedHeight );
		}

		// Ensure minimum size
		return (Math.Max( 64, renderWidth ), Math.Max( 36, renderHeight ));
	}

	private void EnsureBackPixmap( Vector2 size )
	{
		if ( _backPixmap is null || _backPixmap.Size != size )
		{
			// Pixmap uses finalizer for cleanup, no IDisposable
			_backPixmap = new Pixmap( size );
			_isDirty = true;
		}
	}

	private void EnsureStagingBitmap( int width, int height )
	{
		if ( _stagingBitmap is null ||
			 _stagingBitmap.Width != width ||
			 _stagingBitmap.Height != height )
		{
			_stagingBitmap?.Dispose();
			_stagingBitmap = new Bitmap( width, height );
			_isDirty = true;
		}
	}

	private void SwapBuffers()
	{
		if ( !_settings.UseDoubleBuffering )
		{
			// Single buffer mode - front and back point to same pixmap
			// Don't null _backPixmap - reuse it next frame to avoid per-frame allocations
			lock ( _swapLock )
			{
				_frontPixmap = _backPixmap;
			}
		}
		else
		{
			// Double buffer mode
			lock ( _swapLock )
			{
				// First render: promote back to front and create new back buffer
				// to avoid wasting the initial allocation
				if ( _frontPixmap is null && _backPixmap is not null )
				{
					_frontPixmap = _backPixmap;
					_backPixmap = new Pixmap( _frontPixmap.Size );
				}
				else
				{
					(_frontPixmap, _backPixmap) = (_backPixmap, _frontPixmap);
				}
			}
		}
	}

	public void Dispose()
	{
		if ( _isDisposed ) return;
		_isDisposed = true;

		// Lock to prevent race with Pixmap getter
		lock ( _swapLock )
		{
			_frontPixmap = null;
			_backPixmap = null;
		}

		// Bitmap implements IDisposable
		_stagingBitmap?.Dispose();
		_stagingBitmap = null;
	}
}
