using Editor.MeshEditor;
using System.Text.Json.Serialization;

namespace Editor.RectEditor;

public partial class Document
{

	public class MeshRectangle : Rectangle
	{
		[Hide, JsonIgnore]
		public override bool CanDelete => false;

		[Hide, JsonIgnore]
		public MeshFace[] MeshFaces { get; set; }

		[Hide, JsonIgnore]
		public List<Vector2> UnwrappedVertexPositions { get; set; } = new();

		[Hide, JsonIgnore]
		public List<List<int>> FaceVertexIndices { get; set; } = new();

		[Hide, JsonIgnore]
		public List<Vector3> OriginalVertexPositions { get; set; } = new();

		[Hide, JsonIgnore]
		public MappingMode PreviousMappingMode { get; private set; } = MappingMode.UnwrapSquare;

		[Hide, JsonIgnore]
		public List<Vector2> OriginalUVs { get; private set; } = new();

		[Hide, JsonIgnore]
		public int AlignEdgeVertexA { get; set; } = -1;

		[Hide, JsonIgnore]
		public int AlignEdgeVertexB { get; set; } = -1;

		[Hide, JsonIgnore]
		public (int vertexA, int vertexB) HoveredEdge { get; set; } = (-1, -1);

		[Hide, JsonIgnore]
		public List<Vector2> UnwrappedVertexPositionsWorldSpace { get; set; } = new();

		public MeshRectangle( Window window ) : base( window )
		{
		}

		public MeshRectangle( Window window, MeshFace[] meshFaces ) : base( window )
		{
			MeshFaces = meshFaces;
			StoreOriginalUVs();

			var settings = Session?.Settings?.FastTextureSettings;
			if ( settings != null && settings.SavedRectMin != settings.SavedRectMax )
			{
				Min = settings.SavedRectMin;
				Max = settings.SavedRectMax;
			}
		}

		public override void OnPaint( RectView view )
		{
			var originalPen = Paint.Pen;
			Paint.SetPen( Color.White.WithAlpha( 0.8f ), 2 );
			var transformedPositions = GetRectangleRelativePositions();
			foreach ( var faceIndices in FaceVertexIndices )
			{
				var facePoints = new List<Vector2>();
				foreach ( var vertexIndex in faceIndices )
				{
					if ( vertexIndex < transformedPositions.Count )
					{
						var uv = transformedPositions[vertexIndex];
						var pixelPos = view.UVToPixel( uv );
						facePoints.Add( pixelPos );
					}
				}

				if ( facePoints.Count >= 3 )
				{
					Paint.SetBrush( Color.White.WithAlpha( 0.25f ) );
					Paint.DrawPolygon( facePoints.ToArray() );
				}
			}

			DrawIndexedLine( view, HoveredEdge.vertexA, HoveredEdge.vertexB, transformedPositions );
			DrawIndexedLine( view, AlignEdgeVertexA, AlignEdgeVertexB, transformedPositions, 0.5f );
			Paint.SetPen( originalPen );
		}

		private bool DrawIndexedLine( RectView view, int indexA, int indexB, List<Vector2> positions, float alpha = 1f )
		{
			if ( indexA >= 0 && indexA < positions.Count &&
				 indexB >= 0 && indexB < positions.Count )
			{
				var uvA = positions[indexA];
				var uvB = positions[indexB];
				var pixelA = view.UVToPixel( uvA );
				var pixelB = view.UVToPixel( uvB );

				Paint.SetPen( Color.Cyan.WithAlpha( alpha ), 2 );
				Paint.DrawLine( pixelA, pixelB );
				return true;
			}
			return false;
		}

		private void StoreOriginalUVs()
		{
			OriginalUVs.Clear();

			if ( MeshFaces == null || MeshFaces.Length == 0 )
				return;

			foreach ( var face in MeshFaces )
			{
				if ( !face.IsValid )
					continue;

				for ( int i = 0; i < face.TextureCoordinates.Length; i++ )
				{
					OriginalUVs.Add( face.TextureCoordinates[i] );
				}
			}
		}

		private void CalculateUVBounds()
		{
			if ( UnwrappedVertexPositions.Count == 0 )
				return;

			var min = UnwrappedVertexPositions[0];
			var max = UnwrappedVertexPositions[0];

			foreach ( var pos in UnwrappedVertexPositions )
			{
				min = Vector2.Min( min, pos );
				max = Vector2.Max( max, pos );
			}

			Min = min;
			Max = max;
		}

		private void SaveBoundsToSettings()
		{
			var settings = Session?.Settings?.FastTextureSettings;
			if ( settings != null )
			{
				settings.SavedRectMin = Min;
				settings.SavedRectMax = Max;
			}
		}

		public void ApplyMapping( FastTextureSettings settings, bool resetBoundsFromUseExisting = false )
		{
			if ( MeshFaces == null || MeshFaces.Length == 0 )
				return;

			var previousBounds = (Min, Max);
			var currentMapping = settings.Mapping;

			if ( resetBoundsFromUseExisting )
			{
				Min = Vector2.Zero;
				Max = Vector2.One;
				previousBounds = (Min, Max);
			}

			switch ( currentMapping )
			{
				case MappingMode.UnwrapSquare:
				case MappingMode.UnwrapConforming:
					BuildUnwrappedMesh( currentMapping );
					break;
				case MappingMode.Planar:
					var cameraRot = SceneViewWidget.Current.LastSelectedViewportWidget.State.CameraRotation;
					BuildUnwrappedMeshWithPlanarMapping( cameraRot.Left, cameraRot.Up );
					break;
				case MappingMode.UseExisting:
					BuildUnwrappedMeshFromExistingUVs();
					break;
			}

			// Apply edge alignment if an edge was picked with "Pick Edge"
			bool hasEdgeAlignment = AlignEdgeVertexA >= 0 && AlignEdgeVertexB >= 0 && currentMapping != MappingMode.UseExisting;
			if ( hasEdgeAlignment )
			{
				ApplyEdgeAlignment( settings.Alignment );
			}

			ApplyFastTextureTransforms( settings, hasEdgeAlignment );
			if ( currentMapping == MappingMode.UseExisting )
			{
				CalculateUVBounds();
			}
			else if ( !resetBoundsFromUseExisting )
			{
				Min = previousBounds.Min;
				Max = previousBounds.Max;
			}

			SaveBoundsToSettings();

			PreviousMappingMode = currentMapping;
		}

		private void ApplyFastTextureTransforms( FastTextureSettings settings, bool hasEdgeAlignment )
		{
			if ( UnwrappedVertexPositions.Count == 0 )
				return;

			// Only apply V-axis rotation if no edge alignment (edge alignment handles axis orientation itself)
			if ( settings.Alignment == AlignmentMode.VAxis && !hasEdgeAlignment )
			{
				var center = GetUnwrappedMeshCenter();
				for ( int i = 0; i < UnwrappedVertexPositions.Count; i++ )
				{
					var pos = UnwrappedVertexPositions[i];
					var relative = pos - center;
					var rotated = new Vector2( -relative.y, relative.x );
					UnwrappedVertexPositions[i] = center + rotated;
				}
			}

			var flipHorizontal = settings.IsFlippedHorizontal;
			if ( settings.Mapping == MappingMode.UnwrapSquare ) flipHorizontal = !flipHorizontal;

			if ( flipHorizontal )
			{
				var bounds = GetUnwrappedMeshBounds();
				for ( int i = 0; i < UnwrappedVertexPositions.Count; i++ )
				{
					var pos = UnwrappedVertexPositions[i];
					pos.x = bounds.min.x + bounds.max.x - pos.x;
					UnwrappedVertexPositions[i] = pos;
				}
			}

			if ( settings.IsFlippedVertical )
			{
				var bounds = GetUnwrappedMeshBounds();
				for ( int i = 0; i < UnwrappedVertexPositions.Count; i++ )
				{
					var pos = UnwrappedVertexPositions[i];
					pos.y = bounds.min.y + bounds.max.y - pos.y;
					UnwrappedVertexPositions[i] = pos;
				}
			}
		}

		private Vector2 GetUnwrappedMeshCenter()
		{
			if ( UnwrappedVertexPositions.Count == 0 )
				return Vector2.Zero;

			var sum = Vector2.Zero;
			foreach ( var pos in UnwrappedVertexPositions )
			{
				sum += pos;
			}
			return sum / UnwrappedVertexPositions.Count;
		}

		private (Vector2 min, Vector2 max) GetUnwrappedMeshBounds()
		{
			if ( UnwrappedVertexPositions.Count == 0 )
				return (Vector2.Zero, Vector2.Zero);

			var min = UnwrappedVertexPositions[0];
			var max = UnwrappedVertexPositions[0];

			foreach ( var pos in UnwrappedVertexPositions )
			{
				min = Vector2.Min( min, pos );
				max = Vector2.Max( max, pos );
			}

			return (min, max);
		}

		private void BuildUnwrappedMesh( MappingMode mode )
		{
			if ( MeshFaces == null || MeshFaces.Length == 0 )
				return;

			UnwrappedVertexPositions.Clear();
			FaceVertexIndices.Clear();
			OriginalVertexPositions.Clear();

			var unwrapper = new EdgeAwareFaceUnwrapper( MeshFaces );
			var result = unwrapper.Unwrap( mode );

			UnwrappedVertexPositions.AddRange( result.VertexPositions );
			FaceVertexIndices.AddRange( result.FaceIndices );
			OriginalVertexPositions.AddRange( result.OriginalPositions );

			if ( AlignEdgeVertexA < 0 || AlignEdgeVertexB < 0 )
			{
				PickBestInitialAlignmentEdge();
			}

			NormalizeUnwrappedMeshToSquare();
		}

		private void PickBestInitialAlignmentEdge()
		{
			if ( FaceVertexIndices.Count == 0 || UnwrappedVertexPositions.Count == 0 )
				return;

			var edgeCounts = new Dictionary<(int, int), int>();

			foreach ( var faceIndices in FaceVertexIndices )
			{
				for ( int i = 0; i < faceIndices.Count; i++ )
				{
					var v1 = faceIndices[i];
					var v2 = faceIndices[(i + 1) % faceIndices.Count];

					var edge = v1 < v2 ? (v1, v2) : (v2, v1);

					if ( edgeCounts.ContainsKey( edge ) )
						edgeCounts[edge]++;
					else
						edgeCounts[edge] = 1;
				}
			}

			// Find the most horizontal boundary edge
			float bestHorizontalScore = -1f;
			int bestVertexA = -1;
			int bestVertexB = -1;

			foreach ( var kvp in edgeCounts )
			{
				if ( kvp.Value != 1 )
					continue;

				var (v1, v2) = kvp.Key;
				if ( v1 >= UnwrappedVertexPositions.Count || v2 >= UnwrappedVertexPositions.Count )
					continue;

				var pos1 = UnwrappedVertexPositions[v1];
				var pos2 = UnwrappedVertexPositions[v2];
				var edgeDir = (pos2 - pos1).Normal;

				float horizontalScore = MathF.Abs( edgeDir.x );

				if ( horizontalScore > bestHorizontalScore )
				{
					bestHorizontalScore = horizontalScore;

					if ( pos1.x < pos2.x )
					{
						bestVertexA = v1;
						bestVertexB = v2;
					}
					else
					{
						bestVertexA = v2;
						bestVertexB = v1;
					}
				}
			}

			if ( bestVertexA >= 0 && bestVertexB >= 0 )
			{
				AlignEdgeVertexA = bestVertexA;
				AlignEdgeVertexB = bestVertexB;
			}
		}

		private void BuildUnwrappedMeshFromExistingUVs()
		{
			if ( MeshFaces == null || MeshFaces.Length == 0 || OriginalUVs.Count == 0 )
				return;

			UnwrappedVertexPositions.Clear();
			FaceVertexIndices.Clear();
			OriginalVertexPositions.Clear();

			int originalUVIndex = 0;
			foreach ( var face in MeshFaces )
			{
				var faceIndices = new List<int>();

				if ( !face.IsValid )
					continue;

				var vertices = face.Component.Mesh.GetFaceVertices( face.Handle );

				for ( int i = 0; i < face.TextureCoordinates.Length && originalUVIndex < OriginalUVs.Count; i++, originalUVIndex++ )
				{
					var originalUV = OriginalUVs[originalUVIndex];
					var vertex3D = i < vertices.Length ? face.Component.Mesh.GetVertexPosition( vertices[i] ) : Vector3.Zero;

					UnwrappedVertexPositions.Add( originalUV );
					OriginalVertexPositions.Add( vertex3D );
					faceIndices.Add( UnwrappedVertexPositions.Count - 1 );
				}

				FaceVertexIndices.Add( faceIndices );
			}

			CalculateUVBounds();
		}

		private void BuildUnwrappedMeshWithPlanarMapping( Vector3 cameraLeft, Vector3 cameraUp )
		{
			if ( MeshFaces == null || MeshFaces.Length == 0 )
				return;

			var axisU = -cameraLeft;
			var axisV = -cameraUp;

			UnwrappedVertexPositions.Clear();
			FaceVertexIndices.Clear();
			OriginalVertexPositions.Clear();

			foreach ( var face in MeshFaces )
			{
				var faceIndices = new List<int>();

				if ( !face.IsValid )
					continue;

				var vertices = face.Component.Mesh.GetFaceVertices( face.Handle );
				var worldTransform = face.Transform;

				for ( int i = 0; i < vertices.Length; i++ )
				{
					var localVertex3D = face.Component.Mesh.GetVertexPosition( vertices[i] );
					var worldVertex3D = worldTransform.PointToWorld( localVertex3D );

					var projectedUV = new Vector2(
						axisU.Dot( worldVertex3D ),
						axisV.Dot( worldVertex3D )
					);

					UnwrappedVertexPositions.Add( projectedUV );
					OriginalVertexPositions.Add( localVertex3D );
					faceIndices.Add( UnwrappedVertexPositions.Count - 1 );
				}

				FaceVertexIndices.Add( faceIndices );
			}

			NormalizeUnwrappedMeshToSquare();
		}
		private void NormalizeUnwrappedMeshToSquare()
		{
			if ( UnwrappedVertexPositions.Count == 0 )
				return;

			UnwrappedVertexPositionsWorldSpace = new List<Vector2>( UnwrappedVertexPositions );

			var min = UnwrappedVertexPositions[0];
			var max = UnwrappedVertexPositions[0];

			foreach ( var pos in UnwrappedVertexPositions )
			{
				min = Vector2.Min( min, pos );
				max = Vector2.Max( max, pos );
			}

			var size = max - min;
			var maxDimension = MathF.Max( size.x, size.y );

			if ( maxDimension > 0 )
			{
				for ( int i = 0; i < UnwrappedVertexPositions.Count; i++ )
				{
					var pos = UnwrappedVertexPositions[i];
					pos = (pos - min) / maxDimension;
					UnwrappedVertexPositions[i] = pos;
				}
			}

			CalculateUVBounds();
		}

		/// <summary>
		/// Gets the wireframe lines for rendering the unwrapped mesh within the rectangle
		/// Returns lines in rectangle-relative coordinates (0-1)
		/// </summary>
		public List<(Vector2 start, Vector2 end)> GetWireframeLines()
		{
			var lines = new List<(Vector2 start, Vector2 end)>();

			if ( UnwrappedVertexPositions.Count == 0 || FaceVertexIndices.Count == 0 )
				return lines;

			var transformedPositions = GetRectangleRelativePositions();

			foreach ( var faceIndices in FaceVertexIndices )
			{
				for ( int i = 0; i < faceIndices.Count; i++ )
				{
					var currentIndex = faceIndices[i];
					var nextIndex = faceIndices[(i + 1) % faceIndices.Count]; // Wrap back around to first vertex

					if ( currentIndex < transformedPositions.Count && nextIndex < transformedPositions.Count )
					{
						var start = transformedPositions[currentIndex];
						var end = transformedPositions[nextIndex];
						lines.Add( (start, end) );
					}
				}
			}

			return lines;
		}

		/// <summary>
		/// Gets the material's world space mapping dimensions.
		/// </summary>
		private (float width, float height) GetMaterialWorldScale()
		{
			var material = Material.Load( Session?.Settings.ReferenceMaterial );
			if ( material == null )
				return (512.0f, 512.0f);

			var worldMappingWidth = material.Attributes.GetInt( "WorldMappingWidth", 0 );
			var worldMappingHeight = material.Attributes.GetInt( "WorldMappingHeight", 0 );

			var texture = material.FirstTexture;
			if ( texture == null )
			{
				if ( worldMappingWidth > 0 && worldMappingHeight > 0 )
					return (worldMappingWidth, worldMappingHeight);
				return (512.0f, 512.0f);
			}

			var textureWidth = texture.Size.x;
			var textureHeight = texture.Size.y;

			float mappingWidth;
			float mappingHeight;

			if ( worldMappingWidth > 0 )
				mappingWidth = worldMappingWidth;
			else
				mappingWidth = 0.25f * textureWidth;

			if ( worldMappingHeight > 0 )
				mappingHeight = worldMappingHeight;
			else
				mappingHeight = 0.25f * textureHeight;

			if ( mappingWidth <= 0 )
				mappingWidth = 512.0f;
			if ( mappingHeight <= 0 )
				mappingHeight = 512.0f;

			return (mappingWidth, mappingHeight);
		}

		/// <summary>
		/// Transforms unwrapped vertex positions so they are relative to the current rectangle bounds
		/// </summary>
		public List<Vector2> GetRectangleRelativePositions()
		{
			var transformedPositions = new List<Vector2>();

			if ( UnwrappedVertexPositions.Count == 0 )
				return transformedPositions;

			var (unwrappedMin, unwrappedMax) = GetUnwrappedMeshBounds();
			var unwrappedSize = unwrappedMax - unwrappedMin;

			var rectSize = Max - Min;
			var imageSize = Session.GetImageSize();
			var settings = Session.Settings.FastTextureSettings;
			var insetUV = new Vector2(
				settings.InsetX / imageSize.x,
				settings.InsetY / imageSize.y
			);

			float tileU = 1.0f;
			float tileV = 1.0f;

			if ( settings.ScaleMode == ScaleMode.WorldScale )
			{
				var worldSpacePositions = UnwrappedVertexPositionsWorldSpace.Count > 0
					? UnwrappedVertexPositionsWorldSpace
					: UnwrappedVertexPositions;

				var minUV = worldSpacePositions[0];
				foreach ( var pos in worldSpacePositions )
				{
					minUV = Vector2.Min( minUV, pos );
				}

				var (mappingWidth, mappingHeight) = GetMaterialWorldScale();

				foreach ( var pos in worldSpacePositions )
				{
					var scaledX = ((pos.x - minUV.x) / mappingWidth) + Min.x;
					var scaledY = ((pos.y - minUV.y) / mappingHeight) + Min.y;

					transformedPositions.Add( new Vector2( scaledX, scaledY ) );
				}

				return transformedPositions;
			}

			if ( settings.ScaleMode != ScaleMode.Fit )
			{
				if ( settings.TileMode == TileMode.Repeat )
				{
					if ( settings.ScaleMode == ScaleMode.TileU )
						tileU = settings.Repeat;
					else if ( settings.ScaleMode == ScaleMode.TileV )
						tileV = settings.Repeat;
				}
				else if ( settings.TileMode == TileMode.MaintainAspect )
				{
					var meshWidth = MathF.Max( unwrappedSize.x, 0.001f );
					var meshHeight = MathF.Max( unwrappedSize.y, 0.001f );
					var meshAspect = meshWidth / meshHeight;

					var rectPixelWidth = MathF.Max( rectSize.x * imageSize.x, 0.001f );
					var rectPixelHeight = MathF.Max( rectSize.y * imageSize.y, 0.001f );
					var rectAspect = rectPixelWidth / rectPixelHeight;

					if ( settings.ScaleMode == ScaleMode.TileU )
					{
						tileU = meshAspect / rectAspect;
						tileV = 1.0f;
					}
					else if ( settings.ScaleMode == ScaleMode.TileV )
					{
						tileU = 1.0f;
						tileV = rectAspect / meshAspect;
					}
				}
			}

			foreach ( var pos in UnwrappedVertexPositions )
			{
				Vector2 relativePos;

				if ( unwrappedSize.x > 0 && unwrappedSize.y > 0 )
				{
					var normalized = (pos - unwrappedMin) / unwrappedSize;

					normalized.x *= tileU;
					normalized.y *= tileV;

					var insetMin = Min + insetUV;
					var insetSize = rectSize - insetUV * 2;
					relativePos = insetMin + normalized * insetSize;
				}
				else
				{
					relativePos = Min + insetUV + (pos - unwrappedMin);
				}

				transformedPositions.Add( relativePos );
			}

			return transformedPositions;
		}

		/// <summary>
		/// Find the edge closest to the given position and update HoveredEdge
		/// </summary>
		public bool FindHoveredEdge( Vector2 mousePos, float maxDistance = 0.02f )
		{
			if ( FaceVertexIndices.Count == 0 || UnwrappedVertexPositions.Count == 0 )
			{
				HoveredEdge = (-1, -1);
				return false;
			}

			var transformedPositions = GetRectangleRelativePositions();
			float closestDistance = maxDistance;
			int bestVertexA = -1;
			int bestVertexB = -1;

			foreach ( var faceIndices in FaceVertexIndices )
			{
				for ( int i = 0; i < faceIndices.Count; i++ )
				{
					var currentIndex = faceIndices[i];
					var nextIndex = faceIndices[(i + 1) % faceIndices.Count];

					if ( currentIndex < transformedPositions.Count && nextIndex < transformedPositions.Count )
					{
						var edgeStart = transformedPositions[currentIndex];
						var edgeEnd = transformedPositions[nextIndex];

						var distance = DistanceToLineSegment( mousePos, edgeStart, edgeEnd );

						if ( distance < closestDistance )
						{
							closestDistance = distance;
							bestVertexA = currentIndex;
							bestVertexB = nextIndex;
						}
					}
				}
			}

			HoveredEdge = (bestVertexA, bestVertexB);
			return bestVertexA >= 0 && bestVertexB >= 0;
		}

		/// <summary>
		/// Pick an edge to align the UV mapping to based on a click position
		/// </summary>
		public bool PickAlignmentEdge( Vector2 clickPos, float maxDistance = 10f )
		{
			if ( FaceVertexIndices.Count == 0 || UnwrappedVertexPositions.Count == 0 )
				return false;

			var transformedPositions = GetRectangleRelativePositions();
			float closestDistance = maxDistance;
			int bestVertexA = -1;
			int bestVertexB = -1;

			foreach ( var faceIndices in FaceVertexIndices )
			{
				for ( int i = 0; i < faceIndices.Count; i++ )
				{
					var currentIndex = faceIndices[i];
					var nextIndex = faceIndices[(i + 1) % faceIndices.Count];

					if ( currentIndex < transformedPositions.Count && nextIndex < transformedPositions.Count )
					{
						var edgeStart = transformedPositions[currentIndex];
						var edgeEnd = transformedPositions[nextIndex];

						var distance = DistanceToLineSegment( clickPos, edgeStart, edgeEnd );

						if ( distance < closestDistance )
						{
							closestDistance = distance;
							bestVertexA = currentIndex;
							bestVertexB = nextIndex;
						}
					}
				}
			}

			if ( bestVertexA >= 0 && bestVertexB >= 0 )
			{
				AlignEdgeVertexA = bestVertexA;
				AlignEdgeVertexB = bestVertexB;
				return true;
			}

			return false;
		}

		private float DistanceToLineSegment( Vector2 point, Vector2 lineStart, Vector2 lineEnd )
		{
			var lineVec = lineEnd - lineStart;
			var pointVec = point - lineStart;
			var lineLength = lineVec.Length;

			if ( lineLength < 0.0001f )
				return point.Distance( lineStart );

			var t = MathF.Max( 0, MathF.Min( 1, pointVec.Dot( lineVec ) / (lineLength * lineLength) ) );
			var projection = lineStart + t * lineVec;
			return point.Distance( projection );
		}

		/// <summary>
		/// Apply alignment rotation to UVs based on the picked edge
		/// </summary>
		public void ApplyEdgeAlignment( AlignmentMode alignmentMode )
		{
			if ( AlignEdgeVertexA < 0 || AlignEdgeVertexB < 0 || UnwrappedVertexPositions.Count == 0 )
				return;

			if ( AlignEdgeVertexA >= UnwrappedVertexPositions.Count || AlignEdgeVertexB >= UnwrappedVertexPositions.Count )
				return;

			var uvA = UnwrappedVertexPositions[AlignEdgeVertexA];
			var uvB = UnwrappedVertexPositions[AlignEdgeVertexB];

			Vector2 axisU;
			Vector2 axisV;

			if ( alignmentMode == AlignmentMode.UAxis )
			{
				axisU = (uvB - uvA).Normal;
				axisV = new Vector2( -axisU.y, axisU.x );
			}
			else
			{
				axisV = (uvB - uvA).Normal;
				axisU = new Vector2( axisV.y, -axisV.x );
			}

			for ( int i = 0; i < UnwrappedVertexPositions.Count; i++ )
			{
				var toVertex = UnwrappedVertexPositions[i] - uvA;
				UnwrappedVertexPositions[i] = new Vector2( axisU.Dot( toVertex ), axisV.Dot( toVertex ) );
			}
		}
	}
}
