namespace Sandbox;

public static partial class Gizmo
{
	static GizmoDraw _draw = new();

	/// <summary>
	/// Draw a shape using the gizmo library
	/// </summary>
	public static GizmoDraw Draw => _draw;

	/// <summary>
	/// Contains functions to add objects to the Gizmo Scene. This
	/// is an instantiable class so it's possible to add extensions.
	/// </summary>
	public sealed partial class GizmoDraw
	{
		static Material LineMaterial;
		static Material SolidMaterial;
		static Material SpriteMaterial;
		static Material GridMaterial;

		internal static void InitStatic()
		{
			LineMaterial = Material.Load( "materials/gizmo/line.vmat" );
			SolidMaterial = Material.Load( "materials/gizmo/solid.vmat" );
			SpriteMaterial = Material.Load( "materials/gizmo/sprite.vmat" );
			GridMaterial = Material.Load( "materials/gizmo/grid.vmat" );
		}

		internal static void DisposeStatic()
		{
			LineMaterial?.Dispose();
			LineMaterial = null;
			SolidMaterial?.Dispose();
			SolidMaterial = null;
			SpriteMaterial?.Dispose();
			SpriteMaterial = null;
			GridMaterial?.Dispose();
			GridMaterial = null;
		}

		internal GizmoDraw()
		{

		}

		internal Color32 Color32 => Active.scope.Color32;

		/// <summary>
		/// The color to render the next object
		/// </summary>
		public Color Color
		{
			get => Active.scope.Color;
			set
			{
				if ( Active.scope.Color == value )
					return;

				Active.scope.Color = value;
				Active.scope.Color32 = value;
			}
		}

		static VertexSceneObject _vertexObject;
		static string _vertexObjectPath;

		/// <summary>
		/// Ignore depth when drawing, draw on top of everything
		/// </summary>
		public bool IgnoreDepth
		{
			get => Active.scope.IgnoreDepth;
			set => Active.scope.IgnoreDepth = value;
		}

		/// <summary>
		/// The thickness of line drawings
		/// </summary>
		public float LineThickness
		{
			get => Active.scope.LineThickness;
			set => Active.scope.LineThickness = value;
		}

		/// <summary>
		/// Don't draw backfaces when drawing solids
		/// </summary>
		public bool CullBackfaces
		{
			get => Active.scope.CullBackfaces;
			set => Active.scope.CullBackfaces = value;
		}

		internal void Start()
		{
			End();

			IgnoreDepth = false;
			Color = Color.White;
			LineThickness = 1.0f;
		}

		internal void End()
		{
			// Push the vertex buffer to the GPU on end of scope
			_vertexObject?.Write();

			_vertexObject = default;
			_vertexObjectPath = default;
		}

		/// <summary>
		/// Draw a model
		/// </summary>
		public SceneModel Model( string modelName, Transform localTransform )
		{
			var model = Sandbox.Model.Load( modelName );
			if ( model == null ) model = Sandbox.Model.Load( "models/dev/error.vmdl" );

			return Model( model, localTransform );
		}

		/// <summary>
		/// Draw a model
		/// </summary>
		public SceneModel Model( string modelName ) => Model( modelName, Transform.Zero );

		/// <summary>
		/// Draw a model
		/// </summary>
		public SceneModel Model( Model model, Transform localTransform )
		{
			var tx = Transform.ToWorld( localTransform );
			var so = Active.FindOrCreate<SceneModel>( $"{model.Name}", () => new SceneModel( World, model, tx ) );
			so.Flags.CastShadows = false;
			so.Model = model;
			so.ColorTint = Color;
			so.Transform = tx;

			return so;
		}

		/// <summary>
		/// Draw a model
		/// </summary>
		public SceneModel Model( Model modelName ) => Model( modelName, Transform.Zero );



		/// <summary>
		/// Draw particles. Control points will be set to the transform position.
		/// </summary>
		[Obsolete]
		public void Particles( string modelName, Transform localTransform, float? updateSpeed = null )
		{
		}

		/// <summary>
		/// Draw particles. Control point 0 will be set to the transform position.
		/// </summary>
		[Obsolete]
		public void Particles( string modelName, float? updateSpeed = null )
		{
		}

		/// <summary>
		/// Draw text
		/// </summary>
		public void Text( string text, Transform tx, string font = "Roboto", float size = 12.0f, TextFlag flags = TextFlag.Center )
		{
			tx = Transform.ToWorld( tx );
			var so = Active.FindOrCreate( $"text", () => new TextSceneObject( World ) );

			so.TextBlock = new TextRendering.Scope( text, Color, size, font );
			so.Transform = tx;
			so.ScreenPos = Camera.ToScreen( tx.Position );
			so.Bounds = BBox.FromPositionAndSize( tx.Position, 50.0f );
			so.TextFlags = flags;
		}

		public void WorldText( string text, Transform tx, string font = "Roboto", float size = 12.0f, TextFlag flags = TextFlag.Center )
		{
			tx = Transform.ToWorld( tx );
			var so = Active.FindOrCreate( $"worldtext", () => new WorldTextSceneObject( World ) );

			so.Text = text;
			so.Color = Color;
			so.Transform = tx;
			so.Bounds = BBox.FromPositionAndSize( tx.Position, 1000.0f );
			so.FontName = font;
			so.FontSize = size;
			so.TextFlags = flags;
			so.IgnoreDepth = IgnoreDepth;
		}

		/// <summary>
		/// Draw text
		/// </summary>
		public void ScreenText( string text, Vector2 pos, string font = "Roboto", float size = 12.0f, TextFlag flags = TextFlag.LeftTop )
		{
			var so = Active.FindOrCreate( $"text", () => new TextSceneObject( World ) );

			so.TextBlock = new TextRendering.Scope( text, Color, size, font );
			so.Transform = Transform.Zero;
			so.ScreenPos = pos;
			so.Bounds = BBox.FromPositionAndSize( 0, float.MaxValue );
			so.TextFlags = flags;
		}

		/// <summary>
		/// Draw text on screen at a 3d position
		/// </summary>
		public void ScreenText( string text, Vector3 worldPos, Vector2 offset, string font = "Roboto", float size = 12.0f, TextFlag flags = TextFlag.LeftTop )
		{
			if ( !Camera.ToScreen( worldPos, out var screen ) )
				return;

			ScreenText( text, screen + offset, font, size, flags );
		}

		/// <summary>
		/// Draw a rect, on the screen
		/// </summary>
		public void ScreenRect( Rect rect, Color color, Vector4 borderRadius = default, Color borderColor = default, Vector4 borderSize = default, BlendMode blendMode = BlendMode.Normal )
		{
			var so = Active.FindOrCreate<GizmoInlineSceneObject>( $"screen-rect", () => new GizmoInlineSceneObject( World ) );

			so.RenderLayer = SceneRenderLayer.OverlayWithoutDepth;
			so.Action = () =>
			{
				so.Attributes.Set( "BoxPosition", new Vector2( rect.Left, rect.Top ) );
				so.Attributes.Set( "BoxSize", new Vector2( rect.Width, rect.Height ) );
				so.Attributes.Set( "BorderRadius", borderRadius );
				so.Attributes.Set( "Texture", Texture.White );
				so.Attributes.SetCombo( "D_BACKGROUND_IMAGE", 0 );
				so.Attributes.SetCombo( "D_BORDER_IMAGE", 0 );
				so.Attributes.SetComboEnum( "D_BLENDMODE", blendMode );

				if ( borderSize.Length != 0 )
				{
					so.Attributes.Set( "HasBorder", 1 );
					so.Attributes.Set( "BorderSize", borderSize );

					so.Attributes.Set( "BorderColorL", borderColor );
					so.Attributes.Set( "BorderColorT", borderColor );
					so.Attributes.Set( "BorderColorR", borderColor );
					so.Attributes.Set( "BorderColorB", borderColor );
				}
				else
				{
					so.Attributes.Set( "HasBorder", 0 );
				}

				Graphics.DrawQuad( rect, Material.UI.Box, color, so.Attributes );
			};
		}

		/// <summary>
		/// Draw text at an angle
		/// </summary>
		internal void ScreenText( string text, Vector2 pos, Vector2 clip, float angle, string font = "Roboto", float size = 12.0f, TextFlag flags = TextFlag.LeftTop )
		{
			var so = Active.FindOrCreate<TextSceneObject>( $"text", () => new TextSceneObject( World ) );

			so.TextBlock = new TextRendering.Scope( text, Color, size, font );
			so.ColorTint = Color;
			so.Transform = Transform.Zero;
			so.AngleDegrees = angle;
			so.ScreenPos = pos;
			so.ScreenSize = clip;
			so.Bounds = BBox.FromPositionAndSize( 0, float.MaxValue );
			so.TextFlags = flags;
		}

		/// <summary>
		/// Draw a plane
		/// </summary>
		public void Plane( Vector3 position, Vector3 normal )
		{
			using var scope = Scope( "plane", new Transform( position, Rotation.LookAt( normal ) ) );

			LineThickness = 1.0f;

			Line( Vector3.Left * 1000.0f, Vector3.Left * -1000.0f );
			Line( Vector3.Up * 1000.0f, Vector3.Up * -1000.0f );
			Line( (Vector3.Up + Vector3.Left).Normal * 1000.0f, (Vector3.Up + Vector3.Left).Normal * -1000.0f );
			Line( (Vector3.Down + Vector3.Left).Normal * 1000.0f, (Vector3.Down + Vector3.Left).Normal * -1000.0f );


			for ( int i = 0; i < 1000; i += 50 )
			{
				LineCircle( position, i, sections: 32 );
			}

		}

		/// <summary>
		/// Draw a line with an arrow on the end
		/// </summary>
		public void Arrow( Vector3 from, Vector3 to, float arrowLength = 12.0f, float arrowWidth = 5.0f )
		{
			var delta = to - from;
			var deltaNormal = delta.Normal;

			Line( from, to - deltaNormal * arrowLength );
			SolidCone( to - deltaNormal * arrowLength, deltaNormal * arrowLength, arrowWidth );
		}

		/// <summary>
		/// Draws a grid
		/// </summary>
		public void Grid( GridAxis axis, float spacing = 32, float opacity = 1.0f, float minorLineWidth = 0.01f, float majorLineWidth = 0.02f )
		{
			Grid( axis, new Vector2( spacing, spacing ), opacity, minorLineWidth, majorLineWidth );
		}

		/// <summary>
		/// Draws a grid
		/// </summary>
		public void Grid( GridAxis axis, Vector2 spacing = default, float opacity = 1.0f, float minorLineWidth = 0.01f, float majorLineWidth = 0.02f )
		{
			Grid( 0, axis, spacing, opacity, minorLineWidth, majorLineWidth );
		}

		/// <summary>
		/// Draws a grid centered at a position
		/// </summary>
		public void Grid( Vector3 center, GridAxis axis, Vector2 spacing = default, float opacity = 1.0f, float minorLineWidth = 0.01f, float majorLineWidth = 0.02f )
		{
			if ( spacing == default ) spacing = new Vector2( 32, 32 );
			var so = VertexObject( Graphics.PrimitiveType.Triangles, GridMaterial );

			so.Attributes.Set( "GridOrigin", center );
			so.Attributes.Set( "GridAxis", (int)axis );
			so.Attributes.Set( "GridScale", spacing );
			so.Attributes.Set( "MinorLineWidth", minorLineWidth );
			so.Attributes.Set( "MajorLineWidth", majorLineWidth );
			so.Attributes.Set( "AxisLineWidth", majorLineWidth + 0.01f );
			so.Attributes.Set( "MinorLineColor", new Vector4( 1, 1, 1, 0.5f * opacity ) );
			so.Attributes.Set( "MajorLineColor", new Vector4( 1, 1, 1, 0.8f * opacity ) );

			so.Attributes.Set( "XAxisColor", Colors.Forward );
			so.Attributes.Set( "YAxisColor", Colors.Left );
			so.Attributes.Set( "ZAxisColor", Colors.Up );
			so.Attributes.Set( "CenterColor", Color.White );
			so.Attributes.Set( "MajorGridDivisions", 16.0f );

			// Tessellating helps with depth bias precision
			// Generally 1x1 is enough for 8k x 8k, 2x2 for 16k x 16k and so on.
			// Obvious optimization here is to generate this mesh only once and to use a simpler Vertex format
			// But it barely matters
			int tessellationLevel = 4;
			float x = center.x;
			float y = center.y;
			float z = center.z;
			for ( int i = 0; i < tessellationLevel; i++ )
			{
				for ( int j = 0; j < tessellationLevel; j++ )
				{
					float x0 = i / (float)tessellationLevel;
					float x1 = (i + 1) / (float)tessellationLevel;
					float y0 = j / (float)tessellationLevel;
					float y1 = (j + 1) / (float)tessellationLevel;

					switch ( axis )
					{
						case GridAxis.XY:
							{
								so.Vertices.Add( new Vertex( new Vector3( x0, y0, z ) ) );
								so.Vertices.Add( new Vertex( new Vector3( x1, y0, z ) ) );
								so.Vertices.Add( new Vertex( new Vector3( x0, y1, z ) ) );

								so.Vertices.Add( new Vertex( new Vector3( x0, y1, z ) ) );
								so.Vertices.Add( new Vertex( new Vector3( x1, y0, z ) ) );
								so.Vertices.Add( new Vertex( new Vector3( x1, y1, z ) ) );
								break;
							}
						case GridAxis.YZ:
							{
								so.Vertices.Add( new Vertex( new Vector3( x, x0, y0 ) ) );
								so.Vertices.Add( new Vertex( new Vector3( x, x1, y0 ) ) );
								so.Vertices.Add( new Vertex( new Vector3( x, x0, y1 ) ) );

								so.Vertices.Add( new Vertex( new Vector3( x, x0, y1 ) ) );
								so.Vertices.Add( new Vertex( new Vector3( x, x1, y0 ) ) );
								so.Vertices.Add( new Vertex( new Vector3( x, x1, y1 ) ) );
								break;
							}
						case GridAxis.ZX:
							{
								so.Vertices.Add( new Vertex( new Vector3( x0, y, y0 ) ) );
								so.Vertices.Add( new Vertex( new Vector3( x1, y, y0 ) ) );
								so.Vertices.Add( new Vertex( new Vector3( x0, y, y1 ) ) );

								so.Vertices.Add( new Vertex( new Vector3( x0, y, y1 ) ) );
								so.Vertices.Add( new Vertex( new Vector3( x1, y, y0 ) ) );
								so.Vertices.Add( new Vertex( new Vector3( x1, y, y1 ) ) );
								break;
							}
						default: break;
					}
				}
			}
		}
	}
}
