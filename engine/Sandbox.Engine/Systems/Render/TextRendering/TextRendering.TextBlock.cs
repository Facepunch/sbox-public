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
			if ( CacheKey != 0 ) Dictionary.TryAdd( CacheKey, this );

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
		GpuFontText.Placement _placement;

		/// <summary>
		/// The block's glyph instances in this frame's shared text buffers, for a quad that composites them per
		/// pixel straight from the outlines (like particle text). Built once, uploaded once a frame however often it's drawn.
		/// </summary>
		internal GpuFontText.Placement Upload()
		{
			if ( _placement.Frame == Application.FrameCount )
				return _placement;

			EnsureLayout();

			if ( _instances is null )
			{
				var instances = new List<GPUBoxInstance>();
				GpuFontText.Build( Layout, BlockOrigin, GpuFontText.Options.For( _scope ), instances );
				_instances = instances;
			}

			_placement = GpuFontText.Upload( _instances, (int)Size.x, (int)Size.y );

			// Evicted by Tick while a command list still held us: back in the cache so it can evict us again
			if ( CacheKey != 0 && !Dictionary.ContainsKey( CacheKey ) ) Dictionary.TryAdd( CacheKey, this );

			return _placement;
		}

	}
}
