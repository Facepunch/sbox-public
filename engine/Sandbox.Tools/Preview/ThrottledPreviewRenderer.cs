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
	private readonly PreviewRenderSettings _settings;

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
	public bool TryRender( SceneCamera camera, Vector2 displaySize )
	{
		if ( _isDisposed ) return false;
		if ( camera?.World == null ) return false;
		if ( displaySize.x <= 1 || displaySize.y <= 1 ) return false;

		// Frame budget check - skip if frame is already over budget
		if ( Time.Delta * 1000 > _settings.FrameBudgetMs )
			return false;

		// Focus + throttle check
		if ( !ShouldRender( camera ) )
			return false;

		_lastRender = 0;

		// Calculate render resolution (may be smaller than display for GPU savings)
		var (renderWidth, renderHeight) = CalculateRenderSize( displaySize );

		// Ensure back pixmap exists at display size
		EnsureBackPixmap( displaySize );

		// Ensure staging bitmap exists at render size
		EnsureStagingBitmap( renderWidth, renderHeight );

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

	private bool ShouldRender( SceneCamera camera )
	{
		float interval;

		if ( !Sandbox.Application.IsFocused )
		{
			if ( _settings.SkipWhenUnfocused )
				return false;

			interval = _settings.GetInterval( _settings.UnfocusedFps );
		}
		else
		{
			// Adaptive throttling: faster when camera is moving, slower when idle
			bool hasChanges = HasCameraChanged( camera );
			float fps = hasChanges ? _settings.ActiveFps : _settings.IdleFps;
			interval = _settings.GetInterval( fps );
		}

		return _lastRender >= interval;
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

		// Maintain aspect ratio
		float targetAspect = targetSize.x / targetSize.y;
		float renderAspect = (float)renderWidth / renderHeight;

		if ( renderAspect > targetAspect )
			renderWidth = (int)(renderHeight * targetAspect);
		else
			renderHeight = (int)(renderWidth / targetAspect);

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
			// Single buffer mode - just use back as front
			// Pixmap uses finalizer for cleanup, old front will be GC'd
			lock ( _swapLock )
			{
				_frontPixmap = _backPixmap;
				_backPixmap = null;
			}
		}
		else
		{
			// Double buffer - swap pointers
			lock ( _swapLock )
			{
				(_frontPixmap, _backPixmap) = (_backPixmap, _frontPixmap);
			}
		}
	}

	public void Dispose()
	{
		if ( _isDisposed ) return;
		_isDisposed = true;

		// Pixmap uses finalizer for cleanup, just null the references
		_frontPixmap = null;
		_backPixmap = null;

		// Bitmap implements IDisposable
		_stagingBitmap?.Dispose();
		_stagingBitmap = null;
	}
}
