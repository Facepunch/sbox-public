using Basalt.Gpu;

namespace Basalt.Backend;

/// <summary>
/// A debug figure of the visibility-driven forward pass (<c>basaltlab -capture-visibility</c>): while it's set as
/// <see cref="BackendDevice.Figure"/>, each GPU-driven forward pass of a frame also replays its multi-draws with
/// <c>basalt_gpu_driven_figure</c> into its targets: a count per fragment rasterised, a flat colour per triangle over the
/// view's depth with its edges, or every triangle's edges with no depth test. It keeps where each pass's commands are,
/// for <see cref="Counts"/> once the frame is done.
/// </summary>
public sealed class VisibilityFigure : IDisposable
{
	readonly Device gpu;

	/// <summary>A count per pixel (R16 float, additive), a colour per pixel (RGBA8).</summary>
	public TextureHandle Count, Colour;

	/// <summary>Draw the triangles flat over the view's depth with dark edges; otherwise every triangle's faint edges, no depth test.</summary>
	public bool DepthTestedTriangles;

	internal bool Cleared;

	/// <summary>The fragments of the view whose ids <see cref="BackendDevice.FigureVisibility"/> holds this frame.</summary>
	internal int VisibilityFragments;

	/// <summary>Where a forward pass's commands are in the frame's chunk: its multi-draws (first command, and the count's offset).</summary>
	internal readonly List<(BufferHandle Buffer, long CommandsOffset, List<(int First, long CountOffset, int Max)> Runs, long ListedOffset, long CursorOffset)> Passes = new();

	/// <summary>Targets of <paramref name="width"/> by <paramref name="height"/>, the size of the view it's for.</summary>
	public VisibilityFigure( Device gpu, int width, int height )
	{
		this.gpu = gpu;
		Count = gpu.CreateTexture( new TextureInfo { Width = width, Height = height, Depth = 1, Layers = 1, Mips = 1, Samples = 1, Dimension = TextureDimension.Texture2D, Format = Format.R16Float, Usage = TextureUsage.ColorTarget | TextureUsage.CopySource, Name = "Visibility figure count" } );
		Colour = gpu.CreateTexture( new TextureInfo { Width = width, Height = height, Depth = 1, Layers = 1, Mips = 1, Samples = 1, Dimension = TextureDimension.Texture2D, Format = Format.RGBA8Unorm, Usage = TextureUsage.ColorTarget | TextureUsage.CopySource, Name = "Visibility figure colour" } );
	}

	/// <summary>Start another frame: the targets clear when it first draws, and the passes are forgotten.</summary>
	public void Reset()
	{
		Cleared = false;
		VisibilityFragments = 0;
		Passes.Clear();
	}

	/// <summary>
	/// The frame's forward passes' commands (multi-draw slots kept), the triangles they draw, the fragments listed for
	/// compaction and the compacted triangles: read from the frame's host-visible chunk once the device is idle.
	/// </summary>
	public (long Commands, long Triangles, long Listed, long CompactedTriangles) Counts()
	{
		long commands = 0, triangles = 0, listed = 0, compacted = 0;
		foreach ( var pass in Passes )
		{
			var data = gpu.Map( pass.Buffer );
			foreach ( var run in pass.Runs )
			{
				var count = (int)Math.Min( BitConverter.ToUInt32( data[(int)run.CountOffset..] ), (uint)run.Max );
				commands += count;
				for ( int c = 0; c < count; c++ )
				{
					var at = (int)pass.CommandsOffset + (run.First + c) * GpuDrivenTables.CommandStride;
					triangles += BitConverter.ToUInt32( data[at..] ) / 3 * BitConverter.ToUInt32( data[(at + 4)..] );
				}
			}

			if ( pass.ListedOffset >= 0 ) listed += BitConverter.ToUInt32( data[(int)pass.ListedOffset..] );
			if ( pass.CursorOffset >= 0 ) compacted += BitConverter.ToUInt32( data[(int)pass.CursorOffset..] ) / 3;
		}

		return (commands, triangles, listed, compacted);
	}

	/// <summary>Free its targets.</summary>
	public void Dispose()
	{
		gpu.Destroy( Count );
		gpu.Destroy( Colour );
	}
}
