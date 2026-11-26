using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Borealis.Backend;

/// <summary>A texture's single mip, as <see cref="ShaderParameters.Set(StringToken, GpuTexture, int)"/> names one.</summary>
internal sealed record TextureMip( GpuTexture Texture, int Mip );

/// <summary>
/// Compute dispatches with parameters of their own (<see cref="GpuContext.Dispatch(GpuMaterial, ShaderParameters, int, int, int)"/>):
/// what Borealis's shared passes run, the depth chain among them. Each binds through this context's own <see cref="ShaderDrawer"/>,
/// with material objects (persistent sets, as the engine's dispatches bind): the parameters over this context's
/// attributes, found by the tokens and attribute ids worked out once a variant (<see cref="DispatchPlan"/>), single mips as
/// views of one mip, and the textures it writes moved to <see cref="ImageLayout.General"/> (a mip it reads of a texture it writes moved to
/// <see cref="ImageLayout.ShaderRead"/> for the dispatch alone), the textures it only reads to
/// <see cref="ImageLayout.ShaderRead"/>.
/// </summary>
internal sealed partial class BackendContext
{
	/// <summary>
	/// The drawer parameterised dispatches bind with, this context's own: contexts recording on several threads at once
	/// (the depth chain beside a frame buffer copy's mips) don't wait for each other's binds.
	/// </summary>
	ShaderDrawer dispatchDrawer;

	/// <summary>Single-mip views made for parameterised dispatches, one a texture and mip.</summary>
	static readonly ConditionalWeakTable<BackendTexture, Dictionary<int, TextureViewHandle>> mipViews = new();

	// One dispatch's attributes and the textures it writes and reads, reused from dispatch to dispatch
	ShaderAttributes dispatchAttributes;
	readonly HashSet<BackendTexture> dispatchWritten = new(), dispatchSampled = new();
	readonly List<(BackendTexture Texture, int Mip)> dispatchSampledMips = new();

	public override void Dispatch( GpuMaterial computeShader, ShaderParameters parameters, int x, int y, int z )
	{
		if ( recording is null || computeShader is not BackendMaterial { Shader: { } shader } ) return;
		if ( shader[ShaderProgramType.Compute] is null || shader.Modes.Count == 0 ) return;
		var given = parameters as BackendParameters;

		object Value( StringToken token ) => given?.Get( token ) ?? Lookup( token );

		var drawer = dispatchDrawer ??= new ShaderDrawer( device );

		// Kept on the context and emptied for each dispatch: dispatches allocate nothing in steady state
		var attributes = dispatchAttributes ??= new ShaderAttributes();
		attributes.Clear();
		foreach ( var (id, token) in dispatchCombos.GetValue( shader, static s => [.. s[ShaderProgramType.Compute].DynamicCombos.Select( c => (AttributeIds.Of( c.Name ), Token( c.Name )) )] ) )
		{
			if ( (given?.GetCombo( token ) ?? LookupCombo( token )) is int value ) attributes.SetComboById( id, value );
		}

		var variant = drawer.Variant( shader, attributes );
		if ( variant?.Compute?.Bytecode is null ) return;

		var plan = dispatchPlans.GetValue( variant, DispatchPlan.Of );
		Span<float> floats = stackalloc float[16];
		foreach ( var (token, id) in plan.Constants )
		{
			var count = Components( Value( token ), floats );
			if ( count != 0 ) SetDispatchFloats( attributes, id, floats[..count] );
		}

		CloseScope();
		var written = dispatchWritten;
		var sampled = dispatchSampled;
		var sampledMips = dispatchSampledMips;
		written.Clear();
		sampled.Clear();
		sampledMips.Clear();
		foreach ( var (index, token, id) in plan.Resources )
		{
			ref readonly var resource = ref variant.ResourceSpan[index];
			var storage = resource.Kind == ShaderResourceKind.StorageTexture;
			switch ( Value( token ) )
			{
				case TextureMip { Texture: BackendTexture texture, Mip: var mip }:
					attributes.SetById( id, MipView( texture, mip ) );
					if ( storage ) written.Add( texture );
					else sampledMips.Add( (texture, mip) );
					break;

				case BackendTexture texture when resource.Kind is ShaderResourceKind.Texture or ShaderResourceKind.StorageTexture:
					attributes.SetById( id, texture.Handle );
					if ( storage ) written.Add( texture );
					else sampled.Add( texture );
					break;

				case GpuBuffer { Resource: BackendBuffer buffer }:
					attributes.SetById( id, buffer.Handle );
					break;

				case byte[] data when resource.Kind == ShaderResourceKind.ConstantBuffer:
					attributes.SetById( id, (object)data );
					break;
			}
		}

		// Writes in General; reads in ShaderRead, a read mip of a written texture only for this dispatch
		foreach ( var texture in written ) Transition( texture, ImageLayout.General );
		foreach ( var texture in sampled ) if ( !written.Contains( texture ) ) Transition( texture, ImageLayout.ShaderRead );
		foreach ( var (texture, mip) in sampledMips )
		{
			if ( written.Contains( texture ) ) List.Barrier( MipBarrier( texture.Handle, mip, ImageLayout.General, ImageLayout.ShaderRead ) );
			else Transition( texture, ImageLayout.ShaderRead );
		}

		drawer.Dispatch( List, variant, attributes, x, y, z );
		recording.Pipeline = default;
		recording.Binder = null;

		foreach ( var (texture, mip) in sampledMips )
		{
			if ( written.Contains( texture ) ) List.Barrier( MipBarrier( texture.Handle, mip, ImageLayout.ShaderRead, ImageLayout.General ) );
		}
	}

	/// <summary>
	/// What a parameterised dispatch of a variant reads, found once a variant: the material constants it takes from attributes
	/// and the resources it binds by name, each as the token its parameters are found by and the attribute id it's set under.
	/// </summary>
	sealed class DispatchPlan
	{
		public (StringToken Token, int Id)[] Constants;
		public (int Index, StringToken Token, int Id)[] Resources;

		public static DispatchPlan Of( ShaderVariant variant )
		{
			var constants = new List<(StringToken, int)>();
			foreach ( var (variable, _) in variant.Compute.MaterialConstants )
			{
				if ( variable.Source != VariableSource.Attribute || string.IsNullOrEmpty( variable.Attribute ) ) continue;
				constants.Add( (Token( variable.Attribute ), AttributeIds.Of( variable.Attribute )) );
			}

			var resources = new List<(int, StringToken, int)>();
			var span = variant.ResourceSpan;
			for ( int i = 0; i < span.Length; i++ )
			{
				if ( span[i].Name is not { } name ) continue;
				// An append buffer's hidden counter is set under its counter's name, where the drawer looks for it
				if ( span[i].Kind == ShaderResourceKind.Counter ) name = ShaderResource.CounterName( name );
				resources.Add( (i, Token( name ), AttributeIds.Of( name )) );
			}

			return new DispatchPlan { Constants = [.. constants], Resources = [.. resources] };
		}
	}

	static readonly ConditionalWeakTable<ShaderVariant, DispatchPlan> dispatchPlans = new();
	static readonly ConditionalWeakTable<ShaderFile, (int Id, StringToken Token)[]> dispatchCombos = new();

	// Arrays for constants of more than four floats (matrices), one an attribute id, refilled each dispatch
	readonly Dictionary<int, float[]> dispatchFloats = new();

	/// <summary>
	/// A parameter's value as floats under an attribute id: one to four in the arrays the attributes own, more in an array this
	/// context keeps for the id, so a dispatch allocates nothing in steady state.
	/// </summary>
	void SetDispatchFloats( ShaderAttributes into, int id, ReadOnlySpan<float> values )
	{
		if ( values.Length <= 4 )
		{
			into.SetFloatsById( id, values );
			return;
		}

		if ( !dispatchFloats.TryGetValue( id, out var kept ) || kept.Length != values.Length ) dispatchFloats[id] = kept = new float[values.Length];
		values.CopyTo( kept );
		into.SetById( id, (object)kept );
	}

	static readonly StringToken MipLevel0 = new( "MipLevel0" ), MipLevel1 = new( "MipLevel1" ), TextureSize = new( "TextureSize" ), InvTextureSize = new( "InvTextureSize" ), DownsampleMethod = new( "D_DOWNSAMPLE_METHOD" );
	static readonly StringToken MipRegion = new( "MipRegion" );
	static readonly StringToken MaskOutput = new( "Output" ), MaskMip = new( "Mip" ), MaskSource = new( "MaskSource" ), MaskRegion = new( "MaskRegion" ), MaskCopies = new( "D_COPY" ), MaskSamples = new( "MaskSamples" );

	/// <summary>The frame buffer copy's mask, copy and downsample parameters, kept between frames.</summary>
	readonly BackendParameters maskParameters = new(), copyParameters = new(), mipParameters = new(), tailMipParameters = new();

	/// <summary>
	/// The frame buffer copy made by its depth-aware mask (<c>framecopy_mask_cs</c>, <c>depthaware_mask_cs</c> with the copy): one pass writes the
	/// last frame's blurred copy where the mask takes it and the frame everywhere else, so the frame is read only where it's
	/// kept and the copy written once. The same texels as a copy then the mask; a multisampled frame is resolved there, its
	/// samples averaged as a resolve averages them, so most of its samples (where the mask takes last frame's) aren't read.
	/// A frame of another format (converted by the copy) isn't made this way. False when it wasn't.
	/// </summary>
	bool CopyFrameBufferMasked( BackendTexture source, BackendTexture copy, Rect viewport )
	{
		if ( !device.FrameBufferCopyMasked || source is null || source.Format != copy.Format ) return false;
		if ( source.Width < copy.Width || source.Height < copy.Height ) return false;
		return MaskFrameBufferCopy( copy, viewport, source );
	}

	/// <summary>
	/// Native's depth-aware mask over the frame buffer copy (<c>CSceneSystem::DownsampleTexture</c> runs it before the mips):
	/// <c>depthaware_mask_cs</c> writes, where something is nearer than the glass in <c>RefractionDepthTexture</c>, the last
	/// frame's blurred copy reprojected, so nearer objects stay out of the refraction. With <paramref name="source"/>,
	/// <c>framecopy_mask_cs</c> does, and copies the frame everywhere else, over the readers' region. False when the shader
	/// isn't there.
	/// </summary>
	bool MaskFrameBufferCopy( BackendTexture copy, Rect viewport, BackendTexture source )
	{
		var shader = source is null ? "shaders/depthaware_mask_cs.shader" : "shaders/framecopy_mask_cs.shader";
		if ( device.MaterialFromShader( shader ) is not BackendMaterial { Shader: not null } mask ) return false;

		var scope = BeginGpuScope( "Frame Buffer Copy Mask" );
		var parameters = source is null ? maskParameters : copyParameters;
		parameters.Set( MaskOutput, copy, 0 );
		parameters.Set( MaskMip, copy, 1 );
		if ( source is null )
		{
			Dispatch( mask, parameters, copy.Width, copy.Height, 1 );
		}
		else
		{
			var (x0, y0, x1, y1) = ReadRegion( Math.Min( copy.Width, (int)viewport.Width ), Math.Min( copy.Height, (int)viewport.Height ) );
			parameters.Set( MaskRegion, new Vector4( x0, y0, x1, y1 ) );
			var samples = source.Samples;
			parameters.SetCombo( MaskCopies, samples switch { 1 => 1, 4 => 3, _ => 2 } );
			if ( samples > 1 ) parameters.Set( MaskSource, source );
			else parameters.Set( MaskSource, source, 0 );
			if ( samples > 1 && samples != 4 ) parameters.Set( MaskSamples, (float)samples );
			Dispatch( mask, parameters, x1 - x0, y1 - y0, 1 );
		}

		List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
		EndGpuScope( scope );
		return true;
	}

	/// <summary>How far past its readers' area the copy is made: the reach of their blurred reads of its first few mips.</summary>
	const int ReadMargin = 64;

	/// <summary>How far past the copy's region each of its mips is made, in that mip's texels: its gaussian's reach into the mip above.</summary>
	const int MipMargin = 8;

	/// <summary>
	/// The pixels of a <paramref name="width"/> by <paramref name="height"/> copy its readers read (<see cref="GpuContext.FrameBufferReadArea"/>,
	/// widened by <see cref="ReadMargin"/>), or all of them (<see cref="BackendDevice.FrameBufferCopyRegions"/>).
	/// </summary>
	(int X0, int Y0, int X1, int Y1) ReadRegion( int width, int height )
	{
		var area = FrameBufferReadArea;
		if ( !device.FrameBufferCopyRegions || area.Whole || area.IsEmpty ) return (0, 0, width, height);

		// Normalized device coordinates are y up, the copy's rows down
		var x0 = (int)MathF.Floor( (area.Min.x * 0.5f + 0.5f) * width ) - ReadMargin;
		var x1 = (int)MathF.Ceiling( (area.Max.x * 0.5f + 0.5f) * width ) + ReadMargin;
		var y0 = (int)MathF.Floor( (0.5f - area.Max.y * 0.5f) * height ) - ReadMargin;
		var y1 = (int)MathF.Ceiling( (0.5f - area.Min.y * 0.5f) * height ) + ReadMargin;
		return (Math.Clamp( x0, 0, width ), Math.Clamp( y0, 0, height ), Math.Clamp( x1, 0, width ), Math.Clamp( y1, 0, height ));
	}

	/// <summary>
	/// The frame buffer copy's mips, as native's <c>CSceneSystem::DownsampleTexture</c> makes them after the copy and its
	/// mask: a gaussian dispatch a mip, each from the one above, over the viewport's part. <c>framecopy_mips_cs</c> makes
	/// <c>downsample_cs</c>'s gaussian mip a 16x16 tile a group, so each texel takes 2.25 taps of the mip above, not 4
	/// (<see cref="BackendDevice.FrameBufferCopyTiledMips"/>). A copy made only around its readers (<see cref="ReadRegion"/>)
	/// gets its mips there too, each widened by <see cref="MipMargin"/> of its own texels; the rest keep an earlier frame's.
	/// The mips from the first of <see cref="TailSize"/> square or under are made whole, in one group (<c>framecopy_tail_cs</c>),
	/// not a dispatch and a barrier each.
	/// </summary>
	void GenerateFrameBufferCopyMips( BackendTexture copy, Rect viewport )
	{
		if ( copy.Mips <= 1 ) return;
		var tiled = device.FrameBufferCopyTiledMips && device.MaterialFromShader( "shaders/framecopy_mips_cs.shader" ) is BackendMaterial { Shader: not null } tiles ? tiles : null;
		var downsample = tiled ?? device.MaterialFromShader( "shaders/downsample_cs.shader" ) as BackendMaterial;
		if ( downsample is not { Shader: not null } ) return;

		// Around the copy's region; downsample_cs makes a mip whole
		var (rx0, ry0, rx1, ry1) = ReadRegion( Math.Min( copy.Width, (int)viewport.Width ), Math.Min( copy.Height, (int)viewport.Height ) );
		if ( tiled is null ) (rx0, ry0, rx1, ry1) = (0, 0, (int)viewport.Width, (int)viewport.Height);

		var parameters = mipParameters;
		parameters.SetCombo( DownsampleMethod, 1 );

		// The mips from the first of 32x32 or under in one group (framecopy_tail_cs), when it's there
		var tail = tiled is not null && device.MaterialFromShader( "shaders/framecopy_tail_cs.shader" ) is BackendMaterial { Shader: not null } last ? last : null;
		var tailFrom = copy.Mips;
		if ( tail is not null )
		{
			tailFrom = 1;
			while ( tailFrom < copy.Mips && ((int)viewport.Width >> tailFrom > TailSize || (int)viewport.Height >> tailFrom > TailSize) ) tailFrom++;
		}

		for ( int i = 0; i < tailFrom - 1; i++ )
		{
			var width = Math.Max( 1, (int)viewport.Width >> (i + 1) );
			var height = Math.Max( 1, (int)viewport.Height >> (i + 1) );
			parameters.Set( MipLevel0, copy, i );
			parameters.Set( MipLevel1, copy, i + 1 );
			parameters.Set( TextureSize, new Vector2( width, height ) );
			parameters.Set( InvTextureSize, new Vector2( 1.0f / width, 1.0f / height ) );

			// The region's texels at this mip, widened by the blur's reach
			var shift = i + 1;
			var x0 = Math.Max( 0, (rx0 >> shift) - MipMargin );
			var y0 = Math.Max( 0, (ry0 >> shift) - MipMargin );
			var x1 = Math.Min( width, ((rx1 + (1 << shift) - 1) >> shift) + MipMargin );
			var y1 = Math.Min( height, ((ry1 + (1 << shift) - 1) >> shift) + MipMargin );
			if ( x1 <= x0 || y1 <= y0 ) continue;
			parameters.Set( MipRegion, new Vector4( x0, y0, x1, y1 ) );

			// A thread a texel for downsample_cs; tiled, an 8x8 group a 16x16 tile
			if ( tiled is not null ) Dispatch( downsample, parameters, (x1 - x0 + 15) / 16 * 8, (y1 - y0 + 15) / 16 * 8, 1 );
			else Dispatch( downsample, parameters, x1 - x0, y1 - y0, 1 );
			List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
		}

		if ( tailFrom >= copy.Mips ) return;

		// Mips past the last of the copy's are bound to it, unwritten
		var count = Math.Min( copy.Mips - tailFrom, TailMips.Length );
		var tailParameters = tailMipParameters;
		tailParameters.Set( MipLevel0, copy, tailFrom - 1 );
		for ( int m = 0; m < TailMips.Length; m++ ) tailParameters.Set( TailMips[m], copy, tailFrom + Math.Min( m, count - 1 ) );
		tailParameters.Set( TailInfo, new Vector4( (int)viewport.Width, (int)viewport.Height, tailFrom, count ) );
		Dispatch( tail, tailParameters, 256, 1, 1 );
		List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
	}

	/// <summary>The largest mip side <c>framecopy_tail_cs</c> makes, and its mips' attributes.</summary>
	const int TailSize = 32;
	static readonly StringToken[] TailMips = [new( "TailMip0" ), new( "TailMip1" ), new( "TailMip2" ), new( "TailMip3" ), new( "TailMip4" ), new( "TailMip5" )];
	static readonly StringToken TailInfo = new( "TailInfo" );

	/// <summary>
	/// Destroy the mip views compute made of a texture's image: when the texture goes or takes another image. They were kept
	/// for good before, one set per texture ever dispatched over.
	/// </summary>
	internal static void ForgetMipViews( BackendTexture texture, Device gpu )
	{
		if ( !mipViews.TryGetValue( texture, out var views ) ) return;
		lock ( views )
		{
			foreach ( var view in views.Values ) gpu.Destroy( view );
			views.Clear();
		}
	}

	TextureViewHandle MipView( BackendTexture texture, int mip )
	{
		var views = mipViews.GetValue( texture, static _ => new Dictionary<int, TextureViewHandle>() );
		lock ( views )
		{
			if ( views.TryGetValue( mip, out var view ) ) return view;
			view = Gpu.CreateView( texture.Handle, new TextureViewInfo { Dimension = TextureDimension.Texture2D, BaseMip = mip, MipCount = 1, LayerCount = 1 } );
			views[mip] = view;
			return view;
		}
	}
}
