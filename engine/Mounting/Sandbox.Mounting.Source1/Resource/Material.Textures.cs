using System.IO;
using Sandbox.Rendering;
using VtfFlags = Sandbox.Mounting.Source1.VtfFlags;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceMaterialBuilder
{
	private readonly Dictionary<(string Path, int Frame, bool Animated), SurfaceTexture> dependencies = [];
	internal bool TryGetMappingSize( out Vector2 size )
	{
		foreach ( var parameter in new[] { "$basetexture", "$envmapmask", "$bumpmap", "$dudvmap", "$normalmap" } )
		{
			var path = parameters.String( parameter );
			if ( string.IsNullOrWhiteSpace( path ) )
				continue;
			if ( path.Trim().StartsWith( "_rt_", System.StringComparison.OrdinalIgnoreCase ) )
			{
				size = default;
				return false;
			}

			var header = Host.GetSourceTexture( path, contentRoot ).Header;
			size = new Vector2( header.Width, header.Height );
			return true;
		}

		size = new Vector2( 32, 32 );
		return true;
	}

	private sealed record SurfaceTexture( Texture Texture, VtfHeader Header )
	{
		public bool HasAlpha => (Header.Flags & (VtfFlags.ONEBITALPHA | VtfFlags.EIGHTBITALPHA)) != 0
			|| Header.HiResFormat == VtfFormat.DXT1_ONEBITALPHA;
		public bool IsCube => (Header.Flags & VtfFlags.ENVMAP) != 0;

		internal SamplerState Sampler( bool clamp = false )
		{
			var flags = Header.Flags;
			if ( clamp || IsCube )
				flags |= VtfFlags.CLAMPS | VtfFlags.CLAMPT | VtfFlags.CLAMPU;

			return new SamplerState
			{
				Filter = (flags & VtfFlags.POINTSAMPLE) != 0 ? FilterMode.Point
					: (flags & VtfFlags.ANISOTROPIC) != 0 ? FilterMode.Anisotropic
					: (flags & VtfFlags.TRILINEAR) != 0 || IsCube ? FilterMode.Trilinear
					: FilterMode.Anisotropic,
				AddressModeU = Address( VtfFlags.CLAMPS ),
				AddressModeV = Address( VtfFlags.CLAMPT ),
				AddressModeW = Address( VtfFlags.CLAMPU )
			};
			TextureAddressMode Address( VtfFlags axis ) => (flags & VtfFlags.BORDER) != 0
				? TextureAddressMode.Border
				: (flags & axis) != 0 ? TextureAddressMode.Clamp : TextureAddressMode.Wrap;
		}

		public void Bind( Material material, string input, bool clamp = false )
		{
			material.Set( input, Texture );
			material.Attributes.Set( $"{input}Sampler", Sampler( clamp ) );
		}
	}

	private SurfaceTexture LoadSurfaceTexture( string path, int frame = 0, TextureAnimation? animation = null )
	{
		var key = (path.Trim().ToLowerInvariant(), animation?.StaticFrame ?? (animation.HasValue ? 0 : frame), animation is { FrameRate: > 0 });
		if ( dependencies.TryGetValue( key, out var cached ) && cached.Texture is { IsValid: true, IsError: false } )
			return cached;
		return dependencies[key] = ReadSurfaceTexture( path, frame, animation );
	}

	internal void ClearDependencies()
	{
		dependencies.Clear();
		proxyTexture = null;
		proxyTextureData = null;
	}

	private SurfaceTexture LoadEnvironmentTexture( string path, int frame )
	{
		path = VmtFile.NormalizePath( path, ".vtf" );
		var hdr = path.EndsWith( ".hdr.vtf", System.StringComparison.OrdinalIgnoreCase ) ? path : path[..^4] + ".hdr.vtf";
		if ( Host.GetContentPath( hdr, contentRoot ) is not null )
			path = hdr;
		else if ( Host.GetContentPath( path, contentRoot ) is null && Host is GarrysModMount )
		{
			Log.Trace( $"{FullPath}: environment texture '{path}' was not found; using GMod's default cubemap." );
			path = Host.GetContentPath( "materials/engine/defaultcubemap.hdr.vtf", contentRoot ) is not null
				? "materials/engine/defaultcubemap.hdr.vtf" : "materials/engine/defaultcubemap.vtf";
		}
		return LoadSurfaceTexture( path, frame );
	}

	private static bool IsFramebufferTexture( string path ) => path?.Trim().Equals( "_rt_fullframefb", System.StringComparison.OrdinalIgnoreCase ) == true;

	private SurfaceTexture ReadSurfaceTexture( string path, int frame, TextureAnimation? animation )
	{
		if ( path.Trim().Equals( "_rt_camera", System.StringComparison.OrdinalIgnoreCase )
			|| path.Trim().Equals( "_rt_fullscreen", System.StringComparison.OrdinalIgnoreCase ) )
		{
			if ( frame != 0 || animation is { FrameRate: > 0 } )
				throw new System.NotSupportedException( $"{FullPath}: camera render targets do not support frames or animation." );
			Log.Trace( $"{FullPath}: binding an inactive '{path}' render target; model-only mounting does not import camera feeds or captured UI views." );
			return new SurfaceTexture( Host.GetCameraRenderTarget(),
				new VtfHeader { Width = 256, Height = 256, Frames = 1, HiResFormat = VtfFormat.RGBA8888, Flags = VtfFlags.RENDERTARGET } );
		}
		if ( path.Trim().StartsWith( "_rt_", System.StringComparison.OrdinalIgnoreCase ) )
			throw new System.NotSupportedException( $"{FullPath}: unsupported render-target texture '{path}'." );
		path = VmtFile.NormalizePath( path, ".vtf" );
		var source = Host.GetSourceTexture( path, contentRoot );
		var selectedFrame = animation?.StaticFrame ?? (animation.HasValue ? 0 : frame);
		if ( animation?.StaticFrame is not null && (selectedFrame < 0 || selectedFrame >= source.Header.Frames) )
			selectedFrame = 0;
		var texture = animation is { FrameRate: > 0 } ? source.GetAnimationFrames() : source.GetFrame( selectedFrame );
		if ( texture == null || !texture.IsValid || texture.IsError )
			throw new InvalidDataException( $"{FullPath}: texture '{path}' frame {frame} could not be loaded." );
		return new SurfaceTexture( texture, source.Header );
	}
}
