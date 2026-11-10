using Basalt.Backend;
using Basalt.Gpu;
using Basalt.Source2;
using Basalt.Source2.Shaders;
using Sandbox.Rendering;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using GpuDevice = Basalt.Gpu.Device;
using GpuFormat = Basalt.Gpu.Format;
using GpuCommandList = Basalt.Gpu.CommandList;
using EngineCommandList = Sandbox.Rendering.CommandList;

namespace Sandbox.BasaltLab;

/// <summary>
/// <c>-ui-parity folder</c>: paints a card of UI with the engine's <see cref="Painter"/>, then draws the painter's
/// batch twice in one process - through Source 2's device, as the game does, and through a Basalt.Gpu device of its
/// own with the same compiled shader (<c>ui_cssbox_batched</c>) and the same instance tables - and compares the pixels.
/// Saves <c>native.png</c>, <c>basalt.png</c> and <c>diff.png</c> (differences x16) and exits 1 when they differ by
/// more than <c>-tolerance</c> (default 2) in any channel.
/// <para>
/// A spike for moving the engine's own rendering (UI, <c>Graphics</c>, <c>CommandList</c>) onto Basalt.Gpu; see
/// docs/managed/basalt-gpu-path.md. The card is one batch (one blend mode, no layers or backdrops).
/// </para>
/// </summary>
internal static class UiParity
{
	const int Width = 320, Height = 200;

	public static int Run( Source2Backend backend, string folder )
	{
		LoadFonts();
		backend.Phase( "Fonts" );
		backend.BeginFrame();

		var imagePixels = Checker();
		var image = Texture.Create( 8, 8 ).WithData( imagePixels ).Finish();

		// Paint the card into a command list, keeping the painter's tables
		var commands = new EngineCommandList( "UI parity" );
		using ( var painter = Painter.Begin( commands, new Rect( 0, 0, Width, Height ) ) ) Card( painter, image );
		var batcher = commands.FindResource<Painter.Context>().Batcher;
		if ( batcher.DrawCalls != 1 ) throw new InvalidOperationException( $"The card should be one batch, it's {batcher.DrawCalls}" );

		// Source 2: as Painter.Begin( Texture ) submits
		var target = Texture.CreateRenderTarget().WithSize( Width, Height ).Create();
		using ( var scope = Graphics.Scope.Create() )
		{
			Graphics.RenderTarget = RenderTarget.From( target );
			scope.Attributes.Set( "UIGammaOutput", true );
			scope.Attributes.Set( "UIFrameGrabEncoded", true );
			commands.Execute();
		}

		g_pRenderDevice.Flush();
		var native = target.GetPixels();
		backend.Phase( "Source 2 draw" );

		// Basalt.Gpu: its own device, the same shader and tables
		using var gpu = GpuDevice.Create( new DeviceOptions { ApplicationName = "Basalt Lab UI parity", Validation = Args.Has( "-validation" ), Presentation = false, Message = m => Log.Info( $"Basalt.Gpu: {m.Severity} {m.Text}" ) } );
		backend.Phase( $"Basalt.Gpu device ({gpu.Adapter.Name})" );
		var basalt = DrawOnBasaltGpu( gpu, batcher, (image, imagePixels) );
		backend.Phase( "Basalt.Gpu draw" );

		// Compare
		var tolerance = Args.Int( "-tolerance", 2 );
		var diff = new Color32[native.Length];
		int maxDiff = 0, over = 0;
		for ( int i = 0; i < native.Length; i++ )
		{
			var a = native[i];
			var b = basalt[i];
			var d = Math.Max( Math.Max( Math.Abs( a.r - b.r ), Math.Abs( a.g - b.g ) ), Math.Max( Math.Abs( a.b - b.b ), Math.Abs( a.a - b.a ) ) );
			maxDiff = Math.Max( maxDiff, d );
			if ( d > tolerance ) over++;
			var v = (byte)Math.Min( 255, d * 16 );
			diff[i] = new Color32( v, v, v, 255 );
		}

		folder = System.IO.Path.GetFullPath( folder );
		System.IO.Directory.CreateDirectory( folder );
		Source2Backend.SavePng( native, Width, Height, System.IO.Path.Combine( folder, "native.png" ) );
		Source2Backend.SavePng( basalt, Width, Height, System.IO.Path.Combine( folder, "basalt.png" ) );
		Source2Backend.SavePng( diff, Width, Height, System.IO.Path.Combine( folder, "diff.png" ) );

		var summary = $"UI parity: {batcher.Instances.Count} instances in {batcher.DrawCalls} batch, {Width}x{Height}; max channel difference {maxDiff}, {over} pixels over {tolerance} ({folder}); validation errors {gpu.ValidationErrors}";
		Log.Info( summary );
		Console.WriteLine( summary );
		return over == 0 && gpu.ValidationErrors == 0 ? 0 : 1;
	}

	/// <summary>
	/// The core fonts, as the panel app loads them (through reflection: the font and filesystem types' assemblies
	/// aren't referenced here).
	/// </summary>
	static void LoadFonts()
	{
		var manager = typeof( Texture ).Assembly.GetType( "Sandbox.FontManager" );
		var instance = manager.GetField( "Instance", BindingFlags.Static | BindingFlags.Public ).GetValue( null );
		var core = typeof( EngineFileSystem ).GetProperty( "CoreContent", BindingFlags.Static | BindingFlags.Public ).GetValue( null );
		manager.GetMethod( "LoadAll", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ).Invoke( instance, [core] );
	}

	/// <summary>
	/// One batch exercising what the batched box shader does: rounded fills, per-side borders, gradients, outset and
	/// inset shadows, outlines, rounded clips, transforms, paths, text and an image.
	/// </summary>
	static void Card( Painter painter, Texture image )
	{
		painter.Clear( new Color( 0.12f, 0.13f, 0.15f ) );

		painter.Fill = new Color( 0.9f, 0.3f, 0.2f );
		painter.Stroke = Stroke.Solid( Color.White, 3 );
		painter.Rect( new Rect( 12, 12, 90, 60 ), new Painter.CornerRadii( 14 ) );

		painter.RectShadow( new Rect( 120, 16, 80, 52 ), new Painter.CornerRadii( 8 ), Color.Black.WithAlpha( 0.8f ), blur: 10, offset: new Vector2( 3, 4 ) );
		painter.Stroke = default;
		painter.Fill = Fill.LinearGradient( new Color( 0.2f, 0.6f, 1 ), new Color( 0.9f, 0.2f, 0.8f ) );
		painter.Rect( new Rect( 120, 16, 80, 52 ), new Painter.CornerRadii( 8 ) );

		painter.Fill = new Color( 0.95f, 0.9f, 0.6f );
		painter.Rect( new Rect( 220, 14, 86, 56 ), new Painter.CornerRadii( 6 ) );
		painter.RectShadow( new Rect( 220, 14, 86, 56 ), new Painter.CornerRadii( 6 ), new Color( 0.5f, 0.2f, 0, 0.9f ), blur: 8, inset: true );
		painter.Outline( new Rect( 220, 14, 86, 56 ), 6, 3 );

		using ( painter.Scope() )
		{
			painter.Clip( new Rect( 12, 90, 120, 90 ), new Painter.CornerRadii( 20 ) );
			painter.Fill = new Color( 0.3f, 0.8f, 0.4f );
			painter.Circle( new Vector2( 30, 100 ), 50 );
			painter.Fill = new Color( 0.1f, 0.2f, 0.7f, 0.7f );
			painter.Rect( new Rect( 60, 120, 100, 80 ) );
		}

		using ( painter.Scope() )
		{
			painter.Translate( 190, 135 );
			painter.Rotate( 20 );
			painter.Fill = new Color( 1, 0.8f, 0.1f );
			painter.Stroke = Stroke.Solid( new Color( 0.2f, 0.1f, 0 ), 2 );
			painter.Star( Vector2.Zero, 18, 16 );
		}

		painter.Fill = new Color( 0.4f, 0.9f, 1 );
		painter.Stroke = default;
		painter.Ring( new Vector2( 268, 140 ), 14, 26 );

		painter.Texture( image, new Rect( 140, 90, 32, 32 ) );

		painter.TextStyle = painter.TextStyle with { Color = Color.White, FontSize = 18 };
		painter.Text( "Basalt.Gpu UI", new Rect( 150, 170, 170, 26 ) );
	}

	static byte[] Checker()
	{
		var pixels = new byte[8 * 8 * 4];
		for ( int y = 0; y < 8; y++ )
		{
			for ( int x = 0; x < 8; x++ )
			{
				var i = (y * 8 + x) * 4;
				var on = ((x ^ y) & 2) != 0;
				pixels[i] = (byte)(on ? 255 : 40);
				pixels[i + 1] = (byte)(x * 32);
				pixels[i + 2] = (byte)(on ? 60 : 220);
				pixels[i + 3] = 255;
			}
		}

		return pixels;
	}

	/// <summary>
	/// Draw the painter's batch on Basalt.Gpu: upload every table it uploads under the same attribute names, the glyph
	/// cache too, with texture and sampler indices into Source 2's bindless table moved to Basalt.Gpu's.
	/// </summary>
	static Color32[] DrawOnBasaltGpu( GpuDevice gpu, PainterBatcher batcher, (Texture Texture, byte[] Pixels) image )
	{
		using var library = new ShaderLibrary( gpu, ContentFiles.FindGame() );
		using var defaults = new ShaderDefaults( gpu );
		var drawer = new ShaderDrawer( gpu, library, defaults );
		var shader = library.Load( "shaders/ui_cssbox_batched.shader" );

		// The image, and Source 2's bindless indices mapped to Basalt.Gpu's
		var texture = gpu.CreateTexture( TextureInfo.Texture2D( 8, 8, GpuFormat.RGBA8Unorm, name: "Checker" ) );
		gpu.WriteTexture( texture, image.Pixels, 0, 0 );
		var transparent = gpu.CreateTexture( TextureInfo.Texture2D( 1, 1, GpuFormat.RGBA8Unorm, name: "Transparent" ) );
		gpu.WriteTexture( transparent, [128, 128, 128, 0], 0, 0 );
		var textures = new Dictionary<int, int>
		{
			[image.Texture.Index] = gpu.BindlessIndex( texture ),
			[Texture.Transparent.Index] = gpu.BindlessIndex( transparent ),
		};

		var samplers = new Dictionary<int, int>();
		foreach ( var (address, mode) in new[] { (TextureAddressMode.Clamp, AddressMode.Clamp), (TextureAddressMode.Wrap, AddressMode.Wrap) } )
		{
			samplers[SamplerState.GetBindlessIndex( new SamplerState { AddressModeU = address, AddressModeV = address, Filter = FilterMode.Bilinear } )] = gpu.SamplerIndex( gpu.CreateSampler( SamplerInfo.Linear( mode ) ) );
			samplers[SamplerState.GetBindlessIndex( new SamplerState { AddressModeU = address, AddressModeV = address, Filter = FilterMode.Point } )] = gpu.SamplerIndex( gpu.CreateSampler( SamplerInfo.Point( mode ) ) );
		}

		var instances = batcher.Instances.ToArray();
		for ( int i = 0; i < instances.Length; i++ )
		{
			ref var box = ref instances[i];
			var text = (box.Flags & (1u << 14)) != 0;
			if ( !text && box.TextureIndex > 0 ) box.TextureIndex = Remap( textures, box.TextureIndex, "texture" );
			if ( box.SamplerIndex > 0 ) box.SamplerIndex = Remap( samplers, box.SamplerIndex, "sampler" );
			if ( box.BorderImageIndex > 0 || box.TextMaskIndex > 0 ) throw new InvalidOperationException( "Border images and text masks aren't mapped" );
		}

		var attributes = new ShaderAttributes();
		var buffers = new List<BufferHandle>();
		void Table( string name, ReadOnlySpan<byte> data )
		{
			if ( data.IsEmpty ) return;
			var buffer = gpu.CreateBuffer( new BufferInfo { Size = data.Length, Usage = BufferUsage.Storage, Name = name } );
			gpu.WriteBuffer( buffer, data );
			attributes.Set( name, buffer );
			buffers.Add( buffer );
		}

		// Every table but the boxes, generically: each knows the attribute it's bound as
		foreach ( var table in (IEnumerable)typeof( PainterBatcher ).GetField( "_tables", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( batcher ) )
		{
			var name = (string)Field( table, f => f.Name.Contains( "attribute" ) );
			if ( name != "BoxInstances" ) Table( name, Bytes( Field( table, f => f.Name == "_items" ) ) );
		}

		Table( "BoxInstances", MemoryMarshal.AsBytes( instances.AsSpan() ) );

		// The glyph cache, as GpuFontGlyphCache.Bind binds it
		var glyphs = typeof( GpuFontGlyphCache );
		Table( "GlyphCurves", Bytes( glyphs.GetField( "_curves", BindingFlags.Static | BindingFlags.NonPublic ).GetValue( null ) ) );
		Table( "GlyphBands", Bytes( glyphs.GetField( "_bands", BindingFlags.Static | BindingFlags.NonPublic ).GetValue( null ) ) );
		Table( "GlyphTable", Bytes( glyphs.GetField( "_table", BindingFlags.Static | BindingFlags.NonPublic ).GetValue( null ) ) );

		// PainterBatcher.Draw's attributes, over the texture painter's scope (UIGammaOutput, UIFrameGrabEncoded)
		attributes.Set( "LayerMat", ShaderDefaults.Floats( System.Numerics.Matrix4x4.Identity ) );
		attributes.Set( "UIGammaOutput", 1 );
		attributes.Set( "UIFrameGrabEncoded", 1 );
		attributes.Set( "InstanceOffset", 0 );
		attributes.SetCombo( "D_POLYGON_POINTS", attributes.Get( "PolygonPointBuffer" ) is null ? 0 : 1 );
		attributes.SetCombo( "D_BLENDMODE", 0 );

		var indices = gpu.CreateBuffer( new BufferInfo { Size = 6 * 4, Usage = BufferUsage.Index, Name = "Quad" } );
		gpu.WriteBuffer<uint>( indices, [0, 1, 2, 0, 2, 3] );

		// Painter.Clear premultiplies
		var clear = new Color( 0.12f, 0.13f, 0.15f );
		var color = gpu.CreateTexture( TextureInfo.RenderTarget( Width, Height, GpuFormat.RGBA8Unorm, name: "UI" ) );
		var list = gpu.BeginCommandList( QueueType.Graphics, "UI parity" );
		list.Barrier( Basalt.Gpu.TextureBarrier.Transition( color, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.None, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
		list.BeginRendering( [ColorTarget.Of( color, new System.Numerics.Vector4( clear.r, clear.g, clear.b, 1 ) )] );
		if ( !drawer.DrawIndexedInstanced( list, shader, attributes, GpuFormat.RGBA8Unorm, 1, indices, 6, instances.Length, new System.Numerics.Vector4( 0, 0, Width, Height ) ) )
			throw new InvalidOperationException( "ui_cssbox_batched didn't draw" );
		list.EndRendering();

		var readback = gpu.CreateBuffer( new BufferInfo { Size = Width * Height * 4, Memory = MemoryLocation.Readback, Name = "Readback" } );
		list.Barrier( Basalt.Gpu.TextureBarrier.Transition( color, ImageLayout.ColorTarget, ImageLayout.CopySource, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite, PipelineStage.Transfer, MemoryAccess.TransferRead ) );
		list.CopyTextureToBuffer( color, 0, 0, readback );
		list.Barrier( new BufferBarrier { Buffer = readback, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		gpu.Wait( gpu.Submit( list ) );

		var bytes = gpu.Map( readback );
		var pixels = new Color32[Width * Height];
		for ( int i = 0; i < pixels.Length; i++ ) pixels[i] = new Color32( bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3] );
		gpu.WaitIdle();
		return pixels;
	}

	/// <summary>A private field of an object's class or a class it derives from.</summary>
	static object Field( object value, Func<FieldInfo, bool> match )
	{
		for ( var type = value.GetType(); type is not null; type = type.BaseType )
		{
			var field = type.GetFields( BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly ).FirstOrDefault( match );
			if ( field is not null ) return field.GetValue( value );
		}

		throw new MissingFieldException( value.GetType().Name );
	}

	static int Remap( Dictionary<int, int> map, int index, string what ) =>
		map.TryGetValue( index, out var mapped ) ? mapped : throw new InvalidOperationException( $"No Basalt.Gpu {what} for Source 2's bindless index {index}" );

	/// <summary>A <c>List&lt;T&gt;</c> of unmanaged values as bytes.</summary>
	static byte[] Bytes( object list )
	{
		var method = typeof( UiParity ).GetMethod( nameof( ListBytes ), BindingFlags.Static | BindingFlags.NonPublic ).MakeGenericMethod( list.GetType().GetGenericArguments()[0] );
		return (byte[])method.Invoke( null, [list] );
	}

	static byte[] ListBytes<T>( List<T> list ) where T : unmanaged => MemoryMarshal.AsBytes( CollectionsMarshal.AsSpan( list ) ).ToArray();
}
