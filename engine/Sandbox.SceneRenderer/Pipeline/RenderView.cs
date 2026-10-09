namespace Sandbox.SceneRenderer;

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
	/// Draw the depth prepass into a normals and roughness G-buffer too (native's <c>DepthNormalPrepassLayer</c>), for the
	/// camera's AO and SSR. Native always does; here it's only for cameras whose effects read it.
	/// </summary>
	public bool DepthNormals { get; set; }

	/// <summary>
	/// Optional camera and scene attributes, merged before pipeline overrides (<c>CCameraRenderer::CreateView</c>).
	/// </summary>
	internal RenderAttributes CameraAttributes { get; set; }

	/// <summary>
	/// Shader exposure scale (<c>g_flToneMapScalarLinear</c>); auto-exposure value or 1.
	/// </summary>
	public float ToneMapScalar { get; set; } = 1.0f;

	/// <summary>
	/// Optional Graphics view supplied by the caller for engine drawing code.
	/// </summary>
	internal Graphics.ManagedView GraphicsView { get; set; }

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

	static readonly uint WorldTag = new StringToken( "world" ).Value;

	/// <summary>
	/// Apply include/exclude tags, treating world objects as tagged <c>world</c>
	/// (<c>CSceneSystem</c>, <c>CLightBinnerStandard</c>).
	/// </summary>
	internal bool Shows( RenderObject obj )
	{
		var render = RenderTags;
		var exclude = ExcludeTags;
		if ( render.Length == 0 && exclude.Length == 0 ) return true;

		var tags = obj.Tags;
		if ( render.Length > 0 && !(obj.IsWorld ? Contains( render, WorldTag ) : Overlaps( render, tags )) ) return false;
		if ( exclude.Length > 0 && ((obj.IsWorld && Contains( exclude, WorldTag )) || Overlaps( exclude, tags )) ) return false;
		return true;
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
		ViewToProjection = Orthographic
			? ReverseZOrthographic( OrthoSize.x, OrthoSize.y, ZNear, ZFar )
			: ReverseZPerspective( FieldOfView, aspect, ZNear, ZFar );

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
	/// Fill constants for a pass-specific viewport and target (<c>CreatePerLayerViewConstants</c>).
	/// </summary>
	internal void FillConstants( ref ViewConstants c, Rect viewport, Vector2 targetSize, float time, int samples = 1, float minZ = 0, float maxZ = 1 )
	{
		var worldToProjection = WorldToViewRotation * ViewToProjection;
		var projectionToWorld = worldToProjection.Inverted;
		var projectionToView = ViewToProjection.Inverted;

		var translation = Matrix.CreateTranslation( -Position );

		c.WorldToProjection = worldToProjection;
		c.ProjectionToWorld = projectionToWorld;
		c.WorldToView = translation * WorldToViewRotation;
		c.ViewToProjection = ViewToProjection;
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
		c.ViewToScreen = ViewToProjection * screen;
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
