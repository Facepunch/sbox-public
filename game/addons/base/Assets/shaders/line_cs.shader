HEADER
{
	DevShader = true;
	Description = "Compute vertices for line rendering with index buffer and caps";
}

MODES
{
	Default();
}

COMMON
{
	#include "common/shared.hlsl"
}

CS
{
	struct Point
	{
		uint Offset;
		float3 Position;
		float3 Normal;
		float4 Color;
		float Width;
		float TextureCoord;
	};

	struct Vertex
	{
		float3 Position;
		float3 Normal;
		float3 Tangent;
		float4 Color;
		float2 TextureCoord;
	};

	enum Face
	{
		Camera = 0,
		Normal = 1,
		Cylinder = 2
	};
	
	enum Cap
	{
		None = 0,
		Triangle = 1,
		Arrow = 2,
		Rounded = 3
	};

	static const float PI = 3.14159265359f;

	int FaceMode < Attribute("FaceMode"); Default(0); >;
	int StartCap < Attribute("StartCap"); Default(0); >;
	int EndCap < Attribute("EndCap"); Default(0); >;
	int RoundedCapSegments < Attribute("RoundedCapSegments"); Default(6); >;
	int TessellationLevel < Attribute("TessellationLevel"); Default(1); >;

	StructuredBuffer<Point> PointBuffer < Attribute("PointBuffer"); >;
	RWStructuredBuffer<Vertex> VertexBuffer < Attribute("VertexBuffer"); >;
	RWStructuredBuffer<uint> IndexBuffer < Attribute("IndexBuffer"); >;
	uint PointCount < Attribute("PointCount"); >;

	// Helper function to calculate common line orientation vectors
	void CalculateLineOrientation(Point prev, Point cur, Point next, out float3 normal, out float3 tangent, out float3 binormal)
	{
		if (FaceMode == Face::Camera)
		{
			normal = normalize(cur.Position - g_vCameraPositionWs);
		}
		else
		{
			// If a straight line, use cross section based on the first two points
			if ( PointCount == 2 )
			{
				float3 lineDir = normalize( next.Position - prev.Position );
				normal = cur.Normal;
				normal -= lineDir * dot( normal, lineDir );

				if ( length( normal ) < 1e-4 )
				{
					float3 up = float3( 0, 1, 0 );
					if ( abs( dot( lineDir, up ) ) > 0.9f)
						up = float3( 1, 0, 0 );

					normal = up - lineDir * dot( up, lineDir );
				}

				normal = normalize( normal );
			}
			else
				normal = normalize(cur.Normal);
		}


		float3 coming = prev.Position - cur.Position;
		float3 going = cur.Position - next.Position;

		if (FaceMode == Face::Camera)
		{
			coming -= normal * dot(coming, normal);
			going -= normal * dot(going, normal);
		}

		float3 average = normalize(normalize(coming) + normalize(going));
		tangent = normalize(cross(average, normal));
		binormal = normalize(cross(tangent, normal));
	}
	
	// Helper function to calculate vertex position and normal for cylindrical mode
	void CalculateCylindricalVertex(float3 position, float3 tangent, float3 normal, float width, float angle, out float3 vertexPosition, out float3 vertexNormal)
	{
		float cylinderRadius = width;
		float horizontalOffset = sin(angle) * cylinderRadius;
		float verticalOffset = cos(angle) * cylinderRadius;
		
		vertexNormal = normalize(tangent * sin(angle) + normal * cos(angle));
		vertexPosition = position + tangent * horizontalOffset + normal * verticalOffset;
	}

	void CalculateLineVertices(float width, Point prev, Point cur, Point next, out Vertex left, out Vertex right)
	{
		float3 normal, tangent, binormal;
		CalculateLineOrientation(prev, cur, next, normal, tangent, binormal);

		if (FaceMode == Face::Cylinder)
		{
			// Left edge
			float leftAngle = 0.0f;
			float3 leftPosition, leftSurfaceNormal;
			CalculateCylindricalVertex(cur.Position, tangent, normal, width, leftAngle, leftPosition, leftSurfaceNormal);
			
			left.TextureCoord = float2(0, cur.TextureCoord);
			left.Color = cur.Color;
			left.Normal = leftSurfaceNormal;
			left.Tangent = binormal;
			left.Position = leftPosition;

			// Right edge
			float rightAngle = PI;
			float3 rightPosition, rightSurfaceNormal;
			CalculateCylindricalVertex(cur.Position, tangent, normal, width, rightAngle, rightPosition, rightSurfaceNormal);
			
			right.TextureCoord = float2(1, cur.TextureCoord); // If the texture is stretched, maybe increase this to 3 ( roundest number close to pi ) to alleviate that
			right.Color = cur.Color;
			right.Normal = rightSurfaceNormal;
			right.Tangent = binormal;
			right.Position = rightPosition;
		}
		else
		{
			// Flat tessellation
			left.TextureCoord = float2(0, cur.TextureCoord);
			left.Color = cur.Color;
			left.Normal = -normal;
			left.Tangent = tangent;
			left.Position = cur.Position + tangent * width;

			right = left;
			right.TextureCoord.x = 1;
			right.Position = cur.Position - tangent * width;
		}
	}

	void GenerateTessellatedVertices(float width, Point prev, Point cur, Point next, uint baseVertexIndex)
	{
		float3 normal, tangent, binormal;
		CalculateLineOrientation(prev, cur, next, normal, tangent, binormal);

		// Generate tessellated vertices across the width
		// u goes from 0 (left edge) to 1 (right edge)
		for (uint t = 0; t <= TessellationLevel; t++)
		{
			float u = float(t) / float(TessellationLevel);
			
			Vertex vertex;
			vertex.TextureCoord = float2(u, cur.TextureCoord);
			vertex.Color = cur.Color;
			
			if (FaceMode == Face::Cylinder)
			{
				// Create a cylinder shape by using sine/cosine functions
				// Map u from [0,1] to [0, 2π] for cylinder
				float angle = u * PI * 2;
				
				float3 vertexPosition, vertexNormal;
				CalculateCylindricalVertex(cur.Position, tangent, normal, -width, angle, vertexPosition, vertexNormal);
				
				vertex.Normal = -vertexNormal; // Use cylindrical normal
				vertex.Tangent = normalize(cross(binormal, vertexNormal));
				vertex.Position = vertexPosition;
			}
			else
			{
				vertex.Normal = -normal;
				vertex.Tangent = tangent;
				vertex.Position = cur.Position + tangent * width * (1.0 - 2.0 * u);
			}
			
			VertexBuffer[baseVertexIndex + t] = vertex;
		}
	}

	uint GetCapVertexCount(int capType)
	{
		if (capType == Cap::None) return 0;
		if (capType == Cap::Triangle) return 1;
		if (capType == Cap::Arrow) return 3;
		if (capType == Cap::Rounded) return RoundedCapSegments + 2;
		return 0;
	}

	uint GetCapIndexCount(int capType)
	{
		if (capType == Cap::None) return 0;
		if (capType == Cap::Triangle) return 3;
		if (capType == Cap::Arrow) return 3;
		if (capType == Cap::Rounded) return RoundedCapSegments * 3;
		return 0;
	}

	uint GetCapVertexOffset()
	{
		return PointCount * (TessellationLevel + 1);
	}

	uint GetCapIndexOffset()
	{
		return (PointCount - 1) * TessellationLevel * 6;
	}

	void AddCap(uint vertexOffset, uint indexOffset, int capType, bool isStartCap)
	{
		if (capType == Cap::None || PointCount < 2) return;
		
		Point capPoint, adjacentPoint, extrapolatedPoint;
		uint baseVertexIndex;
		
		if (isStartCap)
		{
			capPoint = PointBuffer[0];
			adjacentPoint = PointBuffer[1];
			extrapolatedPoint = capPoint;
			extrapolatedPoint.Position = capPoint.Position + (capPoint.Position - adjacentPoint.Position);
			baseVertexIndex = 0;
		}
		else
		{
			capPoint = PointBuffer[PointCount - 1];
			adjacentPoint = PointBuffer[PointCount - 2];
			extrapolatedPoint = capPoint;
			extrapolatedPoint.Position = capPoint.Position + (capPoint.Position - adjacentPoint.Position);
			baseVertexIndex = (PointCount - 1) * (TessellationLevel + 1);
		}
		
		Vertex left = (Vertex)0;
		Vertex right = (Vertex)0;
		float width = capPoint.Width;
		
		if (isStartCap)
		{
			CalculateLineVertices(width, extrapolatedPoint, capPoint, adjacentPoint, left, right);
		}
		else
		{
			CalculateLineVertices(width, adjacentPoint, capPoint, extrapolatedPoint, left, right);
		}
		
		float3 direction = normalize(capPoint.Position - adjacentPoint.Position);
		float3 tangent = normalize(right.Position - left.Position);
		float halfWidth = length(right.Position - left.Position) * 0.5;
		float3 centerPoint = capPoint.Position;
		float3 normal = left.Normal;
		float baseY = capPoint.TextureCoord;
		float yOffsetDir = isStartCap ? -1.0 : 1.0;
		
		if (capType == Cap::Triangle)
		{
			Vertex capVertex = left;
			capVertex.Position = centerPoint + direction * halfWidth;
			capVertex.Tangent = tangent;
			capVertex.Normal = normal;
			capVertex.TextureCoord = float2(0.5, baseY + yOffsetDir * 0.1);
			
			VertexBuffer[vertexOffset] = capVertex;
			
			if (isStartCap)
			{
				IndexBuffer[indexOffset] = baseVertexIndex; // Left edge
				IndexBuffer[indexOffset + 1] = baseVertexIndex + TessellationLevel; // Right edge
				IndexBuffer[indexOffset + 2] = vertexOffset; // Cap vertex
			}
			else
			{
				IndexBuffer[indexOffset] = baseVertexIndex; // Left edge
				IndexBuffer[indexOffset + 1] = vertexOffset; // Cap vertex
				IndexBuffer[indexOffset + 2] = baseVertexIndex + TessellationLevel; // Right edge
			}
		}
		else if (capType == Cap::Arrow)
		{
			Vertex leftExtended = left;
			Vertex rightExtended = right;
			
			float3 perpDirection = tangent * halfWidth * 2;
			
			leftExtended.Position = centerPoint + perpDirection;
			rightExtended.Position = centerPoint - perpDirection;
			
			leftExtended.TextureCoord = float2(0.0, baseY + yOffsetDir * 0.1);
			rightExtended.TextureCoord = float2(1.0, baseY + yOffsetDir * 0.1);
			
			leftExtended.Normal = normal;
			rightExtended.Normal = normal;
			
			Vertex tipVertex = left;
			tipVertex.Position = centerPoint + direction * halfWidth * 2.0;
			tipVertex.Tangent = tangent;
			tipVertex.Normal = normal;
			tipVertex.TextureCoord = float2(0.5, baseY + yOffsetDir * 0.1);
			
			VertexBuffer[vertexOffset] = leftExtended;
			VertexBuffer[vertexOffset + 1] = rightExtended;
			VertexBuffer[vertexOffset + 2] = tipVertex;
			
			if (isStartCap)
			{
				IndexBuffer[indexOffset] = vertexOffset;
				IndexBuffer[indexOffset + 1] = vertexOffset + 1;
				IndexBuffer[indexOffset + 2] = vertexOffset + 2;
			}
			else
			{
				IndexBuffer[indexOffset] = vertexOffset;
				IndexBuffer[indexOffset + 1] = vertexOffset + 2;
				IndexBuffer[indexOffset + 2] = vertexOffset + 1;
			}
		}
		else if (capType == Cap::Rounded)
		{
			Vertex centerVertex = left;
			centerVertex.Position = centerPoint;
			centerVertex.Tangent = tangent;
			centerVertex.Normal = normal;
			centerVertex.TextureCoord = float2(0.5, baseY);
			
			uint centerIndex = vertexOffset;
			VertexBuffer[centerIndex] = centerVertex;
			
			float angleMultiplier = isStartCap ? -1.0 : 1.0;
			
			for (uint s = 0; s <= RoundedCapSegments; s++)
			{
				float t = float(s) / float(RoundedCapSegments);
				float angle = lerp(-PI/2.0f, PI/2.0f, t);
				
				Vertex arcVertex = centerVertex;
				
				arcVertex.Position = centerPoint + 
					tangent * halfWidth * sin(angle * angleMultiplier) + 
					direction * halfWidth * cos(angle * angleMultiplier);
				
				float arcU = isStartCap ? t : 1 - t;
				float arcY = baseY + -yOffsetDir * 0.1 * cos(angle);
				
				arcVertex.TextureCoord = float2(arcU, arcY);
				
				uint arcIndex = vertexOffset + 1 + s;
				VertexBuffer[arcIndex] = arcVertex;
			}
			
			for (uint t = 0; t < RoundedCapSegments; t++)
			{
				uint v0 = centerIndex;
				uint v1 = vertexOffset + 1 + t;
				uint v2 = vertexOffset + 1 + t + 1;
				
				uint idxOffset = indexOffset + (t * 3);
				
				if (isStartCap)
				{
					IndexBuffer[idxOffset] = v0;
					IndexBuffer[idxOffset + 1] = v1;
					IndexBuffer[idxOffset + 2] = v2;
				}
				else
				{
					IndexBuffer[idxOffset] = v0;
					IndexBuffer[idxOffset + 2] = v2;
					IndexBuffer[idxOffset + 1] = v1;
				}
			}
		}
	}

	[numthreads(32, 1, 1)]
	void MainCs(uint2 dispatchId : SV_DispatchThreadID)
	{
		uint i = dispatchId.x;
		
		if (i >= PointCount) return;
		if (PointCount < 2) return;
		
		Point prev, cur, next;
		cur = PointBuffer[i];
		prev = PointBuffer[i > 0 ? i - 1 : 0];
		next = PointBuffer[i < PointCount - 1 ? i + 1 : i];

		// If first or last one, extrapolate next/prev point
		if( i == 0 && PointCount > 1)
		{
			// Generate fake point, so we can calculate a tangent for the first point
			prev.Position = cur.Position + (cur.Position - PointBuffer[1].Position);
		}
		else if (i == PointCount - 1)
		{
			// Generate fake point, so we can calculate a tangent for the last point
			next.Position = next.Position - ( prev.Position - cur.Position );
		}

		// Generate tessellated vertices for this point
		uint vertexOffset = i * (TessellationLevel + 1);
		GenerateTessellatedVertices(cur.Width, prev, cur, next, vertexOffset);
		
		// Add triangles with correct winding order for tessellated segments
		if (i < PointCount - 1)
		{
			uint baseIndexOffset = i * TessellationLevel * 6;
			
			for (uint t = 0; t < TessellationLevel; t++)
			{
				uint v0 = i * (TessellationLevel + 1) + t;
				uint v1 = i * (TessellationLevel + 1) + t + 1;
				uint v2 = (i + 1) * (TessellationLevel + 1) + t;
				uint v3 = (i + 1) * (TessellationLevel + 1) + t + 1;
				
				uint idxOffset = baseIndexOffset + t * 6;
				
				// First triangle
				IndexBuffer[idxOffset] = v0;
				IndexBuffer[idxOffset + 1] = v2;
				IndexBuffer[idxOffset + 2] = v1;
				
				// Second triangle
				IndexBuffer[idxOffset + 3] = v1;
				IndexBuffer[idxOffset + 4] = v2;
				IndexBuffer[idxOffset + 5] = v3;
			}
		}
		
		// First thread generates caps
		// Caps are appended to the very end of the index/vertex buffers
		if (i == 0)
		{
			uint capVertexOffset = GetCapVertexOffset();
			uint capIndexOffset = GetCapIndexOffset();
			
			AddCap(capVertexOffset, capIndexOffset, StartCap, true);
			
			uint startCapVertexCount = GetCapVertexCount(StartCap);
			uint startCapIndexCount = GetCapIndexCount(StartCap);
			uint endCapVertexOffset = capVertexOffset + startCapVertexCount;
			uint endCapIndexOffset = capIndexOffset + startCapIndexCount;
			
			AddCap(endCapVertexOffset, endCapIndexOffset, EndCap, false);
		}
	}
}