using Sandbox.Rendering;
using Sandbox.UI;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// Replaying a recorded run of draws into the next frame: a panel subtree that hasn't changed copies last frame's
/// instances instead of resolving its descriptors again. The instances name rows of tables that are rebuilt every frame
/// (scissors, transforms, gradients, shapes, text), so each copied index is moved to the row with the same value this frame.
/// </summary>
internal partial class PainterBatcher
{
	/// <summary>
	/// Changes each time the tables start a new frame, to a number no other batcher's tables have had, so a run recorded
	/// into one batcher is never taken for one of another's. A recording can only be replayed from the frame just before.
	/// </summary>
	internal int Generation => _generation;
	int _generation = NextGeneration();

	// The generation the tables had before the current one, which Previous holds
	int _previousGeneration = -1;

	static int _generations;
	static int NextGeneration() => System.Threading.Interlocked.Increment( ref _generations );

	/// <summary>
	/// Counts drawing that a replay can't repeat: explicit flushes (layers, backdrops, native commands), shapes that reach
	/// into other rows, and videos. A recording is replayable if this didn't move while it was made.
	/// </summary>
	internal int Unreplayable;

	// Per box instance: the blend mode it was appended with, and whether it starts a text run (its glyphs share one bounds)
	List<byte> _boxMeta = [];
	List<byte> _previousBoxMeta = [];
	const int TextRunStart = 0x80;

	/// <summary>
	/// Shapes made entirely of their own fields. The others address path buffers or another shape row by index.
	/// </summary>
	static bool IsSelfContained( in UICssBoxBatched.BorderShape shape )
	{
		if ( shape.PathCount != 0 || shape.PathNodeCount != 0 ) return false;
		return shape.Kind is UICssBoxBatched.ShapeKind.Polygon or UICssBoxBatched.ShapeKind.Circle or UICssBoxBatched.ShapeKind.Capsule
			or UICssBoxBatched.ShapeKind.Crescent or UICssBoxBatched.ShapeKind.Heart or UICssBoxBatched.ShapeKind.SimpleLine;
	}

	/// <summary>
	/// Where a run of draws sits in one frame's tables.
	/// </summary>
	internal struct Run
	{
		internal int Generation;
		internal int Boxes, BoxCount;
		internal int Text, TextCount;
		internal int Textures, TextureCount;
	}

	/// <summary>
	/// The table positions and counters when a run starts recording.
	/// </summary>
	internal readonly record struct Capture( int Boxes, int Text, int Textures, int Unreplayable );

	internal Capture BeginCapture() => new( _boxTable.Count, _textTable.Count, _textures.Count, Unreplayable );

	/// <summary>
	/// Ends a run started with <see cref="BeginCapture"/>. False when it drew something a replay can't repeat.
	/// </summary>
	internal bool EndCapture( in Capture capture, out Run run )
	{
		run = new Run
		{
			Generation = Generation,
			Boxes = capture.Boxes,
			BoxCount = _boxTable.Count - capture.Boxes,
			Text = capture.Text,
			TextCount = _textTable.Count - capture.Text,
			Textures = capture.Textures,
			TextureCount = _textures.Count - capture.Textures
		};
		return Unreplayable == capture.Unreplayable && !DebugVisualizeBatches;
	}

	/// <summary>
	/// Whether <paramref name="run"/> was recorded or replayed last frame, and every texture it names is where it was.
	/// </summary>
	internal bool CanReplay( in Run run )
	{
		if ( run.Generation != _previousGeneration || DebugVisualizeBatches ) return false;

		var textures = CollectionsMarshal.AsSpan( _previousTextures ).Slice( run.Textures, run.TextureCount );
		foreach ( ref readonly var usage in textures )
		{
			if ( usage.Texture is not { } texture || !texture.IsValid() ) return false;
			if ( usage.Index != BoundByReference && texture.Index != usage.Index ) return false;
		}

		return true;
	}

	/// <summary>
	/// Render targets (layers) pushed since the tables were cleared. A recording that drew into one isn't kept whole.
	/// </summary>
	internal int TargetsPushed;

	/// <summary>
	/// Whether another list's drawing can be inserted here: outside layers, whose content is hashed from the instances
	/// drawn into them.
	/// </summary>
	internal bool CanInsertRetained => _targetDepth == 0 && _hash.Layers == 0;

	/// <summary>
	/// Draws a list recorded by a batcher of its own (a retained panel subtree) at this point: ends the pending batch,
	/// inserts the list, and forgets what's been drawn for backdrops after it.
	/// </summary>
	internal void InsertRetained( CommandList list )
	{
		Flush();
		_commands.InsertList( list );
		InvalidateBackdrop();
	}

	// Last frame's row index to this frame's, valid where the stamp is this generation
	int[] _scissorMap = [], _scissorStamp = [];
	int[] _transformMap = [], _transformStamp = [];
	int[] _gradientMap = [], _gradientStamp = [];
	int[] _shapeMap = [], _shapeStamp = [];

	/// <summary>
	/// Appends last frame's <paramref name="run"/> to this frame, as recording the same drawing again would: the same
	/// instances, blend changes, retained textures and backdrop writes. Call <see cref="CanReplay"/> first.
	/// Updates <paramref name="run"/> to where it now sits.
	/// </summary>
	internal void Replay( ref Run run )
	{
		Grow( ref _scissorMap, ref _scissorStamp, _scissorTable.Previous.Length );
		Grow( ref _transformMap, ref _transformStamp, _transformTable.Previous.Length );
		Grow( ref _gradientMap, ref _gradientStamp, _gradientTable.Previous.Length );
		Grow( ref _shapeMap, ref _shapeStamp, _shapeTable.Previous.Length );

		// Textures first: they're what the old run's draws read, and what keeps them marked used
		int textures = _textures.Count;
		foreach ( ref readonly var usage in CollectionsMarshal.AsSpan( _previousTextures ).Slice( run.Textures, run.TextureCount ) )
		{
			// As RetainTexture: inside a cached layer, a texture's version is content and a live one can't be cached
			var texture = usage.Texture;
			_textures.Add( usage );
			if ( _hash.Layers > 0 ) _hash.Extra += texture.DirtyVersion;
			if ( _hash.Layers > 0 && (texture.IsAnimated || texture.IsRenderTarget) ) Volatile++;
			if ( usage.Presented ) texture.MarkUsed();
		}

		// Glyph rows, with their gradients moved to this frame's gradient rows
		int text = _textTable.Count;
		if ( run.TextCount > 0 )
		{
			var glyphs = _textTable.Append( run.TextCount );
			_textTable.Previous.Slice( run.Text, run.TextCount ).CopyTo( glyphs );
			foreach ( ref var glyph in glyphs )
			{
				if ( glyph.TextureIndex < 0 && GpuFontText.WantsGradient( glyph ) )
					glyph.TextureIndex = -RemapGradient( -glyph.TextureIndex - 1 ) - 1;
			}
			_textTable.Fold( glyphs );
		}

		int boxes = _boxTable.Count;
		var previousMeta = CollectionsMarshal.AsSpan( _previousBoxMeta ).Slice( run.Boxes, run.BoxCount );
		var source = _boxTable.Previous.Slice( run.Boxes, run.BoxCount );
		int textShift = text - run.Text;
		for ( int i = 0; i < source.Length; )
		{
			// A blend change ends the batch before the instances after it, as when they were recorded
			var blend = (BlendMode)(previousMeta[i] & ~TextRunStart);
			int end = i + 1;
			while ( end < source.Length && (previousMeta[end] & ~TextRunStart) == (int)blend ) end++;

			SetBlendMode( blend );
			var copied = _boxTable.Append( end - i );
			for ( int k = 0; k < copied.Length; k++ )
			{
				var box = source[i + k];
				if ( box.ScissorIndex >= 0 ) box.ScissorIndex = RemapScissor( box.ScissorIndex );
				if ( box.InverseScissorIndex >= 0 ) box.InverseScissorIndex = RemapScissor( box.InverseScissorIndex );
				if ( box.TransformIndex >= 0 ) box.TransformIndex = RemapTransform( box.TransformIndex );
				if ( box.ShapeIndex >= 0 ) box.ShapeIndex = RemapShape( box.ShapeIndex );
				if ( (box.Flags & TextFlag) != 0 ) box.TextureIndex += textShift;
				else if ( box.TextureIndex < 0 ) box.TextureIndex = -RemapGradient( -box.TextureIndex - 1 ) - 1;
				copied[k] = box;
				_boxMeta.Add( previousMeta[i + k] );
			}
			_boxTable.Fold( copied );
			i = end;
		}

		TrackReplayedWrites( boxes );

		run.Generation = Generation;
		run.Boxes = boxes;
		run.Text = text;
		run.Textures = textures;
	}

	// The flag that makes a box a proxy for a glyph row (PainterBatcher.AddText)
	const uint TextFlag = 1u << 14;

	/// <summary>
	/// The replayed instances' bounds for CSS backdrops that may share a capture, the way their recording reported them:
	/// each box alone, a text run's glyphs together.
	/// </summary>
	void TrackReplayedWrites( int first )
	{
		if ( _backdropWrites.Count == 0 ) return;

		var boxes = _boxTable.AsSpan()[first..];
		var meta = CollectionsMarshal.AsSpan( _boxMeta )[first..];
		for ( int i = 0; i < boxes.Length; )
		{
			if ( (boxes[i].Flags & TextFlag) == 0 || (meta[i] & TextRunStart) == 0 )
			{
				TrackBackdropWrite( boxes[i] );
				i++;
				continue;
			}

			var min = new Vector2( float.MaxValue );
			var max = new Vector2( float.MinValue );
			int end = i;
			do
			{
				var rect = boxes[end].Rect;
				min = Vector2.Min( min, new Vector2( rect.x, rect.y ) );
				max = Vector2.Max( max, new Vector2( rect.x + rect.z, rect.y + rect.w ) );
				end++;
			}
			while ( end < boxes.Length && (boxes[end].Flags & TextFlag) != 0 && (meta[end] & TextRunStart) == 0 );

			TrackBackdropWrite( new Rect( min, max - min ), boxes[i].TransformIndex );
			i = end;
		}
	}

	int RemapScissor( int index )
	{
		if ( _scissorStamp[index] == Generation ) return _scissorMap[index];

		ref readonly var key = ref _scissorTable.PreviousKey( index );
		var next = key.Next >= 0 ? RemapScissor( key.Next ) : key.Next;
		var mapped = GetOrAddScissor( key.Scissor, next );
		_scissorStamp[index] = Generation;
		_scissorMap[index] = mapped;
		return mapped;
	}

	int RemapTransform( int index )
	{
		if ( _transformStamp[index] == Generation ) return _transformMap[index];

		var mapped = GetOrAddTransform( _transformTable.Previous[index].Mat );
		_transformStamp[index] = Generation;
		_transformMap[index] = mapped;
		return mapped;
	}

	int RemapGradient( int index )
	{
		if ( _gradientStamp[index] == Generation ) return _gradientMap[index];

		var mapped = GetOrAddGradient( in _gradientTable.PreviousKey( index ) );
		_gradientStamp[index] = Generation;
		_gradientMap[index] = mapped;
		return mapped;
	}

	int RemapShape( int index )
	{
		if ( _shapeStamp[index] == Generation ) return _shapeMap[index];

		var mapped = GetOrAddShape( in _shapeTable.Previous[index] );
		_shapeStamp[index] = Generation;
		_shapeMap[index] = mapped;
		return mapped;
	}

	static void Grow( ref int[] map, ref int[] stamp, int count )
	{
		if ( map.Length >= count ) return;

		var size = Math.Max( 64, (int)System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)count ) );
		Array.Resize( ref map, size );
		Array.Resize( ref stamp, size );
	}
}
