using NativeEngine;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// Used to render to the screen using your Graphics Card, or whatever you
/// kids are using in your crazy future computers. Whatever it is I'm sure
/// it isn't fungible and everyone has free money and no-one has to ever work.
/// </summary>
public static partial class Graphics
{
	public enum PrimitiveType
	{
		Points = NativeEngine.RenderPrimitiveType.RENDER_PRIM_POINTS,
		Lines = NativeEngine.RenderPrimitiveType.RENDER_PRIM_LINES,
		LinesWithAdjacency = NativeEngine.RenderPrimitiveType.RENDER_PRIM_LINES_WITH_ADJACENCY,
		LineStrip = NativeEngine.RenderPrimitiveType.RENDER_PRIM_LINE_STRIP,
		LineStripWithAdjacency = NativeEngine.RenderPrimitiveType.RENDER_PRIM_LINE_STRIP_WITH_ADJACENCY,
		Triangles = NativeEngine.RenderPrimitiveType.RENDER_PRIM_TRIANGLES,
		TrianglesWithAdjacency = NativeEngine.RenderPrimitiveType.RENDER_PRIM_TRIANGLES_WITH_ADJACENCY,
		TriangleStrip = NativeEngine.RenderPrimitiveType.RENDER_PRIM_TRIANGLE_STRIP,
		TriangleStripWithAdjacency = NativeEngine.RenderPrimitiveType.RENDER_PRIM_TRIANGLE_STRIP_WITH_ADJACENCY,
	}

	/// <summary>
	/// If true then we're currently rendering and
	/// you are safe to use the contents of this class
	/// </summary>
	public static bool IsActive => _state.active;

	/// <summary>
	/// The current layer type. This is useful to tell whether you're meant to be drawing opaque, transparent or shadow. You mainly
	/// don't need to think about this, but when you do, it's here.
	/// </summary>
	public static SceneLayerType LayerType => _state.layerType;

	struct RenderState
	{
		public bool active;
		public IRenderContext renderContext;
		public ISceneLayer sceneLayer;
		public ISceneView sceneView;
		public SceneLayerType layerType;
		public RenderAttributes attributes;
		internal SceneSystemPerFrameStats_t stats;
		public ImageFormat colorFormat;
		public MultisampleAmount msaaLevel;
		public Transform cameraTransform;
		internal RenderAttributes frameAttributes;
		internal RenderTarget renderTarget;
		internal float defaultMinZ;
		internal float defaultMaxZ;
	}

	[ThreadStatic]
	private static RenderState _state;

	internal static IRenderContext Context => _state.renderContext;
	internal static ISceneLayer SceneLayer => _state.sceneLayer;
	internal static ISceneView SceneView => _state.sceneView;
	internal static ImageFormat IdealColorFormat => _state.colorFormat;
	internal static MultisampleAmount IdealMsaaLevel => _state.msaaLevel;
	internal static SceneSystemPerFrameStats_t Stats => _state.stats;

	/// <summary>
	/// When Frame grabbing, we store the result in this pooled render target. It stays checked out
	/// until the end of this render scope.
	/// </summary>
	[ThreadStatic]
	static HashSet<RenderTarget> grabbedTextures;

	/// <summary>
	/// In pixel size, where are we rendering to?
	/// </summary>
	public static Rect Viewport
	{
		get => Context.GetViewport().Rect;
		set => Context.SetViewport( value );
	}

	/// <summary>
	/// Access to the current render context's attributes. These will be used
	/// to set attributes in materials/shaders. This is cleared at the end of the render block.
	/// </summary>
	public static RenderAttributes Attributes => _state.attributes;


	/// <summary>
	/// Access to the current frame's attributes.
	/// These will live until the end of the frame.
	/// </summary>
	internal static RenderAttributes FrameAttributes => _state.frameAttributes;

	/// <summary>
	/// The camera transform of the currently rendering view
	/// </summary>
	public static Transform CameraTransform => _state.cameraTransform;

	/// <summary>
	/// The camera position of the currently rendering view
	/// </summary>
	public static Vector3 CameraPosition => CameraTransform.Position;

	/// <summary>
	/// The camera rotation of the currently rendering view
	/// </summary>
	public static Rotation CameraRotation => CameraTransform.Rotation;


	/// <summary>
	/// The field of view of the currently rendering camera view, in degrees.
	/// </summary>
	public static float FieldOfView => _state.sceneView.GetFrustum().GetCameraFOV();

	/// <summary>
	/// The frustum of the currently rendering camera view.
	/// </summary>
	public static Frustum Frustum
	{
		get
		{
			AssertRenderBlock();

			var cf = _state.sceneView.GetFrustum();

			// Extract planes from native CFrustum
			// Plane indices: RIGHT=0, LEFT=1, TOP=2, BOTTOM=3, NEAR=4, FAR=5
			cf.GetPlane( 0, out var rn, out var rd );
			cf.GetPlane( 1, out var ln, out var ld );
			cf.GetPlane( 2, out var tn, out var td );
			cf.GetPlane( 3, out var bn, out var bd );
			cf.GetPlane( 4, out var nn, out var nd );
			cf.GetPlane( 5, out var fn, out var fd );

			return new Frustum(
				right: new Plane( ln, ld ),
				left: new Plane( ln, rd ),
				top: new Plane( tn, td ),
				bottom: new Plane( bn, bd ),
				near: new Plane( nn, nd ),
				far: new Plane( fn, fd )
			);
		}
	}


	internal static int RenderMultiSampleToNum( RenderMultisampleType msaaLevel )
	{
		switch ( msaaLevel )
		{
			case RenderMultisampleType.RENDER_MULTISAMPLE_NONE:
				return 1;
			case RenderMultisampleType.RENDER_MULTISAMPLE_2X:
				return 2;
			case RenderMultisampleType.RENDER_MULTISAMPLE_4X:
				return 4;
			case RenderMultisampleType.RENDER_MULTISAMPLE_6X:
				return 6;
			case RenderMultisampleType.RENDER_MULTISAMPLE_8X:
				return 8;
			case RenderMultisampleType.RENDER_MULTISAMPLE_16X:
				return 16;
			default:
				throw new System.Exception( "Unknown multisample amount" );
		}
	}

	/// <summary>
	/// Creates a scope where Graphics is safe to use.
	/// </summary>
	internal ref struct Scope
	{
		RenderState _previous;

		public Scope( in ManagedRenderSetup_t setup )
		{
			_previous = _state;

			_state = new RenderState();

			var frustum = setup.sceneView.GetFrustum();

			_state.active = true;
			_state.sceneLayer = setup.sceneLayer;
			_state.sceneView = setup.sceneView;
			_state.renderContext = setup.renderContext;
			_state.layerType = setup.sceneLayer.LayerEnum;
			_state.colorFormat = setup.colorImageFormat;
			_state.msaaLevel = setup.msaaLevel.FromEngine();
			_state.cameraTransform = new Transform( frustum.GetCameraPosition(), frustum.GetCameraAngles() );
			_state.stats = setup.stats;

			_state.attributes = ObjectPool<RenderAttributes>.Get();
			_state.attributes.Set( setup.renderContext.GetAttributesPtrForModify() );

			_state.frameAttributes = ObjectPool<RenderAttributes>.Get();
			_state.frameAttributes.Set( setup.sceneView.GetRenderAttributesPtr() );
		}

		public void Dispose()
		{
			if ( _state.attributes is not null )
			{
				_state.attributes.Set( default( CRenderAttributes ) );
				ObjectPool<RenderAttributes>.Return( _state.attributes );
			}

			if ( _state.frameAttributes is not null )
			{
				_state.frameAttributes.Set( default( CRenderAttributes ) );
				ObjectPool<RenderAttributes>.Return( _state.frameAttributes );
			}

			_state = _previous;

			if ( grabbedTextures is not null )
			{
				// return to the pool
				foreach ( var tex in grabbedTextures )
				{
					tex.Dispose();
				}
				grabbedTextures.Clear();
			}
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal static void AssertRenderBlock()
	{
		if ( !IsActive )
			throw new System.Exception( "Tried to render outside of rendering block" );
	}

	/// <summary>
	/// Setup the lighting attributes for this current object. Place them in the targetAttributes
	/// </summary>
	public static void SetupLighting( SceneObject obj, RenderAttributes targetAttributes = null )
	{
		targetAttributes ??= Attributes;

		Assert.NotNull( targetAttributes );
		Assert.IsValid( obj );

		NativeEngine.CSceneSystem.SetupPerObjectLighting( targetAttributes.Get(), obj, SceneLayer );
	}

	/// <summary>
	/// Grabs the current viewport's color texture and stores it in targetName on renderAttributes.
	/// </summary>
	public static RenderTarget GrabFrameTexture( string targetName = "FrameTexture", RenderAttributes renderAttributes = null, DownsampleMethod downsampleMethod = DownsampleMethod.None )
	{
		renderAttributes ??= Attributes;

		AssertRenderBlock();

		bool withMips = downsampleMethod != DownsampleMethod.None;
		var numMips = withMips ? (int)Math.Log2( Math.Max( Viewport.Width, Viewport.Height ) ) : 1;

		// Grab a new one - which may very well be the one we just returned
		var frameTexture = RenderTarget.GetTemporary( 1, ImageFormat.Default, ImageFormat.None, msaa: MultisampleAmount.MultisampleNone, numMips: numMips, targetName );

		RenderTools.ResolveFrameBuffer( Context, frameTexture.ColorTarget.native, Viewport );

		// Generate a mip chain if we want one
		if ( withMips ) GenerateMipMaps( frameTexture.ColorTarget, downsampleMethod );

		grabbedTextures ??= new();
		grabbedTextures.Add( frameTexture );

		renderAttributes.Set( targetName, frameTexture.ColorTarget );

		return frameTexture;
	}

	[Obsolete( "Use GrabFrameTexture with DownsampleMethod instead" )]
	public static void GrabFrameTexture( string targetName, RenderAttributes renderAttributes, bool withMips )
	{
		GrabFrameTexture( targetName, renderAttributes, withMips ? DownsampleMethod.GaussianBlur : DownsampleMethod.None );
	}

	/// <summary>
	/// Grabs the current depth texture and stores it in targetName on renderAttributes.
	/// </summary>
	public static RenderTarget GrabDepthTexture( string targetName = "DepthTexture", RenderAttributes renderAttributes = null )
	{
		renderAttributes ??= Attributes;

		AssertRenderBlock();

		// Grab a new one - which may very well be the one we just returned
		var grabbedTexture = RenderTarget.GetTemporary( 1, ImageFormat.None, ImageFormat.Default, msaa: MultisampleAmount.MultisampleScreen, targetName: targetName );

		RenderTools.ResolveDepthBuffer( Context, grabbedTexture.DepthTarget.native, Viewport );

		grabbedTextures ??= new();
		grabbedTextures.Add( grabbedTexture );

		renderAttributes.Set( targetName, grabbedTexture.DepthTarget );

		return grabbedTexture;
	}





	/// <summary>
	/// Get or set the current render target. Setting this will bind the render target and change the viewport to match it.
	/// </summary>
	public static RenderTarget RenderTarget
	{
		get => _state.renderTarget;
		set
		{
			if ( _state.renderTarget == value )
				return;

			// Going from default render target to custom render target
			if ( _state.renderTarget == null )
			{
				var viewport = Context.GetViewport();
				// Save off min/max Z values here, so we can restore them later.
				_state.defaultMinZ = viewport.MinZ;
				_state.defaultMaxZ = viewport.MaxZ;
			}

			_state.renderTarget = value;

			// Resetting to default render target
			if ( _state.renderTarget == null )
			{
				// alex: if we don't restore min/max Z values properly when setting back to
				// the default render target, we get a lot of weird depth issues.
				// This mainly only applies to things like worldpanels when we render filtered
				// elements, but could probably happen in other places too?
				var viewport = new RenderViewport( SceneLayer.m_viewport.Rect, _state.defaultMinZ, _state.defaultMaxZ );
				Context.SetViewport( viewport );
				Context.RestoreRenderTargets( SceneLayer );
				return;
			}

			Context.BindRenderTargets( _state.renderTarget.ColorTarget?.native ?? default, _state.renderTarget.DepthTarget?.native ?? default, SceneLayer );
			Viewport = new Rect( 0, 0, _state.renderTarget.Width, _state.renderTarget.Height );
		}
	}

	/// <summary>
	/// Clear the current drawing context to given color.
	/// </summary>
	/// <param name="color">Color to clear to.</param>
	/// <param name="clearColor">Whether to clear the color buffer at all.</param>
	/// <param name="clearDepth">Whether to clear the depth buffer.</param>
	/// <param name="clearStencil">Whether to clear the stencil buffer.</param>
	public static void Clear( Color color, bool clearColor = true, bool clearDepth = true, bool clearStencil = true )
	{
		Context.Clear( color, clearColor, clearDepth, clearStencil );
	}

	/// <summary>
	/// Clear the current drawing context to given color.
	/// </summary>
	/// <param name="clearColor">Whether to clear the color buffer to transparent color.</param>
	/// <param name="clearDepth">Whether to clear the depth buffer.</param>
	public static void Clear( bool clearColor = true, bool clearDepth = true )
	{
		Clear( Color.Transparent, clearColor, clearDepth, false );
	}

	/// <summary>
	/// Render this camera to the specified texture target
	/// </summary>
	[Obsolete( "Use CameraComponent.RenderToTexture" )]
	public static bool RenderToTexture( SceneCamera camera, Texture target )
	{
		return false;
	}

	/// <summary>
	/// Copies pixel data from one texture to another on the GPU.
	/// This does not automatically resize or scale the texture, format and size should be equal.
	/// </summary>
	public static void CopyTexture( Texture srcTexture, Texture dstTexture )
	{
		if ( srcTexture == null ) throw new ArgumentNullException( nameof( srcTexture ) );
		if ( dstTexture == null ) throw new ArgumentNullException( nameof( dstTexture ) );
		if ( srcTexture.ImageFormat != dstTexture.ImageFormat ) throw new ArgumentException( "Source and destination texture format must match!" );
		if ( srcTexture.Size != dstTexture.Size ) throw new ArgumentException( "Source and destination texture size must match!" );

		RenderTools.CopyTexture( Context, srcTexture.native, dstTexture.native, default, 0, 0, 0, 0, 0, 0 );
	}

	[Obsolete( "Use the CopyTexture overload without 'srcMipLevels' and 'dstMipLevels' parameters instead." )]
	public static void CopyTexture( Texture srcTexture, Texture dstTexture, int srcMipSlice = 0, int srcArraySlice = 0, int srcMipLevels = 1, int dstMipSlice = 0, int dstArraySlice = 0, int dstMipLevels = 1 )
	{
		CopyTexture( srcTexture, dstTexture, srcMipSlice, srcArraySlice, dstMipSlice, dstArraySlice );
	}

	/// <summary>
	/// Copies pixel data from one texture to another on the GPU.
	/// This does not automatically resize or scale the texture, format and size should be equal.
	/// This one lets you copy to/from arrays / specific mips.
	/// </summary>
	public static void CopyTexture( Texture srcTexture, Texture dstTexture, int srcMipSlice = 0, int srcArraySlice = 0, int dstMipSlice = 0, int dstArraySlice = 0 )
	{
		if ( srcTexture == null ) throw new ArgumentNullException( nameof( srcTexture ) );
		if ( dstTexture == null ) throw new ArgumentNullException( nameof( dstTexture ) );

		if ( srcTexture.ImageFormat != dstTexture.ImageFormat ) throw new ArgumentException( "Source and destination texture format must match!" );

		if ( srcMipSlice < 0 || srcMipSlice >= srcTexture.Mips ) throw new ArgumentException( $"{nameof( srcMipSlice )} out of bounds" );
		if ( dstMipSlice < 0 || dstMipSlice >= dstTexture.Mips ) throw new ArgumentException( $"{nameof( dstMipSlice )} out of bounds" );

		var srcDesc = srcTexture.Desc;
		var dstDesc = dstTexture.Desc;

		if ( srcArraySlice < 0 || srcArraySlice >= srcDesc.ArrayCount ) throw new ArgumentException( $"{nameof( srcArraySlice )} out of bounds" );
		if ( dstArraySlice < 0 || dstArraySlice >= dstDesc.ArrayCount ) throw new ArgumentException( $"{nameof( dstArraySlice )} out of bounds" );

		var srcMipWidth = Math.Max( 1, srcTexture.Width >> srcMipSlice );
		var srcMipHeight = Math.Max( 1, srcTexture.Height >> srcMipSlice );
		var dstMipWidth = Math.Max( 1, dstTexture.Width >> dstMipSlice );
		var dstMipHeight = Math.Max( 1, dstTexture.Height >> dstMipSlice );

		if ( srcMipWidth != dstMipWidth || srcMipHeight != dstMipHeight )
			throw new ArgumentException( "Source and destination texture mip level sizes must match!" );

		RenderTools.CopyTexture( Context, srcTexture.native, dstTexture.native, default, 0, 0,
			(uint)srcMipSlice, (uint)srcArraySlice,
			(uint)dstMipSlice, (uint)dstArraySlice );
	}

	/// <summary>
	/// Forces the GPU to flush all pending commands and wait for completion.
	/// Useful when you need to ensure GPU work is finished before proceeding.
	/// Can be called outside of a render block.
	/// </summary>
	public static void FlushGPU()
	{
		if ( Application.IsHeadless )
			return;

		g_pRenderDevice.ForceFlushGPU( default );
	}
}
