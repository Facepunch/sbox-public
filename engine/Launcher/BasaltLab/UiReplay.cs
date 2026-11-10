using Basalt.Backend;
using Basalt.Gpu;
using Basalt.Source2.Shaders;
using Sandbox.Rendering;
using System.Reflection;
using System.Runtime.InteropServices;
using EngineCommandList = Sandbox.Rendering.CommandList;
using GpuDevice = Basalt.Gpu.Device;
using GpuFormat = Basalt.Gpu.Format;
using Num = System.Numerics;

namespace Sandbox.BasaltLab;

/// <summary>
/// One command an engine <see cref="EngineCommandList"/> recorded, read back: which recording method made it and the
/// entry's fields.
/// </summary>
internal sealed class RecordedCommand
{
	/// <summary>The method that recorded it, as <c>Type.Method(ParameterTypes)</c>: <c>AttributeAccess.Set(StringToken,Single)</c>.</summary>
	public string Kind { get; init; }
	public object Object1 { get; init; }
	public object Object2 { get; init; }
	public object Object3 { get; init; }
	public object Object4 { get; init; }
	public object Object5 { get; init; }
	public uint Token { get; init; }
	public int Count { get; init; }
	public Vector4 Data1 { get; init; }
	public Vector4 Data2 { get; init; }
	public Vector4 Data3 { get; init; }
	public Vector4 Data4 { get; init; }

	/// <summary>Data1 to Data4 as the matrix they hold, M11 first.</summary>
	public Num.Matrix4x4 Matrix => new( Data1.x, Data1.y, Data1.z, Data1.w, Data2.x, Data2.y, Data2.z, Data2.w, Data3.x, Data3.y, Data3.z, Data3.w, Data4.x, Data4.y, Data4.z, Data4.w );

	public override string ToString() => Kind;

	static readonly Type EntryType = typeof( EngineCommandList ).GetNestedType( "Entry", BindingFlags.NonPublic );
	static readonly FieldInfo Entries = typeof( EngineCommandList ).GetField( "_entries", BindingFlags.Instance | BindingFlags.NonPublic );
	static readonly Dictionary<string, FieldInfo> Fields = EntryType.GetFields( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ).ToDictionary( f => f.Name );
	static Dictionary<nint, string> kinds;
	static List<(MethodInfo Open, string Kind)> genericKinds;

	/// <summary>
	/// Every command <paramref name="list"/> holds, in order. Commands are told apart by their execute function: each
	/// recording method of <see cref="EngineCommandList"/> takes the address of its own static local function, which is
	/// found in the method's IL and compared with the entry's pointer.
	/// </summary>
	public static List<RecordedCommand> Read( EngineCommandList list )
	{
		kinds ??= BuildKinds();
		var result = new List<RecordedCommand>();
		foreach ( var entry in (System.Collections.IList)Entries.GetValue( list ) )
		{
			var pointer = Pointer( Fields["Execute"].GetValue( entry ) );
			result.Add( new RecordedCommand
			{
				Kind = KindOf( pointer ) ?? $"unknown 0x{pointer:x}",
				Object1 = Fields["Object1"].GetValue( entry ),
				Object2 = Fields["Object2"].GetValue( entry ),
				Object3 = Fields["Object3"].GetValue( entry ),
				Object4 = Fields["Object4"].GetValue( entry ),
				Object5 = Fields["Object5"].GetValue( entry ),
				Token = ((StringToken)Fields["Token"].GetValue( entry )).Value,
				Count = (int)Fields["Count"].GetValue( entry ),
				Data1 = (Vector4)Fields["Data1"].GetValue( entry ),
				Data2 = (Vector4)Fields["Data2"].GetValue( entry ),
				Data3 = (Vector4)Fields["Data3"].GetValue( entry ),
				Data4 = (Vector4)Fields["Data4"].GetValue( entry ),
			} );
		}

		return result;
	}

	static unsafe nint Pointer( object value ) => value switch
	{
		nint p => p,
		Pointer p => (nint)System.Reflection.Pointer.Unbox( p ),
		_ => throw new InvalidOperationException( $"An entry's execute function read as {value?.GetType()}" ),
	};

	static Dictionary<nint, string> BuildKinds()
	{
		var map = new Dictionary<nint, string>();
		genericKinds = new();
		foreach ( var type in new[] { typeof( EngineCommandList ), typeof( EngineCommandList.AttributeAccess ) } )
		{
			foreach ( var method in type.GetMethods( BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly ) )
			{
				if ( method.Name.Contains( "g__" ) ) continue;
				var il = method.GetMethodBody()?.GetILAsByteArray();
				if ( il is null ) continue;

				var kind = $"{type.Name}.{method.Name}({string.Join( ",", method.GetParameters().Select( p => p.ParameterType.Name.TrimEnd( '&' ) ) )})";
				for ( int i = 0; i + 5 < il.Length; i++ )
				{
					// ldftn <token>
					if ( il[i] != 0xFE || il[i + 1] != 0x06 ) continue;
					MethodBase target;
					try { target = type.Module.ResolveMethod( BitConverter.ToInt32( il, i + 2 ), null, method.IsGenericMethod ? method.GetGenericArguments() : null ); }
					catch ( ArgumentException ) { continue; }

					if ( target is not MethodInfo { IsStatic: true } local || !local.Name.Contains( "g__Execute" ) ) continue;
					if ( local.ContainsGenericParameters ) genericKinds.Add( (local.GetGenericMethodDefinition(), kind) );
					else map.TryAdd( local.MethodHandle.GetFunctionPointer(), kind );
				}
			}
		}

		return map;
	}

	/// <summary>A pointer's kind; generic recorders (<c>SetCombo&lt;T&gt;</c>) are tried with the engine's enums.</summary>
	static string KindOf( nint pointer )
	{
		if ( kinds.TryGetValue( pointer, out var kind ) ) return kind;

		var candidates = new[] { typeof( BlendMode ) }.Concat( typeof( Texture ).Assembly.GetTypes().Where( t => t.IsEnum ) ).Concat( typeof( Vector3 ).Assembly.GetTypes().Where( t => t.IsEnum ) );
		foreach ( var (open, generic) in genericKinds )
		{
			foreach ( var type in candidates )
			{
				try
				{
					if ( open.MakeGenericMethod( type ).MethodHandle.GetFunctionPointer() != pointer ) continue;
					kinds[pointer] = generic;
					return generic;
				}
				catch ( ArgumentException )
				{
				}
			}
		}

		return null;
	}
}

/// <summary>
/// Replays an engine <see cref="EngineCommandList"/> of UI drawing on Basalt.Gpu, through a
/// <see cref="GraphicsContext"/>: what <c>Graphics</c> does with each command on Source 2's device, done with
/// Basalt.Backend's drawer. Engine resources get Basalt.Gpu twins: buffers with the contents Source 2 holds (read back),
/// images with their pixels, named render targets made on demand. Bindless indices in what the painter wrote (the box
/// table, sampler attributes) are moved from Source 2's table to Basalt.Gpu's.
/// </summary>
internal sealed class UiReplay
{
	readonly GpuDevice gpu;
	readonly GraphicsContext context;
	readonly ShaderLibrary library;
	readonly EngineCommandList commands;
	readonly Dictionary<uint, string> names;
	readonly Dictionary<string, TextureHandle> targets = new();
	readonly Stack<(TextureHandle Target, Num.Vector4 Viewport)> stack = new();
	readonly ShaderAttributes scope = new(), draw = new();
	readonly Dictionary<(GpuBuffer Buffer, bool Boxes), BufferHandle> buffers = new();
	readonly Dictionary<Texture, TextureHandle> textures = new();
	readonly Dictionary<int, int> textureIndices = new(), samplerIndices = new();
	readonly List<BufferHandle> ownedBuffers = new();
	readonly List<SamplerHandle> ownedSamplers = new();

	/// <summary>Commands replayed, by kind, and kinds that have no replay (they're reported, and fail the case).</summary>
	public SortedDictionary<string, int> Replayed { get; } = new();
	public SortedSet<string> Unhandled { get; } = new();

	/// <summary>Index mapping checks: native's sRGB slot of each image is its linear slot + 1, as Basalt.Gpu's is.</summary>
	public List<string> Notes { get; } = new();

	/// <summary>
	/// Upload matrix constants transposed: the wrong order, for a negative check that the transform cases notice.
	/// </summary>
	public bool TransposeMatrices { get; init; }

	/// <summary>
	/// A replay of <paramref name="commands"/> into <paramref name="context"/>. <paramref name="textures"/> are the engine
	/// textures the drawing refers to by bindless index; <paramref name="shaders"/> name the attributes the shaders read,
	/// which is how attribute tokens are turned back into names.
	/// </summary>
	public UiReplay( GpuDevice gpu, GraphicsContext context, EngineCommandList commands, IEnumerable<Texture> textures, IEnumerable<ShaderFile> shaders )
	{
		this.gpu = gpu;
		this.context = context;
		this.commands = commands;
		library = context.Drawer.Shaders;
		names = Names( shaders );

		foreach ( var texture in textures.Distinct() ) MapTexture( texture );
		MapSamplers();
	}

	/// <summary>The attributes the scope around the list starts with (what the engine's <c>Graphics.Scope</c> held).</summary>
	public ShaderAttributes Scope => scope;

	/// <summary>Replay every command into the bound target.</summary>
	public void Run() => Run( commands );

	void Run( EngineCommandList list )
	{
		foreach ( var command in RecordedCommand.Read( list ) )
		{
			if ( Replay( command ) ) Replayed[command.Kind] = Replayed.GetValueOrDefault( command.Kind ) + 1;
			else Unhandled.Add( command.Kind );
		}
	}

	bool Replay( RecordedCommand c )
	{
		switch ( c.Kind )
		{
			// The draw's private attribute set starts as a copy of the scope's
			case "CommandList.BeginDrawAttributes()":
				draw.Clear();
				draw.Merge( scope );
				return true;

			case "CommandList.DrawIndexedInstanced(GpuBuffer,Material,Int32,AttributeAccess)":
				return Draw( (Material)c.Object2, Attributes( c.Object3 ), a => context.DrawIndexedInstanced( Shader( (Material)c.Object2 ), a, Buffer( (GpuBuffer)c.Object1, false ), ((GpuBuffer)c.Object1).ElementCount, c.Count ) );

			case "CommandList.DrawQuad(Rect,Material,Color,AttributeAccess)":
				return Draw( (Material)c.Object1, Attributes( c.Object2 ), a => context.DrawQuad( Shader( (Material)c.Object1 ), a, Rect( c.Data1 ), Packed( c.Data2 ) ) );

			case "CommandList.DrawQuad(Rect,Material,Color)":
				return Draw( (Material)c.Object1, scope, a => context.DrawQuad( Shader( (Material)c.Object1 ), a, Rect( c.Data1 ), Packed( c.Data2 ) ) );

			case "CommandList.Clear(Color,Boolean,Boolean,Boolean)":
				if ( c.Data2.x != 0 ) context.Clear( new Num.Vector4( c.Data1.x, c.Data1.y, c.Data1.z, c.Data1.w ) );
				return true;

			case "CommandList.GetRenderTarget(String,Int32,Int32,ImageFormat,ImageFormat,MultisampleAmount,Int32)":
				targets[(string)c.Object5] = Target( (string)c.Object5, (int)c.Data1.x, (int)c.Data1.y, (ImageFormat)(int)c.Data1.z, (int)c.Data2.y );
				// Colour only, no MSAA: what the painter asks for
				return (MultisampleAmount)(int)c.Data2.x == MultisampleAmount.MultisampleNone && (ImageFormat)(int)c.Data1.w == ImageFormat.None;

			case "CommandList.SetRenderTarget(RenderTargetHandle)":
				context.SetTarget( targets[(string)c.Object5] );
				return true;

			case "CommandList.PushRenderTarget()":
				stack.Push( (context.Target, context.Viewport) );
				return true;

			case "CommandList.PopRenderTarget()":
				var (target, viewport) = stack.Pop();
				context.SetTarget( target );
				context.Viewport = viewport;
				return true;

			case "CommandList.ReleaseRenderTarget(RenderTargetHandle)":
				targets.Remove( (string)c.Object5 );
				return true;

			case "CommandList.GenerateMipMaps(RenderTargetHandle,DownsampleMethod)":
				context.GenerateMips( targets[(string)c.Object5], Downsample, (int)c.Data1.x );
				return true;

			// Frame stats
			case "CommandList.AddAction(Action)":
				return true;

			case "CommandList.InsertList(CommandList)":
				Run( (EngineCommandList)c.Object1 );
				return true;

			case "AttributeAccess.GrabFrameTexture(String,DownsampleMethod,Int32)":
				Grab( Attributes( c.Object2 ), (string)c.Object5, (Graphics.DownsampleMethod)(int)c.Data1.x, (int)c.Data1.y );
				return true;

			case "AttributeAccess.Set(StringToken,ColorTextureRef,Int32)":
				if ( c.Data1.x >= 0 ) return false;
				Attributes( c.Object4 ).Set( Name( c.Token ), targets[(string)c.Object5] );
				return true;

			case "AttributeAccess.Set(StringToken,Texture,Int32)":
				if ( c.Data1.x >= 0 ) return false;
				Attributes( c.Object4 ).Set( Name( c.Token ), MapTexture( (Texture)c.Object2 ) );
				return true;

			case "AttributeAccess.Set(StringToken,GpuBuffer)":
				var name = Name( c.Token );
				Attributes( c.Object4 ).Set( name, Buffer( (GpuBuffer)c.Object2, name.Equals( "BoxInstances", StringComparison.OrdinalIgnoreCase ) ) );
				return true;

			case "AttributeAccess.Set(StringToken,Matrix)":
				Attributes( c.Object4 ).Set( Name( c.Token ), ShaderDefaults.Floats( TransposeMatrices ? Num.Matrix4x4.Transpose( c.Matrix ) : c.Matrix ) );
				return true;

			case "AttributeAccess.Set(StringToken,Single)":
			case "AttributeAccess.Set(StringToken,Boolean)":
				Attributes( c.Object4 ).Set( Name( c.Token ), c.Data1.x );
				return true;

			case "AttributeAccess.Set(StringToken,Int32)":
				Attributes( c.Object4 ).Set( Name( c.Token ), Index( Name( c.Token ), (int)c.Data1.x ) );
				return true;

			case "AttributeAccess.Set(StringToken,Vector2)":
				Attributes( c.Object4 ).Set( Name( c.Token ), c.Data1.x, c.Data1.y );
				return true;

			case "AttributeAccess.Set(StringToken,Vector3)":
				Attributes( c.Object4 ).Set( Name( c.Token ), c.Data1.x, c.Data1.y, c.Data1.z );
				return true;

			case "AttributeAccess.Set(StringToken,Vector4)":
				Attributes( c.Object4 ).Set( Name( c.Token ), c.Data1.x, c.Data1.y, c.Data1.z, c.Data1.w );
				return true;

			case "AttributeAccess.SetCombo(StringToken,Int32)":
			case "AttributeAccess.SetCombo(StringToken,Boolean)":
			case "AttributeAccess.SetCombo(StringToken,T)":
				Attributes( c.Object4 ).SetCombo( Name( c.Token ), (int)c.Data1.x );
				return true;

			default:
				return false;
		}
	}

	bool Draw( Material material, ShaderAttributes attributes, Func<ShaderAttributes, bool> issue )
	{
		if ( Shader( material ) is null ) return false;
		return issue( attributes );
	}

	ShaderAttributes Attributes( object access ) => ReferenceEquals( access, commands.Attributes ) ? scope : draw;

	static Num.Vector4 Rect( Vector4 data ) => new( data.x, data.y, data.z, data.w );

	/// <summary>The vertex colour <c>Graphics.DrawQuad</c> writes: the engine's <c>Color.ToColor32()</c>.</summary>
	static uint Packed( Vector4 data ) => new Color( data.x, data.y, data.z, data.w ).ToColor32().RawInt;

	ShaderFile Downsample => field ??= library.Load( "shaders/downsample_cs.shader" );

	readonly Dictionary<Material, ShaderFile> shaders = new();

	ShaderFile Shader( Material material )
	{
		if ( shaders.TryGetValue( material, out var shader ) ) return shader;
		var path = material == Material.UI.BatchedBox ? "shaders/ui_cssbox_batched.shader"
			: material == Material.UI.Filter ? "shaders/ui_filter.shader"
			: material == Material.UI.BackdropFilter ? "shaders/ui_backdropfilter.shader"
			: material == Material.UI.DropShadow ? "shaders/ui_dropshadow.shader"
			: material == Material.UI.BorderWrap ? "shaders/ui_borderwrap.shader"
			: material == Material.UI.Box ? "shaders/ui_cssbox.shader"
			: material == Material.UI.Basic ? "shaders/ui_basic.shader"
			: material == Material.UI.Text ? "shaders/ui_text.shader"
			: null;
		shader = path is null ? null : library.Load( path );
		shaders[material] = shader;
		return shader;
	}

	/// <summary>
	/// A named temporary target, as <c>RenderTarget.GetTemporary</c> makes it: mips capped at a full chain, the bound
	/// target's format for <see cref="ImageFormat.Default"/>.
	/// </summary>
	TextureHandle Target( string name, int width, int height, ImageFormat format, int mips )
	{
		mips = Math.Min( mips, (int)Math.Log2( Math.Max( width, height ) ) + 1 );
		return context.CreateTarget( width, height, Format( format ), mips, name );
	}

	GpuFormat Format( ImageFormat format ) => format switch
	{
		ImageFormat.Default => gpu.InfoOf( context.Target ).Format,
		ImageFormat.RGBA8888 => GpuFormat.RGBA8Unorm,
		ImageFormat.BGRA8888 => GpuFormat.BGRA8Unorm,
		ImageFormat.RGBA16161616F => GpuFormat.RGBA16Float,
		_ => throw new NotSupportedException( $"Render target format {format}" ),
	};

	/// <summary>
	/// <c>Graphics.GrabFrameTexture</c>: a viewport-sized copy of the target with a log2 mip chain (or
	/// <paramref name="maxMips"/>) filtered by <paramref name="method"/>, named so later commands find it.
	/// </summary>
	void Grab( ShaderAttributes attributes, string name, Graphics.DownsampleMethod method, int maxMips )
	{
		var mips = 1;
		var viewport = context.Viewport;
		if ( method != Graphics.DownsampleMethod.None )
		{
			var full = (int)Math.Log2( Math.Max( viewport.Z, viewport.W ) );
			mips = maxMips > 0 ? Math.Min( maxMips, full ) : full;
		}

		mips = Math.Min( mips, (int)Math.Log2( Math.Max( (int)viewport.Z, (int)viewport.W ) ) + 1 );
		var grab = context.GrabFrame( mips, Downsample, (int)method, name );
		targets[name] = grab;
		attributes.Set( name, grab );
	}

	/// <summary>The name a token stands for: the shaders' attribute and combo names, else the engine's token table.</summary>
	string Name( uint token ) => names.TryGetValue( token, out var name ) ? name : (string)typeof( StringToken ).GetMethod( "GetValue", BindingFlags.Static | BindingFlags.NonPublic ).Invoke( null, [token] ) ?? $"token{token:x}";

	static Dictionary<uint, string> Names( IEnumerable<ShaderFile> shaders )
	{
		var names = new Dictionary<uint, string>();
		foreach ( var shader in shaders )
		{
			foreach ( var type in (Basalt.Source2.Shaders.ShaderProgramType[])[Basalt.Source2.Shaders.ShaderProgramType.Features, Basalt.Source2.Shaders.ShaderProgramType.Vertex, Basalt.Source2.Shaders.ShaderProgramType.Pixel, Basalt.Source2.Shaders.ShaderProgramType.Compute] )
			{
				if ( shader[type] is not { } program ) continue;
				foreach ( var variable in program.Variables )
				{
					foreach ( var name in (string[])[variable.Name, variable.Attribute] )
					{
						if ( !string.IsNullOrEmpty( name ) ) names.TryAdd( new StringToken( name ).Value, name );
					}
				}

				foreach ( var combo in program.DynamicCombos ) names.TryAdd( new StringToken( combo.Name ).Value, combo.Name );
			}
		}

		return names;
	}

	/// <summary>
	/// The Basalt.Gpu twin of an engine buffer, with what Source 2's copy holds. The painter's box table gets its
	/// bindless texture and sampler indices moved to Basalt.Gpu's.
	/// </summary>
	BufferHandle Buffer( GpuBuffer buffer, bool boxes )
	{
		if ( buffers.TryGetValue( (buffer, boxes), out var handle ) ) return handle;

		var bytes = new byte[buffer.ElementCount * buffer.ElementSize];
		buffer.GetData<byte>( bytes );
		if ( boxes )
		{
			foreach ( ref var box in MemoryMarshal.Cast<byte, UICssBoxBatched.BoxInstance>( bytes.AsSpan() ) )
			{
				var text = (box.Flags & (1u << 14)) != 0;
				if ( !text && box.TextureIndex > 0 ) box.TextureIndex = Remap( textureIndices, box.TextureIndex, "texture" );
				if ( box.SamplerIndex > 0 ) box.SamplerIndex = Remap( samplerIndices, box.SamplerIndex, "sampler" );
				if ( box.BorderImageIndex > 0 ) box.BorderImageIndex = Remap( textureIndices, box.BorderImageIndex, "border image" );
				if ( box.BorderImageSamplerIndex > 0 ) box.BorderImageSamplerIndex = Remap( samplerIndices, box.BorderImageSamplerIndex, "border image sampler" );
				if ( box.TextMaskIndex > 0 ) box.TextMaskIndex = Remap( textureIndices, box.TextMaskIndex, "text mask" );
				if ( box.TextMaskSamplerIndex > 0 ) box.TextMaskSamplerIndex = Remap( samplerIndices, box.TextMaskSamplerIndex, "text mask sampler" );
			}
		}

		handle = gpu.CreateBuffer( new BufferInfo { Size = Math.Max( bytes.Length, 16 ), Usage = BufferUsage.Storage | BufferUsage.Index, Name = boxes ? "BoxInstances" : "Engine buffer" } );
		gpu.WriteBuffer( handle, bytes );
		ownedBuffers.Add( handle );
		buffers[(buffer, boxes)] = handle;
		return handle;
	}

	/// <summary>Bindless sampler indices the painter passes as ints.</summary>
	float Index( string name, int value ) =>
		value > 0 && (name.Equals( "SamplerIndex", StringComparison.OrdinalIgnoreCase ) || name.Equals( "BorderSamplerIndex", StringComparison.OrdinalIgnoreCase ))
			? Remap( samplerIndices, value, name ) : value;

	static int Remap( Dictionary<int, int> map, int index, string what ) =>
		map.TryGetValue( index, out var mapped ) ? mapped : throw new InvalidOperationException( $"No Basalt.Gpu {what} for Source 2's bindless index {index}" );

	/// <summary>
	/// An engine image's twin, from its pixels, with both of its bindless slots mapped: linear at Source 2's
	/// <see cref="Texture.Index"/>, sRGB one past it, on both devices.
	/// </summary>
	TextureHandle MapTexture( Texture texture )
	{
		if ( textures.TryGetValue( texture, out var handle ) ) return handle;
		if ( texture.IsRenderTarget ) throw new NotSupportedException( $"Render target {texture} bound as a texture" );

		var pixels = texture.GetPixels();
		handle = gpu.CreateTexture( TextureInfo.Texture2D( texture.Width, texture.Height, GpuFormat.RGBA8Unorm, name: $"Engine image {texture.Width}x{texture.Height}" ) );
		gpu.WriteTexture( handle, MemoryMarshal.AsBytes( pixels.AsSpan() ), 0, 0 );
		textures[texture] = handle;
		context.Track( handle, ImageLayout.ShaderRead );

		var linear = texture.Index;
		var srgb = g_pRenderDevice.GetTextureViewIndex( texture.native, 1, NativeEngine.RenderTextureDimension.RENDER_TEXTURE_DIMENSION_2D );
		textureIndices[linear] = gpu.BindlessIndex( handle );
		textureIndices[srgb] = gpu.BindlessIndex( handle ) + 1;
		if ( srgb != linear + 1 ) Notes.Add( $"Image {texture.Width}x{texture.Height}: Source 2's sRGB slot {srgb} isn't its linear slot {linear} + 1" );
		return handle;
	}

	/// <summary>Every sampler state the painter can ask for, by Source 2's bindless index, made on Basalt.Gpu.</summary>
	void MapSamplers()
	{
		foreach ( var filter in (FilterMode[])[FilterMode.Point, FilterMode.Bilinear, FilterMode.Trilinear, FilterMode.Anisotropic] )
		{
			foreach ( var u in Enum.GetValues<TextureAddressMode>() )
			{
				foreach ( var v in Enum.GetValues<TextureAddressMode>() )
				{
					var state = new SamplerState { Filter = filter, AddressModeU = u, AddressModeV = v };
					var index = SamplerState.GetBindlessIndex( state );
					if ( samplerIndices.ContainsKey( index ) ) continue;

					var info = filter switch
					{
						FilterMode.Point => SamplerInfo.Point(),
						FilterMode.Bilinear => SamplerInfo.Linear() with { MipFilter = Filter.Nearest },
						FilterMode.Anisotropic => SamplerInfo.Linear( anisotropy: state.MaxAnisotropy ),
						_ => SamplerInfo.Linear(),
					};
					info.AddressU = Address( u );
					info.AddressV = Address( v );
					info.AddressW = Address( state.AddressModeW );
					info.MaxLod = 15;
					var sampler = gpu.CreateSampler( info );
					ownedSamplers.Add( sampler );
					samplerIndices[index] = gpu.SamplerIndex( sampler );
				}
			}
		}
	}

	static AddressMode Address( TextureAddressMode mode ) => mode switch
	{
		TextureAddressMode.Mirror or TextureAddressMode.MirrorOnce => AddressMode.Mirror,
		TextureAddressMode.Clamp => AddressMode.Clamp,
		TextureAddressMode.Border => AddressMode.Border,
		_ => AddressMode.Wrap,
	};

	/// <summary>Destroy the twins (after the device has finished with them).</summary>
	public void Release()
	{
		foreach ( var buffer in ownedBuffers ) gpu.Destroy( buffer );
		foreach ( var texture in textures.Values ) gpu.Destroy( texture );
		foreach ( var sampler in ownedSamplers ) gpu.Destroy( sampler );
	}
}
