using Borealis.Backend;
using System.Runtime.CompilerServices;
using Borealis.Source2.Models;
using System.Runtime.InteropServices;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// An engine resource as what the mirror's world draws with on the Borealis.Gpu device Borealis renders on
/// (<see cref="RenderHost.Device"/>, <c>-borealisgpu</c>): the compiled file Borealis.Source2 reads by the resource's path.
/// What was made at run time (procedural models, materials and textures made in code) and attributes have no file: models
/// made at run time are read back from Source 2 (see <see cref="RuntimeMesh"/>), attributes come from their managed copy
/// (<see cref="ObjectParameters"/>), and the rest are null. Without a Borealis.Gpu device everything is null.
/// </summary>
internal static class MirrorContent
{
	/// <summary>The Borealis.Gpu device Borealis renders on, or null in a process drawing on Source 2's.</summary>
	public static BackendDevice Gpu => RenderHost.Current?.Device as BackendDevice;

	/// <summary>A model's mesh, every mesh group in it (an object's draws hide those it doesn't draw).</summary>
	public static RenderMesh Mesh( Model model ) => Mesh( model, out _ );

	/// <summary>
	/// A model's mesh; loading asynchronously (<see cref="BackendDevice.AsyncModels"/>) null with <paramref name="pending"/>
	/// set while it loads.
	/// </summary>
	public static RenderMesh Mesh( Model model, out bool pending ) => Mesh( model, out pending, pin: true );

	/// <summary>
	/// <see cref="Mesh(Model, out bool)"/>; without <paramref name="pin"/> a model loaded from its file is held by the caller
	/// (<see cref="BackendDevice.HoldModel"/>) and freed a while after the last holder lets it go.
	/// </summary>
	public static RenderMesh Mesh( Model model, out bool pending, bool pin )
	{
		pending = false;
		if ( Gpu is not { } gpu ) return null;

		if ( model is { IsProcedural: true } ) return RuntimeMesh( model, gpu, out pending );
		var path = model?.ResourcePath;
		if ( string.IsNullOrEmpty( path ) ) return null;
		return gpu.AsyncModels ? gpu.LoadModelAsync( path, ulong.MaxValue, out pending, pin ) : gpu.LoadModel( path, ulong.MaxValue );
	}

	static readonly ConditionalWeakTable<Model, RuntimeModel> runtimeMeshes = new();
	static readonly ConditionalWeakTable<Model, System.Threading.Tasks.Task<ModelFile>> runtimeReads = new();
	static readonly System.Collections.Concurrent.ConcurrentQueue<BackendModel> freedModels = new();

	/// <summary>
	/// A model made at run time (no file to read): saved as a compiled model in memory
	/// (<c>Model.SaveToVmdl</c>, its buffers read back from Source 2 as they are) and read as a model file is, its materials by
	/// path; loading asynchronously (<see cref="BackendDevice.AsyncModels"/>) that's on a worker, and it's made within the
	/// frame's load budget, null with <paramref name="pending"/> set until then. Its buffers are freed once the engine's model
	/// is gone.
	/// </summary>
	static RenderMesh RuntimeMesh( Model model, BackendDevice gpu, out bool pending )
	{
		pending = false;
		while ( freedModels.TryDequeue( out var freed ) ) freed.Free();
		if ( runtimeMeshes.TryGetValue( model, out var known ) ) return known.Mesh;

		// Every mesh: a model built in code has no default mesh groups (native draws them all)
		RenderMesh mesh;
		if ( gpu.AsyncModels )
		{
			var read = runtimeReads.GetValue( model, m => gpu.ReadModelAsync( m.SaveToVmdl ) );
			mesh = gpu.MakeModel( read, ulong.MaxValue, drawFastPath: true, out pending );
			if ( pending ) return null;
			runtimeReads.Remove( model );
		}
		else mesh = BackendModel.Create( gpu, ModelFile.Read( model.SaveToVmdl(), gpu.Content.TryRead ), meshGroups: ulong.MaxValue, drawFastPath: true );

		runtimeMeshes.AddOrUpdate( model, new RuntimeModel( mesh ) );
		return mesh;
	}

	/// <summary>Whether a model loading asynchronously (<see cref="Mesh(Model, out bool)"/>) has been read, so asking again can make it.</summary>
	public static bool MeshRead( Model model, BackendDevice gpu ) => model is { IsProcedural: true }
		? !runtimeReads.TryGetValue( model, out var read ) || read.IsCompleted
		: gpu.ModelRead( model.ResourcePath );

	/// <summary>A run-time model's mesh, which frees its geometry after the engine's model is collected.</summary>
	sealed class RuntimeModel( RenderMesh mesh )
	{
		public RenderMesh Mesh { get; } = mesh;

		~RuntimeModel()
		{
			if ( Mesh.Geometry is BackendModel geometry ) freedModels.Enqueue( geometry );
		}
	}

	/// <summary>
	/// A material as its compiled file (a copy's being its original's), or as its shader for one made in code
	/// (<see cref="Sandbox.Material.MadeFromShader"/>), with what code set on it over that
	/// (<see cref="Sandbox.Material.SetInCode"/>: a screen's render target a game sets on its copy of the screen material),
	/// which follows later sets (<see cref="Follow"/>).
	/// </summary>
	public static GpuMaterial Material( Material material )
	{
		if ( Gpu is not { } gpu ) return null;

		var path = material?.FilePath;
		if ( string.IsNullOrEmpty( path ) ) return null;
		var shader = material.MadeFromShader;
		var set = material.SetInCode;
		if ( shader is null && set is not { Count: > 0 } ) return gpu.LoadMaterial( path );

		var made = setMaterials.GetOrCreateValue( material );
		lock ( made )
		{
			if ( made.Material is null )
			{
				Sandbox.Material.SetInCodeChanged ??= Follow;
				var (values, textures) = Lower( set );

				// Made in code from a shader: the shader's defaults, then what's set
				if ( shader is not null )
				{
					made.Material = gpu.CreateMaterial( path, ShaderPath( shader ) );
					gpu.SetOnMaterial( made.Material, values, textures );
				}
				else made.Material = gpu.LoadMaterial( path, values, textures, $"{path} (set in code)" );

				made.Version = material.SetInCodeVersion;
			}

			return made.Material;
		}
	}

	/// <summary>A shader as a material names it (<c>complex.shader</c>, or with its folder), as its compiled file's path.</summary>
	static string ShaderPath( string shader )
	{
		shader = shader.Replace( '\\', '/' );
		if ( shader.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) shader = shader[..^2];
		if ( !shader.EndsWith( ".shader", StringComparison.OrdinalIgnoreCase ) ) shader += ".shader";
		return shader.Contains( '/' ) ? shader : $"shaders/{shader}";
	}

	/// <summary>A material whose values set in code changed: its copy here takes them, if it has one.</summary>
	static void Follow( Sandbox.Material material )
	{
		if ( Gpu is not { } gpu || !setMaterials.TryGetValue( material, out var made ) || material.SetInCode is not { } set ) return;

		lock ( made )
		{
			if ( made.Material is null || made.Version == material.SetInCodeVersion ) return;
			var (values, textures) = Lower( set );
			gpu.SetOnMaterial( made.Material, values, textures );
			made.Version = material.SetInCodeVersion;
		}
	}

	/// <summary>Values set in code as shader variables: vectors, and textures Borealis.Gpu has.</summary>
	static (Dictionary<string, float[]> Values, Dictionary<string, GpuTexture> Textures) Lower( IReadOnlyDictionary<string, object> set )
	{
		var values = new Dictionary<string, float[]>( StringComparer.OrdinalIgnoreCase );
		var textures = new Dictionary<string, GpuTexture>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (name, value) in set ?? new Dictionary<string, object>() )
		{
			if ( value is Vector4 v ) values[name] = [v.x, v.y, v.z, v.w];
			else if ( value is Sandbox.Texture t && Texture( t ) is { } texture ) textures[name] = texture;
		}

		return (values, textures);
	}

	sealed class SetMaterial
	{
		public GpuMaterial Material;
		public int Version;
	}

	static readonly ConditionalWeakTable<Sandbox.Material, SetMaterial> setMaterials = new();

	public static GpuTexture Texture( Texture texture )
	{
		if ( Gpu is not { } gpu || texture is null ) return null;

		// A view rendered for it on this device (a water body's refraction) drew into Borealis's own texture (a render
		// target's path is its name, with no file behind it)
		if ( captures.TryGetValue( texture, out var capture ) && capture.Texture is { IsValid: true } drawn && !capture.Owned ) return drawn;

		// A cube made at run time for compute to write (a realtime probe's) is what cube captures draw into
		if ( IsCaptureCube( texture ) ) return CaptureCube( texture );

		// A 2D render target (a screen a game draws into) is the engine's copy on the device: its path is only its name
		var engine = RenderBackend.Default as BorealisGpuRenderBackend;
		if ( engine is not null && texture.Depth == 1 && !texture.native.IsNull && texture.IsRenderTarget ) return engine.BorealisViewOf( texture );

		var path = texture.ResourcePath;
		if ( !string.IsNullOrEmpty( path ) ) return gpu.LoadTexture( path );

		// A material's texture (a sky's cubemap for fog and sky lighting) is the material's, read by its path
		if ( Sandbox.Texture.MaterialOrigins.TryGetValue( texture, out var origin ) && origin is Sandbox.Texture.MaterialOrigin o )
			return gpu.MaterialTexture( o.Material, o.Parameter );

		// Any other 2D texture made at run time (a render target a game draws a screen into) is the engine's copy on the device
		return engine is not null && !texture.native.IsNull && texture.Depth == 1 ? engine.BorealisViewOf( texture ) : null;
	}

	sealed class Capture
	{
		public GpuTexture Texture;

		/// <summary>Whether <see cref="Texture"/> is this capture's own (a cube's), freed once the engine texture is gone.</summary>
		public bool Owned;

		~Capture()
		{
			if ( Owned && Texture is not null ) freedCaptures.Enqueue( Texture );
		}
	}

	static readonly ConditionalWeakTable<Texture, Capture> captures = new();
	static readonly System.Collections.Concurrent.ConcurrentQueue<GpuTexture> freedCaptures = new();

	/// <summary>
	/// Free what the engine's resources took on the device once they were collected (capture cubes, run-time models' buffers),
	/// whether or not anything asks for another: a map's realtime probes' cubes outlived it in the menu.
	/// </summary>
	public static void FreeCollected()
	{
		while ( freedCaptures.TryDequeue( out var freed ) ) freed.Dispose();
		while ( freedModels.TryDequeue( out var model ) ) model.Free();
	}

	/// <summary>Whether an engine texture is a cube made at run time for compute to write (a realtime probe's), which <see cref="CaptureCube"/> captures into.</summary>
	public static bool IsCaptureCube( Texture texture ) => texture is not null && texture.Depth == 6 && texture.UAVAccess;

	/// <summary>The cube <see cref="CaptureCube"/> has made for <paramref name="texture"/>, or null when nothing has captured or drawn it yet.</summary>
	public static GpuTexture MadeCaptureCube( Texture texture ) => captures.TryGetValue( texture, out var capture ) && capture.Owned && capture.Texture is { IsValid: true } made ? made : null;

	/// <summary>How many cubes <see cref="CaptureCube"/> has made, for what waits for one (<see cref="MadeCaptureCube"/>).</summary>
	public static int CaptureCubesMade { get; private set; }

	/// <summary>
	/// The Borealis.Gpu cube that cube captures of <paramref name="texture"/> (an engine cube made at run time, a realtime
	/// probe's) draw into, and that what reads the engine texture reads: made the first time either asks, as the engine made
	/// it (size, format, mips), black until captured, and made again if the engine texture's shape changes. Null without a
	/// device, or for a texture that isn't a square cube in a format Borealis.Gpu has.
	/// </summary>
	public static GpuTexture CaptureCube( Texture texture )
	{
		if ( Gpu is not { } gpu || texture is null || texture.Depth != 6 || texture.Width != texture.Height || texture.Width < 1 ) return null;

		while ( freedCaptures.TryDequeue( out var freed ) ) freed.Dispose();

		var format = Source2Formats.From( texture.ImageFormat );
		var mips = Math.Max( texture.Mips, 1 );
		var capture = captures.GetOrCreateValue( texture );
		if ( capture.Owned && capture.Texture is { IsValid: true, IsCube: true } made && made.Width == texture.Width && made.Format == format && made.Mips == mips ) return made;
		if ( format == TextureFormat.Unknown ) return null;

		if ( capture.Owned ) capture.Texture?.Dispose();
		capture.Texture = gpu.CreateTexture( new TextureDesc
		{
			Width = texture.Width,
			Height = texture.Height,
			Depth = 1,
			Format = format,
			Samples = 1,
			Mips = mips,
			Cube = true,
			RenderTarget = true,
			Storage = true,
		}, $"Cube capture {texture.Width}px" );
		capture.Owned = true;
		CaptureCubesMade++;
		gpu.Clear( capture.Texture, Color.Transparent );
		return capture.Texture;
	}

	/// <summary>
	/// Note that a view rendered for <paramref name="texture"/> (an engine render target a command list's view asked for) drew
	/// into <paramref name="drawn"/>, a texture of Borealis's: what Borealis's draws read for the engine texture from then on.
	/// </summary>
	public static void SetCapture( Texture texture, GpuTexture drawn ) => captures.GetOrCreateValue( texture ).Texture = drawn;

	/// <summary>
	/// Set on <paramref name="into"/> what <paramref name="attributes"/> had set after its version was <paramref name="version"/>:
	/// what a command list set meanwhile. Numbers, vectors, matrices, combos and the textures Borealis can read carry over.
	/// </summary>
	public static void SetChanged( ManagedRenderAttributes attributes, int version, ShaderParameters into )
	{
		foreach ( var (token, value) in attributes.ValuesSince( version ) ) Set( into, new StringToken( token ), value );
		foreach ( var (token, value) in attributes.CombosSince( version ) ) into.SetCombo( new StringToken( token ), value );
	}

	/// <summary>
	/// A camera's attributes (<c>SceneCamera.Attributes</c>: what its component and <c>Scene.RenderAttributes</c> set, a game's
	/// time of day among them) as its view's camera attributes, as native's camera renderer gives its view them, kept in
	/// <paramref name="parameters"/> (made the first time) by <see cref="CopyChanged"/>.
	/// </summary>
	public static ShaderParameters CameraParameters( RenderAttributes attributes, ref ShaderParameters parameters, ref int version )
	{
		if ( Gpu is not { } gpu || (attributes?.Shadow ?? attributes?.Managed) is null ) return null;
		if ( parameters is null )
		{
			parameters = gpu.CreateParameters();
			version = NeverCopied;
		}

		CopyChanged( attributes, parameters, ref version );
		return parameters;
	}

	/// <summary>The version to start <see cref="CopyChanged"/> from: before anything was set.</summary>
	public const int NeverCopied = int.MinValue;

	/// <summary>
	/// Keep <paramref name="into"/> holding what <paramref name="attributes"/> (its managed copy, or its managed values) holds:
	/// only what was set since <paramref name="version"/>, or everything again when something went since.
	/// </summary>
	public static void CopyChanged( RenderAttributes attributes, ShaderParameters into, ref int version )
	{
		if ( (attributes?.Shadow ?? attributes?.Managed) is not { } source || source.Version == version ) return;
		if ( source.RemovedAt > version && version != NeverCopied )
		{
			into.Clear();
			version = NeverCopied;
		}

		SetChanged( source, version, into );
		version = source.Version;
	}

	/// <summary>
	/// A view's own attributes with a managed store (a planar reflection published for it) written into
	/// <paramref name="parameters"/>, made the first time: what was there before is cleared.
	/// </summary>
	public static ShaderParameters ViewParameters( RenderAttributes attributes, ref ShaderParameters parameters )
	{
		if ( Gpu is not { } gpu || attributes?.Managed is not { } managed ) return null;

		parameters ??= gpu.CreateParameters();
		parameters.Clear();
		foreach ( var (token, value) in managed.Values ) Set( parameters, new StringToken( token ), value );
		foreach ( var (token, value) in managed.Combos ) parameters.SetCombo( new StringToken( token ), value );
		return parameters;
	}

	static void Set( ShaderParameters parameters, StringToken name, object value )
	{
		switch ( value )
		{
			case float[] { Length: 1 } f: parameters.Set( name, f[0] ); break;
			case float[] { Length: 2 } f: parameters.Set( name, new Vector2( f[0], f[1] ) ); break;
			case float[] { Length: 3 } f: parameters.Set( name, new Vector3( f[0], f[1], f[2] ) ); break;
			case float[] { Length: 4 } f: parameters.Set( name, new Vector4( f[0], f[1], f[2], f[3] ) ); break;
			case float[] { Length: 16 } f: parameters.Set( name, MemoryMarshal.Read<Matrix>( MemoryMarshal.AsBytes( f.AsSpan() ) ) ); break;
			case ManagedRenderAttributes.TextureValue t when Texture( t.Texture ) is { } texture: parameters.Set( name, texture, t.Mip ); break;
		}
	}

	/// <summary>
	/// An object's attributes as its draws read them: its managed copy (<c>RenderAttributes.Shadow</c>) converted, again only
	/// when it changed. Numbers, vectors, matrices, combos and textures with a file carry over; buffers, samplers and constant
	/// buffers don't yet.
	/// </summary>
	public static ShaderParameters ObjectParameters( RenderAttributes attributes )
	{
		if ( Gpu is not { } gpu || attributes?.Shadow is not { } shadow ) return null;

		var converted = convertedAttributes.GetOrCreateValue( shadow );
		if ( converted.Parameters is not null && converted.Version == shadow.Version ) return converted.Parameters;

		var parameters = gpu.CreateParameters();
		foreach ( var (token, value) in shadow.Values ) Set( parameters, new StringToken( token ), value );

		foreach ( var (token, value) in shadow.Combos ) parameters.SetCombo( new StringToken( token ), value );
		converted.Parameters = parameters;
		converted.Version = shadow.Version;
		return parameters;
	}

	sealed class ConvertedAttributes
	{
		public ShaderParameters Parameters;
		public int Version;
	}

	static readonly ConditionalWeakTable<ManagedRenderAttributes, ConvertedAttributes> convertedAttributes = new();
}
