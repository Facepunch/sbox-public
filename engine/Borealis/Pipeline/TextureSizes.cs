namespace Borealis;

/// <summary>
/// The size in texels a draw's textures appear at in a view, which texture streaming takes as the mips they need: native's
/// <c>CMeshDrawPrimitive_t::m_nRequiredTextureSize</c>, worked out the way <c>ISceneObjectDesc::GeneratePrimitivesArgs_t</c>
/// does. A draw with a UV density (texture texels per world unit, compiled into the model) asks for that density times the
/// view's pixels per world unit at the object's distance; one without asks for the object's screen size in pixels, scaled up
/// (<see cref="RenderView.FallbackTextureLodScale"/>). Distance and screen size are the root view's, so a shadow view asks for
/// what the camera sees; the pixels per world unit are the view's own. The bind passes the size to <c>MarkUsed</c> for every
/// texture it binds, and streaming keeps each texture's largest.
/// </summary>
internal readonly struct TextureSizes
{
	/// <summary>
	/// Every mip of a texture: what a draw whose size isn't known asks for.
	/// </summary>
	public const int All = int.MaxValue;

	// Pixels per world unit at distance 1 (viewport width over twice the tangent of half the field of view), negative for an
	// orthographic view, whose native frustum's tangent is of half of its -1 degree field of view
	readonly float viewAreaScale;
	readonly float fallbackScale;

	// The root view's camera and frustum, which distances and screen sizes are measured from
	readonly Vector3 camera;
	readonly float tanHalfFov;
	readonly float orthoWidth;
	readonly bool orthographic;

	public TextureSizes( RenderView view, RenderView root )
	{
		var width = (float)(int)view.Viewport.Width;
		viewAreaScale = view.Orthographic ? -1 : width * root.TextureLodScale / (2.0f * MathF.Tan( view.FieldOfView.DegreeToRadian() * 0.5f ));
		fallbackScale = width * root.FallbackTextureLodScale;

		camera = root.Position;
		tanHalfFov = MathF.Tan( root.FieldOfView.DegreeToRadian() * 0.5f );
		orthoWidth = root.OrthoSize.x;
		orthographic = root.Orthographic;
	}

	/// <summary>
	/// An object's sizes from its world bounds: <see cref="ForDraw"/>'s scale and fallback for its draws.
	/// </summary>
	public (float Scale, float Fallback) ForObject( Vector3 center, Vector3 extents )
	{
		// CalcSqrDistanceToAABB
		var outside = Vector3.Max( (center - camera).Abs() - extents, Vector3.Zero );
		var distance = MathF.Sqrt( outside.LengthSquared );
		var scale = viewAreaScale < 0 ? -1 : viewAreaScale / MathF.Max( distance, 1.0f );
		return (scale, ScreenSize( center, extents.Length ) * fallbackScale);
	}

	/// <summary>
	/// <c>CFrustum::ComputeScreenSize</c> on the root view: a sphere's diameter over the view's width at its distance.
	/// </summary>
	float ScreenSize( Vector3 center, float radius )
	{
		if ( orthographic ) return orthoWidth > 0 ? Math.Clamp( 2.0f * radius / orthoWidth, 0, 1 ) : 1;

		var distance = center.Distance( camera );
		return distance < radius ? 1.0f : Math.Clamp( radius / (distance * tanHalfFov), 0, 1 );
	}

	/// <summary>
	/// An aggregate's size: one for all its fragments, the largest any visible fragment asks for, as
	/// <c>CAggregateSceneObjectDesc::GeneratePrimitives</c> works it out. Fragments with a UV density are measured from
	/// <paramref name="fragmentCamera"/>, which is the view's own camera there; the rest take the whole object's screen size.
	/// An orthographic view's negative scale never raises the largest there, so its fragments with a UV density ask for none.
	/// </summary>
	public float ForFragment( float uvDensity, Vector3 center, Vector3 extents, Vector3 fragmentCamera, float objectFallback )
	{
		if ( uvDensity <= 0 ) return objectFallback;
		if ( viewAreaScale < 0 ) return 0;

		var outside = Vector3.Max( (center - fragmentCamera).Abs() - extents, Vector3.Zero );
		return uvDensity * viewAreaScale / MathF.Max( MathF.Sqrt( outside.LengthSquared ), 1.0f );
	}

	/// <summary>
	/// A draw's size in texels from its UV density and its object's (or run's) <see cref="ForObject"/> sizes: all mips when
	/// the scale is unknown (an orthographic view's, or none worked out).
	/// </summary>
	public static int ForDraw( float uvDensity, float scale, float fallback )
	{
		if ( uvDensity > 0 )
			return scale < 0 ? All : Clamp( uvDensity * scale );

		return Clamp( fallback );
	}

	/// <summary>
	/// A size as native's uint16 takes it.
	/// </summary>
	public static int Clamp( float size ) => (int)Math.Clamp( size, 0, ushort.MaxValue );
}
