using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Records 2D drawing commands. Obtained from Painter.Begin() or a panel's OnDraw callback.
/// Copies share drawing state. Dispose a destination-owned painter to submit; panel-supplied painters remain owned by the panel.
/// </summary>
public readonly ref partial struct Painter
{
	readonly Painter.Context _context;
	readonly long _recording;
	readonly bool _ownsContext;

	internal Painter( Painter.Context context, bool ownsContext = false )
	{
		_context = context;
		_recording = context.Recording;
		_ownsContext = ownsContext;
	}

	internal Painter.Context ActiveContext
	{
		get
		{
			var context = GetActiveContext();
			if ( !context.HasState ) context.InitializeState();
			return context;
		}
	}

	Painter.Context GetActiveContext()
	{
		if ( _context is null || !_context.IsActive || _context.Recording != _recording )
			throw new ObjectDisposedException( nameof( Painter ), "The paint context has ended." );
		return _context;
	}

	/// <summary>
	/// Submits a destination-owned recording. Copies may be disposed more than once. Panel-supplied painters do not own their recording.
	/// </summary>
	public void Dispose()
	{
		if ( _ownsContext && _context is not null && _context.Recording == _recording ) _context.End();
	}

	/// <summary>
	/// Clears the destination, ignoring drawing state. Supported by texture and command-list painters, outside layers.
	/// </summary>
	public void Clear( Color color )
	{
		var context = GetActiveContext();
		if ( !context.CanClear )
			throw new InvalidOperationException( "Clear requires a texture or command-list painter outside a layer." );
		context.Batcher.Clear( color );
	}

	/// <summary>
	/// The destination bounds in drawing coordinates. Panel painters start at (0, 0).
	/// </summary>
	public Rect Bounds => GetActiveContext().Bounds;

	/// <summary>
	/// Starts painting a render-target texture. Dispose the painter to submit its drawing.
	/// Existing pixels are preserved unless Clear is called.
	/// </summary>
	public static Painter Begin( Texture texture )
	{
		ArgumentNullException.ThrowIfNull( texture );
		ThreadSafe.AssertIsMainThread();
		return new TexturePaintContext( texture ).Begin();
	}

	/// <summary>
	/// Records drawing into a command list using the screen bounds. Dispose the painter to append its drawing.
	/// </summary>
	public static Painter Begin( CommandList commandList )
	{
		return Begin( commandList, new Rect( Vector2.Zero, Screen.Size ) );
	}

	/// <summary>
	/// Records drawing into a command list. Bounds are in pixels and do not scale the drawing.
	/// Starting another recording discards unfinished drawing. Dispose before recording other commands.
	/// </summary>
	public static Painter Begin( CommandList commandList, Rect bounds )
	{
		return Painter.Context.Get( commandList ).Begin( bounds );
	}

	internal static Painter BeginLegacy( CommandList commandList )
	{
		return Painter.Context.Get( commandList ).Begin( new Rect( Vector2.Zero, Screen.Size ), legacy: true );
	}
}
