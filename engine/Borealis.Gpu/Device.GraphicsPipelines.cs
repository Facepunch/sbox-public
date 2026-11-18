using Borealis.Gpu.Vulkan;
using System.Buffers.Binary;
using System.Threading;

namespace Borealis.Gpu;

/// <summary>
/// The parts a graphics pipeline is linked from where the device has pipeline libraries
/// (<see cref="DeviceCapabilities.PipelineLibraries"/>), for <see cref="Device.PreparePipelineParts"/>.
/// </summary>
[Flags]
public enum PipelineParts
{
	/// <summary>No part.</summary>
	None = 0,

	/// <summary>Vertex buffers, attributes and topology.</summary>
	VertexInput = 1,

	/// <summary>The vertex shader, its constants, the binding layouts, wireframe and depth clamp.</summary>
	PreRasterization = 2,

	/// <summary>The pixel shader, its constants and the binding layouts.</summary>
	FragmentShader = 4,

	/// <summary>Target formats, blending, samples and alpha to coverage.</summary>
	FragmentOutput = 8,

	/// <summary>Every part.</summary>
	All = 15,
}

/// <summary>
/// One part of graphics pipelines (VK_EXT_graphics_pipeline_library), made once for every pipeline whose info agrees on
/// what the part reads. Its fields change under <c>Device.partLock</c>.
/// </summary>
internal sealed class PipelinePart( PartKey key, uint kind, ShaderHandle shader )
{
	public readonly PartKey Key = key;

	/// <summary>Which part (<c>VkGraphicsPipelineLibraryFlagBitsEXT</c>).</summary>
	public readonly uint Kind = kind;

	/// <summary>The shader a pre-rasterisation or fragment part was made from: it goes when the shader does.</summary>
	public readonly ShaderHandle Shader = shader;

	/// <summary>The library, once made.</summary>
	public ulong Pipeline;

	public bool Ready;
	public GpuException Failed;

	/// <summary>Pipelines linked from it that are alive, and links of it under way.</summary>
	public int Users;

	/// <summary>No longer in the cache (its shader was destroyed): destroyed when its last user goes.</summary>
	public bool Dropped;
}

/// <summary>The four parts a graphics pipeline was linked from.</summary>
internal sealed class LinkedParts( PipelinePart vertexInput, PipelinePart preRasterization, PipelinePart fragment, PipelinePart output, string name )
{
	public readonly PipelinePart VertexInput = vertexInput, PreRasterization = preRasterization, Fragment = fragment, Output = output;
	public readonly string Name = name;
}

/// <summary>The bytes of the pipeline info a part reads, compared whole.</summary>
internal sealed class PartKey : IEquatable<PartKey>
{
	readonly byte[] bytes;
	readonly int hash;

	public PartKey( ReadOnlySpan<byte> bytes )
	{
		this.bytes = bytes.ToArray();
		var hasher = new HashCode();
		hasher.AddBytes( bytes );
		hash = hasher.ToHashCode();
	}

	public bool Equals( PartKey other ) => other is not null && hash == other.hash && bytes.AsSpan().SequenceEqual( other.bytes );
	public override bool Equals( object obj ) => Equals( obj as PartKey );
	public override int GetHashCode() => hash;
}

/// <summary>Writes the fields a part reads into its key.</summary>
internal ref struct PartKeyWriter
{
	byte[] buffer;
	int length;

	public PartKeyWriter( uint kind )
	{
		buffer = new byte[128];
		Add( kind );
	}

	public void Add( uint value )
	{
		if ( length + 4 > buffer.Length ) Array.Resize( ref buffer, buffer.Length * 2 );
		BinaryPrimitives.WriteUInt32LittleEndian( buffer.AsSpan( length ), value );
		length += 4;
	}

	public void Add( int value ) => Add( (uint)value );
	public void Add( bool value ) => Add( value ? 1u : 0u );

	public void Add( ulong value )
	{
		Add( (uint)value );
		Add( (uint)(value >> 32) );
	}

	public void Add( ShaderHandle shader )
	{
		Add( shader.Index );
		Add( shader.Generation );
	}

	public void Add( string text )
	{
		Add( text?.Length ?? -1 );
		foreach ( var c in text ?? "" ) Add( (uint)c );
	}

	public void Add( SpecializationConstant[] constants )
	{
		Add( constants?.Length ?? 0 );
		foreach ( var c in constants ?? [] )
		{
			Add( c.Id );
			Add( c.Value );
		}
	}

	public readonly PartKey Key() => new( buffer.AsSpan( 0, length ) );
}

/// <summary>
/// A graphics pipeline's render state that's dynamic in every pipeline Borealis.Gpu makes, packed so a command list
/// compares it with what it last set in one step: cull mode, front face, depth bias enable, depth test, write and compare,
/// stencil test, ops per face and masks.
/// </summary>
internal static class RenderStateBits
{
	public const int Cull = 0, FrontFace = 2, DepthBias = 3, DepthTest = 4, DepthWrite = 5, DepthCompare = 6, StencilTest = 9;
	public const int StencilFront = 10, StencilBack = 22, ReadMask = 34, WriteMask = 42;

	public const ulong CullMask = 3ul << Cull, FrontFaceMask = 1ul << FrontFace, DepthBiasMask = 1ul << DepthBias;
	public const ulong DepthTestMask = 1ul << DepthTest, DepthWriteMask = 1ul << DepthWrite, DepthCompareMask = 7ul << DepthCompare;
	public const ulong StencilTestMask = 1ul << StencilTest, StencilFrontMask = 0xFFFul << StencilFront, StencilBackMask = 0xFFFul << StencilBack;
	public const ulong ReadMaskMask = 0xFFul << ReadMask, WriteMaskMask = 0xFFul << WriteMask;

	public static ulong Pack( in GraphicsPipelineInfo info ) =>
		(ulong)info.CullMode << Cull
		| (ulong)info.FrontFace << FrontFace
		| (info.DepthBias ? DepthBiasMask : 0)
		| (info.DepthTest ? DepthTestMask : 0)
		| (info.DepthWrite ? DepthWriteMask : 0)
		| (ulong)info.DepthCompare << DepthCompare
		| (info.StencilTest ? StencilTestMask : 0)
		| Face( info.StencilFront ) << StencilFront
		| Face( info.StencilBack ) << StencilBack
		| (ulong)info.StencilReadMask << ReadMask
		| (ulong)info.StencilWriteMask << WriteMask;

	static ulong Face( StencilFace face ) => (ulong)face.Fail | (ulong)face.Pass << 3 | (ulong)face.DepthFail << 6 | (ulong)face.Compare << 9;

	public static int Field( ulong state, int shift, int bits ) => (int)(state >> shift) & ((1 << bits) - 1);
}

public sealed unsafe partial class Device
{
	// Pipeline libraries: on when the device has fast-linking graphics pipeline libraries and the options allow them
	bool pipelineLibraries;

	// Parts by what they read, one cache per kind, and the lock their fields and the swap of an optimised link change under
	readonly object partLock = new();
	readonly Dictionary<PartKey, PipelinePart> vertexInputParts = new(), preRasterizationParts = new(), fragmentParts = new(), outputParts = new();

	// Optimised links waiting for the background threads, and how many are running
	readonly Queue<(PipelineHandle Handle, LinkedParts Parts, ulong Layout)> optimiseQueue = new();
	Thread[] optimisers;
	int optimising;
	bool stopOptimising;

	int partsMade, linksMade, optimisedMade;
	long partTicks, linkTicks, optimiseTicks;

	/// <summary>
	/// What pipeline libraries made so far (<see cref="DeviceCapabilities.PipelineLibraries"/>): parts and the
	/// milliseconds making them, fast links, optimised links made on the background threads, and links still waiting to be
	/// optimised. All zero without pipeline libraries.
	/// </summary>
	public PipelineLibraryStats PipelineLibraryCreation
	{
		get
		{
			int pending;
			lock ( optimiseQueue ) pending = optimiseQueue.Count + optimising;
			static double Ms( long ticks ) => Volatile.Read( ref ticks ) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			return new PipelineLibraryStats( Volatile.Read( ref partsMade ), Ms( partTicks ), Volatile.Read( ref linksMade ), Ms( linkTicks ), Volatile.Read( ref optimisedMade ), Ms( optimiseTicks ), pending );
		}
	}

	bool UsesLibraries( in GraphicsPipelineInfo info ) => pipelineLibraries && !info.MeshShader.IsValid && !shaderStatistics;

	/// <summary>
	/// Create a graphics pipeline, through the device's pipeline cache. With pipeline libraries
	/// (<see cref="DeviceCapabilities.PipelineLibraries"/>) it's a fast link of four parts, each made once for every pipeline
	/// that agrees on what it reads (<see cref="PreparePipelineParts"/> makes them ahead); an optimised link made on a
	/// background thread replaces it under the same handle once ready, and the fast one goes when the GPU is done with it.
	/// Every graphics pipeline takes its cull mode, front face, depth and stencil state as dynamic state, which the command
	/// list sets when it binds it.
	/// </summary>
	public PipelineHandle CreateGraphicsPipeline( in GraphicsPipelineInfo info )
	{
		var layout = PipelineLayout( info.Sets );
		if ( info.MeshShader.IsValid && !Capabilities.MeshShaders ) throw new InvalidOperationException( $"{info.Name}: the device has no mesh shaders" );

		ulong pipeline;
		LinkedParts parts = null;
		var started = System.Diagnostics.Stopwatch.GetTimestamp();
		using ( Zone( ZonePipeline ) )
		{
			if ( UsesLibraries( info ) )
			{
				parts = AcquireParts( info, layout );
				try
				{
					var linkStarted = System.Diagnostics.Stopwatch.GetTimestamp();
					pipeline = Link( parts, layout, optimise: false );
					Interlocked.Add( ref linkTicks, System.Diagnostics.Stopwatch.GetTimestamp() - linkStarted );
					Interlocked.Increment( ref linksMade );
				}
				catch
				{
					ReleaseParts( parts );
					throw;
				}
			}
			else
			{
				pipeline = MakeGraphics( info, PipelineParts.All, library: false, layout, null, 0, shaderStatistics ? CaptureStatistics : 0 );
			}
		}

		CountPipeline( started );
		SetName( VkObjectType.Pipeline, pipeline, info.Name );
		if ( shaderStatistics ) KeepStatistics( pipeline, info.Name );

		var (index, generation) = Pipelines.Add( new PipelineRecord
		{
			Pipeline = pipeline,
			Layout = layout,
			BindPoint = Vulkan.Vk.BindPointGraphics,
			RenderState = RenderStateBits.Pack( info ),
			Parts = parts,
		} );

		var handle = new PipelineHandle( index, generation );
		if ( parts is not null ) QueueOptimise( handle, parts, layout );
		return handle;
	}

	/// <summary>
	/// Make the parts of a graphics pipeline ahead of its first use (<see cref="PipelineParts"/>), on any thread, so creating
	/// it is only a fast link: a shader's pre-rasterisation and fragment parts as it loads, say. Parts already made or being
	/// made are skipped. Does nothing without pipeline libraries.
	/// </summary>
	public void PreparePipelineParts( in GraphicsPipelineInfo info, PipelineParts parts = PipelineParts.All )
	{
		if ( !UsesLibraries( info ) ) return;
		var layout = PipelineLayout( info.Sets );
		using ( Zone( ZonePipeline ) )
		{
			if ( (parts & PipelineParts.PreRasterization) != 0 ) Part( preRasterizationParts, Vulkan.Vk.LibraryPreRasterization, PreRasterizationKey( info, layout ), info, layout, info.VertexShader, use: false );
			if ( (parts & PipelineParts.FragmentShader) != 0 ) Part( fragmentParts, Vulkan.Vk.LibraryFragmentShader, FragmentKey( info, layout ), info, layout, info.FragmentShader, use: false );
			if ( (parts & PipelineParts.VertexInput) != 0 ) Part( vertexInputParts, Vulkan.Vk.LibraryVertexInput, VertexInputKey( info ), info, layout, default, use: false );
			if ( (parts & PipelineParts.FragmentOutput) != 0 ) Part( outputParts, Vulkan.Vk.LibraryFragmentOutput, OutputKey( info ), info, layout, default, use: false );
		}
	}

	/// <summary>
	/// Wait until every optimised link queued so far has replaced its fast one, or the timeout passes; false if it passed.
	/// For measurements and tests.
	/// </summary>
	public bool WaitForOptimisedPipelines( TimeSpan timeout )
	{
		var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
		lock ( optimiseQueue )
		{
			while ( optimiseQueue.Count + optimising > 0 )
			{
				var left = deadline - Environment.TickCount64;
				if ( left <= 0 ) return false;
				Monitor.Wait( optimiseQueue, (int)Math.Min( left, int.MaxValue ) );
			}
		}

		return true;
	}

	/// <summary>Whether a pipeline's optimised link has replaced its fast one, for the tests.</summary>
	internal bool IsOptimised( PipelineHandle pipeline )
	{
		lock ( partLock ) return Get( pipeline ).Optimised;
	}

	/// <summary>Parts alive (in the caches or still used by pipelines), for the tests.</summary>
	internal int PartCount
	{
		get
		{
			lock ( partLock ) return vertexInputParts.Count + preRasterizationParts.Count + fragmentParts.Count + outputParts.Count;
		}
	}

	LinkedParts AcquireParts( in GraphicsPipelineInfo info, ulong layout )
	{
		PipelinePart vertexInput = null, preRasterization = null, fragment = null, output = null;
		try
		{
			preRasterization = Part( preRasterizationParts, Vulkan.Vk.LibraryPreRasterization, PreRasterizationKey( info, layout ), info, layout, info.VertexShader, use: true );
			fragment = Part( fragmentParts, Vulkan.Vk.LibraryFragmentShader, FragmentKey( info, layout ), info, layout, info.FragmentShader, use: true );
			vertexInput = Part( vertexInputParts, Vulkan.Vk.LibraryVertexInput, VertexInputKey( info ), info, layout, default, use: true );
			output = Part( outputParts, Vulkan.Vk.LibraryFragmentOutput, OutputKey( info ), info, layout, default, use: true );
		}
		catch
		{
			lock ( partLock )
			{
				foreach ( var part in (ReadOnlySpan<PipelinePart>)[vertexInput, preRasterization, fragment, output] )
				{
					if ( part is not null ) Release( part );
				}
			}

			throw;
		}

		return new LinkedParts( vertexInput, preRasterization, fragment, output, info.Name );
	}

	/// <summary>
	/// A part by its key: made here if nobody has it, waited for if another thread is making it. With
	/// <paramref name="use"/>, counted as used until <see cref="Release"/>.
	/// </summary>
	PipelinePart Part( Dictionary<PartKey, PipelinePart> cache, uint kind, PartKey key, in GraphicsPipelineInfo info, ulong layout, ShaderHandle shader, bool use )
	{
		PipelinePart part;
		bool make;
		lock ( partLock )
		{
			make = !cache.TryGetValue( key, out part );
			if ( make ) cache[key] = part = new PipelinePart( key, kind, shader );
			if ( use ) part.Users++;
		}

		if ( make )
		{
			ulong made = 0;
			GpuException failed = null;
			var started = System.Diagnostics.Stopwatch.GetTimestamp();
			try
			{
				made = MakeGraphics( info, (PipelineParts)kind, library: true, layout, null, 0, 0 );
				SetName( VkObjectType.Pipeline, made, $"{info.Name} ({(PipelineParts)kind})" );
			}
			catch ( GpuException e )
			{
				failed = e;
			}

			Interlocked.Add( ref partTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started );
			Interlocked.Increment( ref partsMade );

			lock ( partLock )
			{
				part.Pipeline = made;
				part.Failed = failed;
				part.Ready = true;
				if ( failed is not null && cache.TryGetValue( key, out var cached ) && cached == part ) cache.Remove( key );
				else if ( part.Dropped && part.Users == 0 ) DestroyPart( part );
				Monitor.PulseAll( partLock );
			}
		}
		else
		{
			lock ( partLock )
			{
				while ( !part.Ready ) Monitor.Wait( partLock );
			}
		}

		if ( part.Failed is { } error )
		{
			if ( use )
			{
				lock ( partLock ) Release( part );
			}

			throw new GpuException( $"{info.Name} ({(PipelineParts)kind}): {error.Message}" );
		}

		return part;
	}

	PartKey VertexInputKey( in GraphicsPipelineInfo info )
	{
		var key = new PartKeyWriter( Vulkan.Vk.LibraryVertexInput );
		key.Add( (int)info.Topology );
		key.Add( info.VertexBuffers?.Length ?? 0 );
		foreach ( var buffer in info.VertexBuffers ?? [] )
		{
			key.Add( buffer.Stride );
			key.Add( (int)buffer.StepRate );
		}

		key.Add( info.Attributes?.Length ?? 0 );
		foreach ( var attribute in info.Attributes ?? [] )
		{
			key.Add( attribute.Location );
			key.Add( attribute.Buffer );
			key.Add( (int)attribute.Format );
			key.Add( attribute.Offset );
		}

		return key.Key();
	}

	static PartKey PreRasterizationKey( in GraphicsPipelineInfo info, ulong layout )
	{
		var key = new PartKeyWriter( Vulkan.Vk.LibraryPreRasterization );
		key.Add( info.VertexShader );
		key.Add( info.VertexEntry ?? "main" );
		key.Add( info.VertexConstants );
		key.Add( layout );
		key.Add( info.Wireframe );
		key.Add( info.DepthClamp );
		return key.Key();
	}

	PartKey FragmentKey( in GraphicsPipelineInfo info, ulong layout )
	{
		var key = new PartKeyWriter( Vulkan.Vk.LibraryFragmentShader );
		key.Add( info.FragmentShader );
		key.Add( info.FragmentShader.IsValid ? info.FragmentEntry ?? "main" : null );
		key.Add( info.FragmentShader.IsValid ? info.FragmentConstants : null );
		key.Add( layout );
		return key.Key();
	}

	PartKey OutputKey( in GraphicsPipelineInfo info )
	{
		var key = new PartKeyWriter( Vulkan.Vk.LibraryFragmentOutput );
		var colors = info.ColorFormats ?? [];
		key.Add( colors.Length );
		for ( int i = 0; i < colors.Length; i++ )
		{
			key.Add( (int)VkFormatOf( colors[i] ) );
			var blend = BlendOf( info, i );
			key.Add( blend.Enable );
			key.Add( (int)blend.SourceColor | (int)blend.DestinationColor << 8 | (int)blend.ColorOp << 16 );
			key.Add( (int)blend.SourceAlpha | (int)blend.DestinationAlpha << 8 | (int)blend.AlphaOp << 16 );
			key.Add( WriteMaskOf( blend ) );
		}

		key.Add( (int)VkFormatOf( info.DepthFormat ) );
		key.Add( Math.Max( info.Samples, 1 ) );
		key.Add( info.AlphaToCoverage );
		return key.Key();
	}

	static BlendState BlendOf( in GraphicsPipelineInfo info, int target ) => info.Blend is { Length: > 0 } ? info.Blend[Math.Min( target, info.Blend.Length - 1 )] : BlendState.Opaque;
	static uint WriteMaskOf( BlendState blend ) => blend.WriteMask == 0 ? Vulkan.Vk.ColorComponentAll : (uint)blend.WriteMask & Vulkan.Vk.ColorComponentAll;

	/// <summary>Link four parts into a pipeline: fast, or optimised as a whole (slow, but as fast on the GPU as one made whole).</summary>
	ulong Link( LinkedParts parts, ulong layout, bool optimise )
	{
		var libraries = stackalloc ulong[4] { parts.VertexInput.Pipeline, parts.PreRasterization.Pipeline, parts.Fragment.Pipeline, parts.Output.Pipeline };
		GraphicsPipelineInfo none = default;
		return MakeGraphics( none, PipelineParts.None, library: false, layout, libraries, 4, optimise ? Vulkan.Vk.PipelineCreateLinkTimeOptimization : 0 );
	}

	/// <summary>
	/// Make a graphics pipeline, or with <paramref name="library"/> the library of the <paramref name="parts"/> of one, from
	/// <paramref name="info"/> and any <paramref name="libraries"/> given.
	/// </summary>
	ulong MakeGraphics( in GraphicsPipelineInfo info, PipelineParts parts, bool library, ulong layout, ulong* libraries, int libraryCount, uint flags )
	{
		using var strings = new NativeStrings();
		var vertexInputPart = (parts & PipelineParts.VertexInput) != 0;
		var preRasterizationPart = (parts & PipelineParts.PreRasterization) != 0;
		var fragmentPart = (parts & PipelineParts.FragmentShader) != 0;
		var outputPart = (parts & PipelineParts.FragmentOutput) != 0;

		var stages = stackalloc VkPipelineShaderStageCreateInfo[3];
		var specializations = stackalloc VkSpecializationInfo[3];
		var stageCount = 0u;
		void Stage( uint stage, ShaderHandle shader, string entry, SpecializationConstant[] constants, VkPipelineShaderStageCreateInfo* stages, VkSpecializationInfo* specializations, NativeStrings strings )
		{
			stages[stageCount] = new VkPipelineShaderStageCreateInfo
			{
				sType = VkStructureType.PipelineShaderStageCreateInfo,
				stage = stage,
				module = Shaders.Get( shader.Index, shader.Generation ).Module,
				pName = strings.Add( entry ?? "main" ),
				pSpecializationInfo = Specialize( constants, &specializations[stageCount], strings ),
			};
			stageCount++;
		}

		// A mesh pipeline: task (if any) and mesh shaders, and no vertex input
		var meshPipeline = info.MeshShader.IsValid;
		if ( preRasterizationPart )
		{
			if ( meshPipeline && info.TaskShader.IsValid ) Stage( Vulkan.Vk.StageTask, info.TaskShader, info.TaskEntry, info.TaskConstants, stages, specializations, strings );
			if ( meshPipeline ) Stage( Vulkan.Vk.StageMesh, info.MeshShader, info.MeshEntry, info.MeshConstants, stages, specializations, strings );
			else Stage( Vulkan.Vk.StageVertex, info.VertexShader, info.VertexEntry, info.VertexConstants, stages, specializations, strings );
		}

		if ( fragmentPart && info.FragmentShader.IsValid ) Stage( Vulkan.Vk.StageFragment, info.FragmentShader, info.FragmentEntry, info.FragmentConstants, stages, specializations, strings );

		var buffers = info.VertexBuffers ?? [];
		var attributes = info.Attributes ?? [];
		var vkBuffers = stackalloc VkVertexInputBindingDescription[Math.Max( buffers.Length, 1 )];
		var vkAttributes = stackalloc VkVertexInputAttributeDescription[Math.Max( attributes.Length, 1 )];
		for ( int i = 0; i < buffers.Length; i++ ) vkBuffers[i] = new VkVertexInputBindingDescription { binding = (uint)i, stride = (uint)buffers[i].Stride, inputRate = (int)buffers[i].StepRate };
		for ( int i = 0; i < attributes.Length; i++ )
		{
			vkAttributes[i] = new VkVertexInputAttributeDescription { location = (uint)attributes[i].Location, binding = (uint)attributes[i].Buffer, format = attributes[i].Format.ToVk(), offset = (uint)attributes[i].Offset };
		}

		var vertexInput = new VkPipelineVertexInputStateCreateInfo
		{
			sType = VkStructureType.PipelineVertexInputStateCreateInfo,
			vertexBindingDescriptionCount = (uint)buffers.Length,
			pVertexBindingDescriptions = vkBuffers,
			vertexAttributeDescriptionCount = (uint)attributes.Length,
			pVertexAttributeDescriptions = vkAttributes,
		};

		var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo { sType = VkStructureType.PipelineInputAssemblyStateCreateInfo, topology = (int)info.Topology };
		var viewport = new VkPipelineViewportStateCreateInfo { sType = VkStructureType.PipelineViewportStateCreateInfo, viewportCount = 1, scissorCount = 1 };

		// Cull mode, front face and depth bias enable are dynamic: these are what a pipeline made whole starts with
		var raster = new VkPipelineRasterizationStateCreateInfo
		{
			sType = VkStructureType.PipelineRasterizationStateCreateInfo,
			depthClampEnable = info.DepthClamp ? 1u : 0u,
			polygonMode = info.Wireframe ? 1 : 0,
			cullMode = (uint)info.CullMode,
			frontFace = (int)info.FrontFace,
			depthBiasEnable = info.DepthBias ? 1u : 0u,
			lineWidth = 1,
		};

		var multisample = new VkPipelineMultisampleStateCreateInfo
		{
			sType = VkStructureType.PipelineMultisampleStateCreateInfo,
			rasterizationSamples = (uint)Math.Max( info.Samples, 1 ),
			alphaToCoverageEnable = info.AlphaToCoverage ? 1u : 0u,
		};

		static VkStencilOpState Stencil( StencilFace face, byte read, byte write ) => new()
		{
			failOp = (int)face.Fail,
			passOp = (int)face.Pass,
			depthFailOp = (int)face.DepthFail,
			compareOp = (int)face.Compare,
			compareMask = read,
			writeMask = write,
		};

		// Dynamic too, but for depth bounds (never tested)
		var depthStencil = new VkPipelineDepthStencilStateCreateInfo
		{
			sType = VkStructureType.PipelineDepthStencilStateCreateInfo,
			depthTestEnable = info.DepthTest ? 1u : 0u,
			depthWriteEnable = info.DepthWrite ? 1u : 0u,
			depthCompareOp = (int)info.DepthCompare,
			stencilTestEnable = info.StencilTest ? 1u : 0u,
			front = Stencil( info.StencilFront, info.StencilReadMask, info.StencilWriteMask ),
			back = Stencil( info.StencilBack, info.StencilReadMask, info.StencilWriteMask ),
		};

		var colorFormats = info.ColorFormats ?? [];
		var vkColorFormats = stackalloc VkFormat[Math.Max( colorFormats.Length, 1 )];
		var blends = stackalloc VkPipelineColorBlendAttachmentState[Math.Max( colorFormats.Length, 1 )];
		for ( int i = 0; i < colorFormats.Length; i++ )
		{
			vkColorFormats[i] = VkFormatOf( colorFormats[i] );
			var blend = BlendOf( info, i );
			blends[i] = new VkPipelineColorBlendAttachmentState
			{
				blendEnable = blend.Enable ? 1u : 0u,
				srcColorBlendFactor = (int)blend.SourceColor,
				dstColorBlendFactor = (int)blend.DestinationColor,
				colorBlendOp = (int)blend.ColorOp,
				srcAlphaBlendFactor = (int)blend.SourceAlpha,
				dstAlphaBlendFactor = (int)blend.DestinationAlpha,
				alphaBlendOp = (int)blend.AlphaOp,
				colorWriteMask = WriteMaskOf( blend ),
			};
		}

		var colorBlend = new VkPipelineColorBlendStateCreateInfo
		{
			sType = VkStructureType.PipelineColorBlendStateCreateInfo,
			attachmentCount = (uint)colorFormats.Length,
			pAttachments = blends,
		};

		// The state each part owns that a command list sets (RenderStateBits), viewport and scissor, depth bias and the
		// stencil reference
		var dynamicStates = stackalloc int[14];
		var dynamicCount = 0u;
		if ( preRasterizationPart )
		{
			dynamicStates[dynamicCount++] = Vulkan.Vk.DynamicViewport;
			dynamicStates[dynamicCount++] = Vulkan.Vk.DynamicScissor;
			dynamicStates[dynamicCount++] = Vulkan.Vk.DynamicDepthBias;
			dynamicStates[dynamicCount++] = Vulkan.Vk.DynamicCullMode;
			dynamicStates[dynamicCount++] = Vulkan.Vk.DynamicFrontFace;
			dynamicStates[dynamicCount++] = Vulkan.Vk.DynamicDepthBiasEnable;
		}

		if ( fragmentPart )
		{
			dynamicStates[dynamicCount++] = Vulkan.Vk.DynamicDepthTestEnable;
			dynamicStates[dynamicCount++] = Vulkan.Vk.DynamicDepthWriteEnable;
			dynamicStates[dynamicCount++] = Vulkan.Vk.DynamicDepthCompareOp;
			dynamicStates[dynamicCount++] = Vulkan.Vk.DynamicStencilTestEnable;
			dynamicStates[dynamicCount++] = Vulkan.Vk.DynamicStencilOp;
			dynamicStates[dynamicCount++] = Vulkan.Vk.DynamicStencilCompareMask;
			dynamicStates[dynamicCount++] = Vulkan.Vk.DynamicStencilWriteMask;
			dynamicStates[dynamicCount++] = Vulkan.Vk.DynamicStencilReference;
		}

		var dynamic = new VkPipelineDynamicStateCreateInfo { sType = VkStructureType.PipelineDynamicStateCreateInfo, dynamicStateCount = dynamicCount, pDynamicStates = dynamicStates };

		// Formats belong to the output part; the shader parts render to whatever it says
		var depthFormat = outputPart ? VkFormatOf( info.DepthFormat ) : VkFormat.Undefined;
		var rendering = new VkPipelineRenderingCreateInfo
		{
			sType = VkStructureType.PipelineRenderingCreateInfo,
			colorAttachmentCount = outputPart ? (uint)colorFormats.Length : 0,
			pColorAttachmentFormats = vkColorFormats,
			depthAttachmentFormat = depthFormat,
			stencilAttachmentFormat = outputPart && info.DepthFormat.HasStencil() ? depthFormat : VkFormat.Undefined,
		};

		var libraryParts = new VkGraphicsPipelineLibraryCreateInfoEXT { sType = VkStructureType.GraphicsPipelineLibraryCreateInfoExt, flags = (uint)parts };
		var linked = new VkPipelineLibraryCreateInfoKHR { sType = VkStructureType.PipelineLibraryCreateInfoKhr, libraryCount = (uint)libraryCount, pLibraries = libraries };
		void* next = null;
		if ( parts != PipelineParts.None )
		{
			rendering.pNext = next;
			next = &rendering;
		}

		if ( library )
		{
			libraryParts.pNext = next;
			next = &libraryParts;
			flags |= Vulkan.Vk.PipelineCreateLibrary | Vulkan.Vk.PipelineCreateRetainLinkTimeOptimizationInfo;
		}

		if ( libraryCount > 0 )
		{
			linked.pNext = next;
			next = &linked;
		}

		var create = new VkGraphicsPipelineCreateInfo
		{
			sType = VkStructureType.GraphicsPipelineCreateInfo,
			pNext = next,
			stageCount = stageCount,
			pStages = stageCount > 0 ? stages : null,
			pVertexInputState = vertexInputPart && !meshPipeline ? &vertexInput : null,
			pInputAssemblyState = vertexInputPart && !meshPipeline ? &inputAssembly : null,
			pViewportState = preRasterizationPart ? &viewport : null,
			pRasterizationState = preRasterizationPart ? &raster : null,
			pMultisampleState = outputPart ? &multisample : null,
			pDepthStencilState = fragmentPart ? &depthStencil : null,
			pColorBlendState = outputPart ? &colorBlend : null,
			pDynamicState = dynamicCount > 0 ? &dynamic : null,
			layout = preRasterizationPart || fragmentPart || libraryCount > 0 ? layout : 0,
			basePipelineIndex = -1,
			flags = flags,
		};

		ulong pipeline;
		Vk.vkCreateGraphicsPipelines( Vk.Device, pipelineCache, 1, &create, null, &pipeline ).Check( $"vkCreateGraphicsPipelines ({info.Name ?? "link"})" );
		return pipeline;
	}

	//
	// Optimised links, made on background threads and swapped in
	//

	/// <summary>Whether fast links are replaced by optimised ones; <c>BOREALIS_OPTIMISED_LINKS=0</c> keeps the fast ones, to compare.</summary>
	static readonly bool optimiseLinks = Environment.GetEnvironmentVariable( "BOREALIS_OPTIMISED_LINKS" ) != "0";

	void QueueOptimise( PipelineHandle handle, LinkedParts parts, ulong layout )
	{
		if ( !optimiseLinks ) return;
		lock ( partLock ) Use( parts );
		lock ( optimiseQueue )
		{
			if ( stopOptimising )
			{
				lock ( partLock ) ReleaseParts( parts );
				return;
			}

			optimiseQueue.Enqueue( (handle, parts, layout) );
			if ( optimisers is null )
			{
				// Few, and below normal priority: they compete with the work making first frames, not the frame
				optimisers = new Thread[Math.Clamp( Environment.ProcessorCount / 4, 1, 4 )];
				for ( int i = 0; i < optimisers.Length; i++ )
				{
					optimisers[i] = new Thread( Optimise ) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = $"Borealis.Gpu optimised links {i}" };
					optimisers[i].Start();
				}
			}

			Monitor.Pulse( optimiseQueue );
		}
	}

	void Optimise()
	{
		while ( true )
		{
			(PipelineHandle Handle, LinkedParts Parts, ulong Layout) job;
			lock ( optimiseQueue )
			{
				while ( optimiseQueue.Count == 0 && !stopOptimising ) Monitor.Wait( optimiseQueue );
				if ( stopOptimising ) return;
				job = optimiseQueue.Dequeue();
				optimising++;
			}

			ulong made = 0;
			var started = System.Diagnostics.Stopwatch.GetTimestamp();
			try
			{
				using ( Zone( ZonePipeline ) ) made = Link( job.Parts, job.Layout, optimise: true );
				SetName( VkObjectType.Pipeline, made, job.Parts.Name );
			}
			catch ( GpuException )
			{
				// The fast link stays
			}

			Interlocked.Add( ref optimiseTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started );

			lock ( partLock )
			{
				if ( made != 0 && Pipelines.IsAlive( job.Handle.Index, job.Handle.Generation ) )
				{
					// Lists recording now may have bound the fast one: it goes with this frame's garbage
					ref var record = ref Pipelines.Get( job.Handle.Index, job.Handle.Generation );
					var fast = record.Pipeline;
					Volatile.Write( ref record.Pipeline, made );
					record.Optimised = true;
					Defer( () => Vk.vkDestroyPipeline( Vk.Device, fast, null ) );
					Interlocked.Increment( ref optimisedMade );
				}
				else if ( made != 0 )
				{
					// Destroyed while it was made: nothing ever bound this one
					Vk.vkDestroyPipeline( Vk.Device, made, null );
				}

				ReleaseParts( job.Parts );
			}

			lock ( optimiseQueue )
			{
				optimising--;
				Monitor.PulseAll( optimiseQueue );
			}
		}
	}

	/// <summary>Stop the background threads, finishing the links they're making; links still queued aren't made.</summary>
	void StopOptimising()
	{
		Thread[] threads;
		lock ( optimiseQueue )
		{
			stopOptimising = true;
			threads = optimisers;
			Monitor.PulseAll( optimiseQueue );
		}

		foreach ( var thread in threads ?? [] ) thread.Join();

		lock ( optimiseQueue )
		{
			lock ( partLock )
			{
				while ( optimiseQueue.TryDequeue( out var job ) ) ReleaseParts( job.Parts );
			}

			optimising = 0;
			Monitor.PulseAll( optimiseQueue );
		}
	}

	//
	// Part lifetimes: a part goes once its shader has and no pipeline uses it. Under the parts' lock
	//

	void Use( LinkedParts parts )
	{
		parts.VertexInput.Users++;
		parts.PreRasterization.Users++;
		parts.Fragment.Users++;
		parts.Output.Users++;
	}

	void ReleaseParts( LinkedParts parts )
	{
		lock ( partLock )
		{
			Release( parts.VertexInput );
			Release( parts.PreRasterization );
			Release( parts.Fragment );
			Release( parts.Output );
		}
	}

	void Release( PipelinePart part )
	{
		part.Users--;
		if ( part.Dropped && part.Users == 0 && part.Ready ) DestroyPart( part );
	}

	void DestroyPart( PipelinePart part )
	{
		if ( part.Pipeline != 0 ) Vk.vkDestroyPipeline( Vk.Device, part.Pipeline, null );
		part.Pipeline = 0;
	}

	/// <summary>Forget the parts made from a destroyed shader; each goes when the pipelines linked from it have.</summary>
	void DropParts( ShaderHandle shader )
	{
		if ( !pipelineLibraries ) return;
		lock ( partLock )
		{
			Drop( preRasterizationParts, shader );
			Drop( fragmentParts, shader );
		}
	}

	void Drop( Dictionary<PartKey, PipelinePart> cache, ShaderHandle shader )
	{
		List<PipelinePart> dropped = null;
		foreach ( var part in cache.Values )
		{
			if ( part.Shader == shader ) (dropped ??= new()).Add( part );
		}

		foreach ( var part in dropped ?? [] )
		{
			cache.Remove( part.Key );
			part.Dropped = true;
			if ( part.Users == 0 && part.Ready ) DestroyPart( part );
		}
	}

	/// <summary>At shutdown, after the pipelines: every part still alive.</summary>
	void DestroyParts( List<PipelineRecord> pipelines )
	{
		StopOptimising();
		var alive = new HashSet<PipelinePart>();
		foreach ( var cache in (ReadOnlySpan<Dictionary<PartKey, PipelinePart>>)[vertexInputParts, preRasterizationParts, fragmentParts, outputParts] )
		{
			alive.UnionWith( cache.Values );
			cache.Clear();
		}

		foreach ( var record in pipelines )
		{
			if ( record.Parts is not { } parts ) continue;
			alive.Add( parts.VertexInput );
			alive.Add( parts.PreRasterization );
			alive.Add( parts.Fragment );
			alive.Add( parts.Output );
		}

		foreach ( var part in alive ) DestroyPart( part );
	}
}

/// <summary>What pipeline libraries made (<see cref="Device.PipelineLibraryCreation"/>).</summary>
public readonly record struct PipelineLibraryStats( int Parts, double PartMs, int Links, double LinkMs, int Optimised, double OptimiseMs, int Pending );
