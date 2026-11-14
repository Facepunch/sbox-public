namespace Borealis;

/// <summary>
/// Camera and projection for one render, equivalent to native's <c>CSceneView</c>.
/// </summary>
public sealed class RenderView
{
	/// <summary>
	/// Name for profilers and debug output.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// World-space camera position.
	/// </summary>
	public Vector3 Position { get; set; }
	/// <summary>
	/// Camera orientation; looks along its forward axis.
	/// </summary>
	public Rotation Rotation { get; set; } = Rotation.Identity;

	/// <summary>
	/// Horizontal field of view in degrees, like <c>SceneCamera.FieldOfView</c>.
	/// </summary>
	public float FieldOfView { get; set; } = 80.0f;

	/// <summary>
	/// Use an orthographic projection with <see cref="OrthoSize"/> dimensions.
	/// </summary>
	public bool Orthographic { get; set; }

	/// <summary>
	/// Width and height of the view in world units, when <see cref="Orthographic"/>.
	/// </summary>
	public Vector2 OrthoSize { get; set; }

	/// <summary>
	/// A projection used instead of the one <see cref="FieldOfView"/> or <see cref="OrthoSize"/> make
	/// (<c>ViewSetup.ProjectionMatrix</c>, <c>SceneCamera.CustomProjectionMatrix</c>), in the engine's convention, as native's
	/// <c>CFrustum::SetOverrideProj</c> takes it: column vectors, depth 0 at the near plane and 1 at the far one. It replaces
	/// <see cref="ClipSpaceBounds"/> too. An oblique one, whose near plane is a clip plane, culls and draws with that plane;
	/// shadow cascades and light clusters fit the view with a near plane square to it instead.
	/// <see cref="FieldOfView"/> still measures screen size, as native's does.
	/// </summary>
	public Matrix? Projection { get; set; }

	/// <summary>
	/// The part of the projection drawn: a rectangle of clip space as left, bottom, right and top, (-1, -1, 1, 1) for all of it
	/// (<c>CFrustum::SetClipSpaceBounds</c>), for posters and off-centre views.
	/// </summary>
	public Vector4 ClipSpaceBounds { get; set; } = FullClipSpace;

	internal static readonly Vector4 FullClipSpace = new( -1, -1, 1, 1 );

	/// <summary>
	/// Mirror the finished frame left to right as it goes into a texture (<c>ViewSetup.FlipX</c>): native flips only a texture
	/// render's final copy (<c>CCameraRenderer::RenderToTexture</c>), so depth stays as drawn and a swap chain's frame isn't
	/// flipped.
	/// </summary>
	public bool FlipX { get; set; }

	/// <summary>
	/// Mirror the finished frame top to bottom as it goes into a texture (<c>ViewSetup.FlipY</c>), as <see cref="FlipX"/>.
	/// </summary>
	public bool FlipY { get; set; }

	/// <summary>
	/// Near clip plane distance. Reverse-Z puts it at depth 1.
	/// </summary>
	public float ZNear { get; set; } = 1.0f;
	/// <summary>
	/// Far clip plane distance. Reverse-Z puts it at depth 0.
	/// </summary>
	public float ZFar { get; set; } = 10000.0f;

	/// <summary>
	/// Viewport in pixels, within the target.
	/// </summary>
	public Rect Viewport { get; set; }

	/// <summary>
	/// The scale on the texture sizes this view's draws ask texture streaming for (<c>r_texture_lod_scale</c>), as native's
	/// views take it (<see cref="TextureSizes"/>).
	/// </summary>
	public float TextureLodScale { get; set; } = 1.0f;

	/// <summary>
	/// The scale on the texture sizes of draws without a UV density, from their screen size (<c>r_fallback_texture_lod_scale</c>).
	/// </summary>
	public float FallbackTextureLodScale { get; set; } = 2.0f;

	/// <summary>
	/// Gamma-space clear colour, converted to linear before clearing the viewport (<c>SceneCamera.BackgroundColor</c>).
	/// </summary>
	public Color ClearColor { get; set; } = Color.Black;

	/// <summary>
	/// Enable tools utility geometry (<c>SVF_TOOL_VIEW</c>).
	/// </summary>
	public bool ToolsView { get; set; }

	/// <summary>
	/// Shader debug mode from the camera or <c>mat_toolsvis</c>; 0 uses normal lighting.
	/// </summary>
	public int ToolsVisMode { get; set; }


	/// <summary>
	/// A reflection (<c>ViewSetup.Reflection</c>), drawn as native's pipeline draws one: no contact shadows, volumetric fog or
	/// bloom objects, and no shadows unless <see cref="ReflectionShadows"/>, then cascades no other view shares. Shaders and
	/// command lists see <c>IsReflection</c>, <c>ReflectionShadows</c> and <c>NeedsDepthNormals</c> (off).
	/// </summary>
	public bool Reflection { get; set; }

	/// <summary>
	/// Whether a <see cref="Reflection"/> draws shadows (<c>r.reflections.shadows</c>).
	/// </summary>
	public bool ReflectionShadows { get; set; }

	/// <summary>
	/// A face of a cube capture (an envmap probe), drawn as native's pipeline draws one: the world only, without objects
	/// <see cref="RenderObject.HiddenInCaptures"/> or viewmodels (<see cref="RenderObject.Overlay"/>), screen overlays or after-UI
	/// objects, and without occlusion culling, since its history would be another face's or another probe's.
	/// </summary>
	public bool Capture { get; set; }

	/// <summary>
	/// Optional camera and scene attributes, merged before pipeline overrides (<c>CCameraRenderer::CreateView</c>).
	/// </summary>
	internal ShaderParameters CameraAttributes { get; set; }

	/// <summary>
	/// Optional attributes of this view alone, merged over the camera's: what the host publishes for one of a camera's views,
	/// such as its planar reflection. Its 3D skybox doesn't get them.
	/// </summary>
	internal ShaderParameters ViewAttributes { get; set; }

	/// <summary>
	/// Camera-local analytic fog. Null keeps the world's legacy gradient and cubemap fog.
	/// Stored on the view so cameras sharing a RenderWorld cannot overwrite one another.
	/// </summary>
	public FogSettings? Fog { get; set; }

	/// <summary>
	/// The view's ambient light, in place of the world's <see cref="SceneLighting.AmbientColor"/>: native works one out for each
	/// view, from the world's and the camera's, tinted and added to by the view's setup (<c>CameraRenderer.Configure</c>). Null
	/// uses the world's. Its 3D skybox uses it too.
	/// </summary>
	public Color? AmbientLight { get; set; }

	/// <summary>
	/// The view's gradient fog, in place of the world's <see cref="SceneLighting.GradientFog"/> (<c>ViewSetup.GradientFog</c>).
	/// Null uses the world's. Its 3D skybox uses it too, scaled as the world's is.
	/// </summary>
	public GradientFogSettings? GradientFog { get; set; }

	/// <summary>
	/// Whether light modulators shade the view. Off, shaders skip them (<c>LightModulatorsDisabled</c>,
	/// <c>ViewSetup.EnableLightModulators</c>).
	/// </summary>
	public bool LightModulators { get; set; } = true;

	/// <summary>
	/// Shader time (<c>g_flTime</c>) in seconds, in place of the frame's (<c>ViewSetup.Time</c>, native's <c>RenderTime</c>
	/// attribute). Its 3D skybox uses it too. Null uses the frame's.
	/// </summary>
	public float? Time { get; set; }

	/// <summary>
	/// Shader exposure scale (<c>g_flToneMapScalarLinear</c>); auto-exposure value or 1.
	/// </summary>
	public float ToneMapScalar { get; set; } = 1.0f;

	/// <summary>
	/// Whatever the host attaches to the view for its own drawing code. Borealis never reads it.
	/// </summary>
	internal object HostView { get; set; }

	/// <summary>
	/// Minimum screen size (<c>CFrustum::ComputeScreenSize</c>). Defaults to 0.25%; zero disables size culling.
	/// </summary>
	public float SizeCullThreshold { get; set; } = 0.0025f;

	/// <summary>
	/// Required object tags as string tokens; empty allows all. Shadow views use the main view's tags.
	/// </summary>
	public uint[] RenderTags { get; set; } = [];

	/// <summary>
	/// Exclude objects matching any of these tags.
	/// </summary>
	public uint[] ExcludeTags { get; set; } = [];

	/// <summary>
	/// The sub-worlds this view shows, as bits: bit <c>n - 1</c> for <see cref="RenderObject.SubWorld"/> <c>n</c>. A world's own
	/// objects are always shown. Shadow views show their main view's.
	/// </summary>
	public ulong SubWorlds { get; set; }

	static readonly uint WorldTag = new StringToken( "world" ).Value;

	/// <summary>
	/// Apply the view's sub-worlds, then include/exclude tags, treating world objects as tagged <c>world</c>
	/// (<c>CSceneSystem</c>, <c>CLightBinnerStandard</c>).
	/// </summary>
	internal bool Shows( RenderObject obj )
	{
		if ( !ShowsSubWorld( obj ) ) return false;
		if ( Capture && (obj.HiddenInCaptures || obj.Overlay) ) return false;

		var render = RenderTags;
		var exclude = ExcludeTags;
		if ( render.Length == 0 && exclude.Length == 0 ) return true;

		var tags = obj.Tags;
		if ( render.Length > 0 && !(obj.IsWorld ? Contains( render, WorldTag ) : Overlaps( render, tags )) ) return false;
		if ( exclude.Length > 0 && ((obj.IsWorld && Contains( exclude, WorldTag )) || Overlaps( exclude, tags )) ) return false;
		return true;
	}

	/// <summary>
	/// Whether the view shows the object's sub-world: always for the world's own objects.
	/// </summary>
	internal bool ShowsSubWorld( RenderObject obj )
	{
		var subWorld = obj.SubWorld;
		return subWorld == 0 || (SubWorlds & (1UL << (subWorld - 1))) != 0;
	}

	static bool Contains( uint[] set, uint tag ) => Array.IndexOf( set, tag ) >= 0;

	static bool Overlaps( uint[] set, uint[] tags )
	{
		foreach ( var tag in tags )
		{
			if ( Contains( set, tag ) ) return true;
		}

		return false;
	}

	/// <summary>
	/// Rotation-only view matrix. Shaders apply <c>g_vWorldToCameraOffset</c> first to preserve large-world precision.
	/// </summary>
	internal Matrix WorldToViewRotation { get; private set; }
	internal Matrix ViewToProjection { get; private set; }

	/// <summary>
	/// <see cref="ViewToProjection"/> with its depth made again from <see cref="ZNear"/> and <see cref="ZFar"/>, so its near plane
	/// is square to the view: what shadow cascades and light clusters fit, since an oblique near plane points another way. The
	/// same matrix unless there's a <see cref="Projection"/>.
	/// </summary>
	internal Matrix SquareViewToProjection { get; private set; }

	/// <summary>
	/// Full world to projection including the camera translation - what culling uses.
	/// </summary>
	internal Matrix WorldToProjection { get; private set; }

	internal ViewFrustum Frustum { get; private set; }

	/// <summary>
	/// Previous projection for reprojection (<c>CSceneView::GetPreviousFrameFrustum</c>); current projection on first use.
	/// </summary>
	internal Matrix PreviousWorldToProjection { get; private set; }

	bool _updated;

	/// <summary>
	/// Rebuild the matrices and frustum from the camera. Run once per frame, before collecting.
	/// </summary>
	internal void Update()
	{
		var previous = WorldToProjection;

		var forward = Rotation.Forward;
		var right = Rotation.Right;
		var up = Rotation.Up;

		// View space is right-handed: +x right, +y up, looking down -z
		WorldToViewRotation = new(
			right.x, up.x, -forward.x, 0,
			right.y, up.y, -forward.y, 0,
			right.z, up.z, -forward.z, 0,
			0, 0, 0, 1 );

		var aspect = Viewport.Width > 0 && Viewport.Height > 0 ? Viewport.Width / Viewport.Height : 1.0f;
		if ( Projection is { } custom )
		{
			ViewToProjection = FromEngineProjection( custom );
			SquareViewToProjection = SquareDepth( ViewToProjection, ZNear, ZFar );
		}
		else
		{
			ViewToProjection = Orthographic
				? ReverseZOrthographic( OrthoSize.x, OrthoSize.y, ZNear, ZFar )
				: ReverseZPerspective( FieldOfView, aspect, ZNear, ZFar );

			if ( ClipSpaceBounds != FullClipSpace ) ViewToProjection *= ClipSpaceRemap( ClipSpaceBounds );
			SquareViewToProjection = ViewToProjection;
		}

		var translation = Matrix.CreateTranslation( -Position );
		WorldToProjection = translation * WorldToViewRotation * ViewToProjection;
		Frustum = ViewFrustum.FromReverseZ( WorldToProjection );

		PreviousWorldToProjection = _updated ? previous : WorldToProjection;
		_updated = true;
	}

	/// <summary>
	/// Row-vector reverse-Z perspective: near 1, far 0 (<c>CRenderDeviceVulkan</c>).
	/// </summary>
	static Matrix ReverseZPerspective( float horizontalFovDegrees, float aspect, float zNear, float zFar )
	{
		var xScale = 1.0f / MathF.Tan( horizontalFovDegrees.DegreeToRadian() * 0.5f );
		var yScale = xScale * aspect;
		var range = zFar - zNear;

		return new(
			xScale, 0, 0, 0,
			0, yScale, 0, 0,
			0, 0, zNear / range, -1,
			0, 0, zFar * zNear / range, 0 );
	}

	/// <summary>
	/// Reverse-Z parallel projection, near plane at depth 1 and far at 0, row-vector convention.
	/// </summary>
	static Matrix ReverseZOrthographic( float width, float height, float zNear, float zFar )
	{
		var range = zFar - zNear;

		return new(
			2.0f / width, 0, 0, 0,
			0, 2.0f / height, 0, 0,
			0, 0, 1.0f / range, 0,
			0, 0, zFar / range, 1 );
	}

	/// <summary>
	/// The engine's column-vector, forward-Z projection as this one's row-vector reverse-Z: transposed, then depth turned
	/// round (native's <c>ComputeZReversalProjectionMatrix</c>, z' = w - z).
	/// </summary>
	internal static Matrix FromEngineProjection( in Matrix projection ) => projection.Transpose() * new Matrix(
		1, 0, 0, 0,
		0, 1, 0, 0,
		0, 0, -1, 0,
		0, 0, 1, 1 );

	/// <summary>
	/// A projection's depth made again from the near and far planes, square to the view, keeping its x, y and w: reverse-Z,
	/// 1 at the near plane and 0 at the far. Perspective when w comes from view depth, parallel otherwise.
	/// </summary>
	internal static Matrix SquareDepth( Matrix projection, float zNear, float zFar )
	{
		var range = zFar - zNear;
		var w = projection.M34 != 0 ? -projection.M34 * zNear : projection.M44;
		var scale = w / range;

		projection.M13 = 0;
		projection.M23 = 0;
		projection.M33 = scale;
		projection.M43 = scale * zFar;
		return projection;
	}

	/// <summary>
	/// Maps the clip-space rectangle (left, bottom, right, top) onto the whole of clip space: what native's off-centre
	/// projections do (<c>ComputeProjectionMatrix</c>, <c>OrthoMatrixOffCenterRH</c> with clip-space bounds).
	/// </summary>
	internal static Matrix ClipSpaceRemap( Vector4 bounds )
	{
		var width = bounds.z - bounds.x;
		var height = bounds.w - bounds.y;

		return new Matrix(
			2.0f / width, 0, 0, 0,
			0, 2.0f / height, 0, 0,
			0, 0, 1, 0,
			-(bounds.x + bounds.z) / width, -(bounds.y + bounds.w) / height, 0, 1 );
	}

	/// <summary>
	/// World-to-texture matrix for shadow lookup: UVs in [0, 1], Y flipped, full translation.
	/// </summary>
	internal Matrix WorldToTexture => WorldToProjection * new Matrix(
		0.5f, 0, 0, 0,
		0, -0.5f, 0, 0,
		0, 0, 1, 0,
		0.5f, 0.5f, 0, 1 );

	/// <summary>
	/// Fill shader view constants for the target size, MSAA count and depth range (<c>CSceneSystem::FillOutViewConstants</c>).
	/// </summary>
	internal void FillConstants( ref ViewConstants c, Vector2 targetSize, float time, int samples = 1, float minZ = 0, float maxZ = 1 )
		=> FillConstants( ref c, Viewport, targetSize, time, samples, minZ, maxZ );

	/// <summary>
	/// Fill constants for a pass-specific viewport and target (<c>CreatePerLayerViewConstants</c>). With <c>square</c>, from
	/// <see cref="SquareViewToProjection"/>, for compute that fits the view's volume.
	/// </summary>
	internal void FillConstants( ref ViewConstants c, Rect viewport, Vector2 targetSize, float time, int samples = 1, float minZ = 0, float maxZ = 1, bool square = false )
	{
		var viewToProjection = square ? SquareViewToProjection : ViewToProjection;
		var worldToProjection = WorldToViewRotation * viewToProjection;
		var projectionToWorld = worldToProjection.Inverted;
		var projectionToView = viewToProjection.Inverted;

		var translation = Matrix.CreateTranslation( -Position );

		c.WorldToProjection = worldToProjection;
		c.ProjectionToWorld = projectionToWorld;
		c.WorldToView = translation * WorldToViewRotation;
		c.ViewToProjection = viewToProjection;
		c.ProjectionToView = projectionToView;
		c.InvProjRow3 = new( projectionToView.M14, projectionToView.M24, projectionToView.M34, projectionToView.M44 );

		c.ToneMapScalarLinear = ToneMapScalar;
		c.LightMapScalar = 1.0f;
		c.EnvMapScalar = 1.0f;
		c.ToneMapScalarGamma = 1.0f;

		c.CameraPositionWs = Position;
		c.CameraDirWs = Rotation.Forward;
		c.CameraUpDirWs = Rotation.Up;
		c.Time = time;

		c.NearPlane = ZNear;
		c.FarPlane = ZFar;
		c.CameraFov = FieldOfView.DegreeToRadian();
		c.DepthPsToVsConversion = new( ZNear * ZFar, ZFar - ZNear, -ZFar );

		c.ViewportMinZ = minZ;
		c.ViewportMaxZ = maxZ;
		c.InvViewportSize = new( 1.0f / viewport.Width, 1.0f / viewport.Height );
		c.ViewportOffset = new( viewport.Left, viewport.Top );
		c.ViewportSize = new( viewport.Width, viewport.Height );
		c.RenderTargetSize = targetSize;
		c.ViewportToGBufferRatio = new( viewport.Width / targetSize.x, viewport.Height / targetSize.y );
		c.InvGBufferSize = new( 1.0f / targetSize.x, 1.0f / targetSize.y, 1.0f, 1.0f );

		c.Mod2xIdentity = 0.5f;
		c.RoughnessParams = new( 1, 1 );
		c.MsaaSampleCount = samples;

		var angles = Rotation.Angles();
		c.CameraAngles = new( angles.pitch.DegreeToRadian(), angles.yaw.DegreeToRadian(), angles.roll.DegreeToRadian(), 0 );
		c.WorldToCameraOffset = new( -Position, 0 );

		// Glass sampling and reprojection constants (CreatePerLayerViewConstants), in row-vector layout.
		c.FrameBufferCopyInvSizeAndUvScale = new( 1.0f / viewport.Width, 1.0f / viewport.Height, 1, 1 );

		var w = 0.5f * viewport.Width;
		var h = 0.5f * viewport.Height;
		var screen = new Matrix(
			w, 0, 0, 0,
			0, -h, 0, 0,
			0, 0, 1, 0,
			w, h, 0, 1 );
		var viewToWorld = c.WorldToView.Inverted;
		c.ViewToScreen = viewToProjection * screen;
		c.CurrFrameViewToPrevFrameProj = viewToWorld * PreviousWorldToProjection * screen;

		// The previous frame's inverse view projection, from native's forward-Z one (GetInvViewProjTranspose): depth w - z
		var forwardZ = new Matrix(
			1, 0, 0, 0,
			0, 1, 0, 0,
			0, 0, -1, 0,
			0, 0, 1, 1 );
		c.PrevProjectionToWorld = (PreviousWorldToProjection * forwardZ).Inverted;
	}
}
