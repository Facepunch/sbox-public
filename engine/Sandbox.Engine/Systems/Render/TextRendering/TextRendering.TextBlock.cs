using Sandbox.UI;

namespace Sandbox;

public static partial class TextRendering
{
	/// <summary>
	/// We'll expose this at some point, but will probably be as Sandbox.TextBlock - and then need to think about ownership and caching
	/// </summary>
	internal class TextBlock : IDisposable
	{
		public Texture Texture;

		public TextFlag Flags;
		public Vector2 Clip;
		public bool IsEmpty;
		internal int CacheKey;

		public ulong LastPreparedFrame;

		Scope _scope;
		Margin _effectMargin = default;


		internal void Initialize( Scope scope )
		{
			_scope = scope;
			IsEmpty = string.IsNullOrEmpty( _scope.Text );
			LastPreparedFrame = Application.FrameCount;
			_effectMargin = default;

			if ( scope.Outline.Enabled && scope.Outline.Size > 0 )
			{
				_effectMargin.Left = MathF.Max( _effectMargin.Left, scope.Outline.Size ).CeilToInt();
				_effectMargin.Right = MathF.Max( _effectMargin.Left, scope.Outline.Size ).CeilToInt();
				_effectMargin.Top = MathF.Max( _effectMargin.Left, scope.Outline.Size ).CeilToInt();
				_effectMargin.Bottom = MathF.Max( _effectMargin.Left, scope.Outline.Size ).CeilToInt();
			}

			if ( scope.OutlineUnder.Enabled && scope.OutlineUnder.Size > 0 )
			{
				_effectMargin.Left = MathF.Max( _effectMargin.Left, scope.OutlineUnder.Size ).CeilToInt();
				_effectMargin.Right = MathF.Max( _effectMargin.Left, scope.OutlineUnder.Size ).CeilToInt();
				_effectMargin.Top = MathF.Max( _effectMargin.Left, scope.OutlineUnder.Size ).CeilToInt();
				_effectMargin.Bottom = MathF.Max( _effectMargin.Left, scope.OutlineUnder.Size ).CeilToInt();
			}

			if ( scope.Shadow.Enabled )
			{
				_effectMargin.Left = MathF.Max( _effectMargin.Left, scope.Shadow.Size.Clamp( 0, 512 ) * 3 + -scope.Shadow.Offset.x ).CeilToInt();
				_effectMargin.Right = MathF.Max( _effectMargin.Right, scope.Shadow.Size.Clamp( 0, 512 ) * 3 + scope.Shadow.Offset.x ).CeilToInt();
				_effectMargin.Top = MathF.Max( _effectMargin.Top, scope.Shadow.Size.Clamp( 0, 512 ) * 3 + -scope.Shadow.Offset.y ).CeilToInt();
				_effectMargin.Bottom = MathF.Max( _effectMargin.Bottom, scope.Shadow.Size.Clamp( 0, 512 ) * 3 + scope.Shadow.Offset.y ).CeilToInt();
			}

			if ( scope.ShadowUnder.Enabled )
			{
				_effectMargin.Left = MathF.Max( _effectMargin.Left, scope.ShadowUnder.Size.Clamp( 0, 512 ) * 3 + -scope.ShadowUnder.Offset.x ).CeilToInt();
				_effectMargin.Right = MathF.Max( _effectMargin.Right, scope.ShadowUnder.Size.Clamp( 0, 512 ) * 3 + scope.ShadowUnder.Offset.x ).CeilToInt();
				_effectMargin.Top = MathF.Max( _effectMargin.Top, scope.ShadowUnder.Size.Clamp( 0, 512 ) * 3 + -scope.ShadowUnder.Offset.y ).CeilToInt();
				_effectMargin.Bottom = MathF.Max( _effectMargin.Bottom, scope.ShadowUnder.Size.Clamp( 0, 512 ) * 3 + scope.ShadowUnder.Offset.y ).CeilToInt();
			}

			// don't let shit get crazy
			_effectMargin.Left = MathF.Min( _effectMargin.Left, 512 );
			_effectMargin.Right = MathF.Min( _effectMargin.Right, 512 );
			_effectMargin.Top = MathF.Min( _effectMargin.Top, 512 );
			_effectMargin.Bottom = MathF.Min( _effectMargin.Bottom, 512 );
		}

		public virtual void Dispose()
		{
			Texture?.Dispose();
			Texture = null;
		}

		Topten.RichTextKit.TextAlignment GetAlignment()
		{
			if ( Flags.Contains( TextFlag.Left ) ) return Topten.RichTextKit.TextAlignment.Left;
			if ( Flags.Contains( TextFlag.CenterHorizontally ) ) return Topten.RichTextKit.TextAlignment.Center;
			if ( Flags.Contains( TextFlag.Right ) ) return Topten.RichTextKit.TextAlignment.Right;

			return Topten.RichTextKit.TextAlignment.Left;
		}

		/// <summary>The laid out block, after <see cref="EnsureLayout"/>.</summary>
		internal Topten.RichTextKit.TextBlock Layout;

		/// <summary>Size of the rendered text including effect margins and glyph overhang - the texture's size.</summary>
		internal Vector2 Size;

		/// <summary>Where the block's (0,0) sits inside that size.</summary>
		internal Vector2 BlockOrigin;

		/// <summary>Lay the text out, once.</summary>
		public Vector2 Measure()
		{
			EnsureLayout();
			return Size;
		}

		internal void EnsureLayout()
		{
			LastPreparedFrame = Application.FrameCount;
			// TryAdd locks even when the key is there, and text blocks are prepared from several threads every frame
			if ( CacheKey != 0 && !Dictionary.ContainsKey( CacheKey ) ) Dictionary.TryAdd( CacheKey, this );

			if ( Layout != null )
				return;

			var block = new Topten.RichTextKit.TextBlock();
			block.FontMapper = FontManager.Instance;
			block.MaxWidth = Clip.x;
			block.MaxHeight = Clip.y;
			block.Alignment = GetAlignment();

			if ( Flags.Contains( TextFlag.SingleLine ) ) // should we remove any newlines?
			{
				block.MaxLines = 1;
			}

			if ( Flags.Contains( TextFlag.DontClip ) )
			{
				block.MaxWidth = null;
				block.MaxHeight = null;
			}

			var style = new Topten.RichTextKit.Style();
			_scope.ToStyle( style );

			block.AddText( IsEmpty ? "." : _scope.Text, style );

			var pad = block.MeasuredPadding;

			int width = block.MeasuredWidth.CeilToInt().Clamp( 2, 4096 );
			int height = block.MeasuredHeight.CeilToInt().Clamp( 2, 4096 );

			if ( style.LetterSpacing < 0 )
				width += Math.Abs( (int)MathF.Floor( style.LetterSpacing ) );

			// Ink that reaches past the measured rect (italic tails, accents, tight bearings) needs room too
			var overhang = block.MeasuredOverhang;
			var margin = _effectMargin + new Margin( MathF.Ceiling( overhang.Left ), MathF.Ceiling( overhang.Top ), MathF.Ceiling( overhang.Right ), MathF.Ceiling( overhang.Bottom ) );

			var marginEdge = margin.EdgeSize;
			width += marginEdge.x.CeilToInt();
			height += marginEdge.y.CeilToInt();

			// Nothing to draw for an empty block, but it keeps the size the placeholder measured to
			if ( IsEmpty )
				block.Clear();

			Layout = block;
			Size = new Vector2( width, height );
			BlockOrigin = new Vector2( margin.Left - pad.Left, margin.Top - pad.Top );
		}

		public void MakeReady()
		{
			EnsureLayout();

			if ( Texture != null )
				return;

			EnsureLayout();

			Texture = GpuFontText.Render( Layout, BlockOrigin, (int)Size.x, (int)Size.y, _scope.IsHdr, 8, GpuFontText.Options.For( _scope ) );
		}

		List<GPUBoxInstance> _instances;
		readonly object _instancesLock = new();

		/// <summary>
		/// Build the block's glyph instances and tiles (<see cref="GpuFontText.Build"/>: its glyphs' outlines encoded the first
		/// time each is used) if they aren't yet. <see cref="Upload"/> does on its first draw; a text renderer does as its text is
		/// set, so a view first drawing it (an envmap capture face, on the render thread) doesn't. Any thread.
		/// </summary>
		/// <summary>Whether its glyph instances are built (<see cref="PrepareInstances"/>), for tests.</summary>
		internal bool HasInstances => System.Threading.Volatile.Read( ref _instances ) is not null;

		internal void PrepareInstances()
		{
			if ( System.Threading.Volatile.Read( ref _instances ) is not null ) return;
			lock ( _instancesLock )
			{
				if ( _instances is not null ) return;
				EnsureLayout();
				var instances = new List<GPUBoxInstance>();
				GpuFontText.Build( Layout, BlockOrigin, GpuFontText.Options.For( _scope ), instances );
				_tiles = GpuFontText.Bin( instances, (int)Size.x, (int)Size.y );
				System.Threading.Volatile.Write( ref _instances, instances );
			}
		}
		List<uint> _tiles;
		GpuFontText.Placement _placement;

		/// <summary>
		/// The block's glyph instances in this frame's shared text buffers, for a quad that composites them per
		/// pixel straight from the outlines (like particle text). Built and binned into tiles once, appended once a frame however often it's drawn.
		/// </summary>
		internal GpuFontText.Placement Upload()
		{
			if ( _placement.Frame == Application.FrameCount )
				return _placement;

			EnsureLayout();
			PrepareInstances();

			_placement = GpuFontText.Upload( _instances, _tiles, (int)Size.x, (int)Size.y );

			// Evicted by Tick while a command list still held us: back in the cache so it can evict us again
			if ( CacheKey != 0 && !Dictionary.ContainsKey( CacheKey ) ) Dictionary.TryAdd( CacheKey, this );

			return _placement;
		}

	}
}
