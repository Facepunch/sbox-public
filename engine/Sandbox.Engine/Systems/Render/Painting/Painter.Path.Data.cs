namespace Sandbox;

public readonly ref partial struct Painter
{
	internal static partial class Path
	{
		/// <summary>
		/// Immutable path geometry and a balanced bounding-volume hierarchy, built once with the draw.
		/// Each node's escape index threads the tree so the shader needs no traversal stack or depth limit.
		/// </summary>
		internal sealed class Data
		{
			internal static int MaxPrimitiveCount => Math.Min( PainterBatcher.MaxBufferElements<UICssBoxBatched.PathPrimitive>(), (PainterBatcher.MaxBufferElements<UICssBoxBatched.PathNode>() + 1) / 2 );

			internal UICssBoxBatched.BorderShape Shape { get; }
			internal Data AlignmentMask { get; }
			readonly UICssBoxBatched.PathPrimitive[] _primitives;
			readonly UICssBoxBatched.PathNode[] _nodes;
			internal ReadOnlySpan<UICssBoxBatched.PathPrimitive> Primitives => _primitives;
			internal ReadOnlySpan<UICssBoxBatched.PathNode> Nodes => _nodes;

			internal Data( UICssBoxBatched.BorderShape shape, ReadOnlySpan<UICssBoxBatched.PathPrimitive> primitives, Data alignmentMask = null )
			{
				ArgumentOutOfRangeException.ThrowIfGreaterThan( primitives.Length, MaxPrimitiveCount );
				Shape = shape;
				AlignmentMask = alignmentMask;
				_primitives = primitives.ToArray();
				_nodes = new UICssBoxBatched.PathNode[checked(Math.Max( 0, primitives.Length * 2 - 1 ))];
				BuildNodes( shape, primitives, _nodes );
			}

			internal static void BuildNodes( UICssBoxBatched.BorderShape shape, ReadOnlySpan<UICssBoxBatched.PathPrimitive> primitives, Span<UICssBoxBatched.PathNode> nodes )
			{
				if ( primitives.IsEmpty ) return;

				int nodeCount = 0;
				Build( shape, primitives, 0, primitives.Length, nodes, ref nodeCount );
			}

			/// <summary>
			/// Builds the node for a run of primitives and returns its bounds.
			/// Primitives follow the path, so neighbours in the run are neighbours in space and halving it needs no sort.
			/// </summary>
			static Vector4 Build( in UICssBoxBatched.BorderShape shape, ReadOnlySpan<UICssBoxBatched.PathPrimitive> primitives, int start, int count, Span<UICssBoxBatched.PathNode> nodes, ref int nodeCount )
			{
				int index = nodeCount++;
				Vector4 bounds;
				if ( count == 1 )
				{
					var rect = shape.Kind == UICssBoxBatched.ShapeKind.PolygonPath ? SegmentBounds( primitives[start].A )
						: PrimitiveBounds( primitives[start], shape.Circle.z * 0.5f );
					bounds = new Vector4( rect.Left, rect.Top, rect.Right, rect.Bottom );
				}
				else
				{
					int left = count / 2;
					var a = Build( shape, primitives, start, left, nodes, ref nodeCount );
					var b = Build( shape, primitives, start + left, count - left, nodes, ref nodeCount );
					bounds = new Vector4( MathF.Min( a.x, b.x ), MathF.Min( a.y, b.y ), MathF.Max( a.z, b.z ), MathF.Max( a.w, b.w ) );
				}
				nodes[index] = new UICssBoxBatched.PathNode
				{
					Bounds = bounds,
					Next = nodeCount,
					Primitive = count == 1 ? start : -1,
				};
				return bounds;
			}
		}
	}
}
