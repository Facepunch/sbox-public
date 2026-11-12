using Basalt.Gpu;
using Basalt.Source2.Shaders;
using System.Runtime.CompilerServices;

namespace Basalt.Backend;

/// <summary>A texture's single mip, as <see cref="ShaderParameters.Set(StringToken, GpuTexture, int)"/> names one.</summary>
internal sealed record TextureMip( GpuTexture Texture, int Mip );

/// <summary>
/// Compute dispatches with parameters of their own (<see cref="GpuContext.Dispatch(GpuMaterial, ShaderParameters, int, int, int)"/>):
/// what Basalt's shared passes run, the depth chain among them. Each binds by reflection through a <see cref="ShaderDrawer"/>
/// of the device's: the parameters over this context's attributes, single mips as views of one mip, and the textures it
/// writes moved to <see cref="ImageLayout.General"/> (a mip it reads of a texture it writes moved to
/// <see cref="ImageLayout.ShaderRead"/> for the dispatch alone), the textures it only reads to
/// <see cref="ImageLayout.ShaderRead"/>.
/// </summary>
internal sealed partial class BackendContext
{
	/// <summary>The drawer parameterised dispatches bind with, one a device, binding by reflection; locked while used.</summary>
	static readonly ConditionalWeakTable<BackendDevice, ShaderDrawer> drawers = new();

	/// <summary>Single-mip views made for parameterised dispatches, one a texture and mip.</summary>
	static readonly ConditionalWeakTable<BackendTexture, Dictionary<int, TextureViewHandle>> mipViews = new();

	public override void Dispatch( GpuMaterial computeShader, ShaderParameters parameters, int x, int y, int z )
	{
		if ( recording is null || computeShader is not BackendMaterial { Shader: { } shader } ) return;
		if ( shader[ShaderProgramType.Compute] is not { } program || shader.Modes.Count == 0 ) return;
		var given = parameters as BackendParameters;

		object Value( string name )
		{
			var token = Token( name );
			return given?.Get( token ) ?? Lookup( token );
		}

		var drawer = drawers.GetValue( device, static d => new ShaderDrawer( d ) { MaterialObjects = false } );
		lock ( drawer )
		{
			var attributes = new ShaderAttributes();
			foreach ( var combo in program.DynamicCombos )
			{
				var token = Token( combo.Name );
				if ( (given?.GetCombo( token ) ?? LookupCombo( token )) is int value ) attributes.SetCombo( combo.Name, value );
			}

			var variant = drawer.Variant( shader, attributes );
			if ( variant?.Compute?.Bytecode is null ) return;

			foreach ( var (variable, _) in variant.Compute.MaterialConstants )
			{
				if ( variable.Source != VariableSource.Attribute || string.IsNullOrEmpty( variable.Attribute ) ) continue;
				if ( Floats( Value( variable.Attribute ) ) is { } floats ) attributes.Set( variable.Attribute, floats );
			}

			CloseScope();
			var written = new HashSet<BackendTexture>();
			var sampled = new HashSet<BackendTexture>();
			var sampledMips = new List<(BackendTexture Texture, int Mip)>();
			foreach ( var resource in variant.Resources )
			{
				if ( resource.Name is not { } name ) continue;
				var storage = resource.Kind == ShaderResourceKind.StorageTexture;
				switch ( Value( name ) )
				{
					case TextureMip { Texture: BackendTexture texture, Mip: var mip }:
						attributes.Set( name, MipView( texture, mip ) );
						if ( storage ) written.Add( texture );
						else sampledMips.Add( (texture, mip) );
						break;

					case BackendTexture texture when resource.Kind is ShaderResourceKind.Texture or ShaderResourceKind.StorageTexture:
						attributes.Set( name, texture.Handle );
						if ( storage ) written.Add( texture );
						else sampled.Add( texture );
						break;

					case GpuBuffer { Resource: BackendBuffer buffer }:
						attributes.Set( name, buffer.Handle );
						break;

					case byte[] data when resource.Kind == ShaderResourceKind.ConstantBuffer:
						attributes.SetData( name, data );
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

			drawer.Dispatch( List, shader, attributes, x, y, z );
			recording.Pipeline = default;
			recording.Binder = null;

			foreach ( var (texture, mip) in sampledMips )
			{
				if ( written.Contains( texture ) ) List.Barrier( MipBarrier( texture.Handle, mip, ImageLayout.ShaderRead, ImageLayout.General ) );
			}
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

	static Basalt.Gpu.TextureBarrier MipBarrier( TextureHandle texture, int mip, ImageLayout from, ImageLayout to ) => new()
	{
		Texture = texture,
		OldLayout = from,
		NewLayout = to,
		SourceStage = PipelineStage.AllCommands,
		SourceAccess = MemoryAccess.MemoryWrite,
		DestinationStage = PipelineStage.AllCommands,
		DestinationAccess = MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite,
		BaseMip = mip,
		MipCount = 1,
		LayerCount = 1,
	};
}
