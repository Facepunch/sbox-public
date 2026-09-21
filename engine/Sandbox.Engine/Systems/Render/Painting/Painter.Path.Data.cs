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
				if ( primitives.IsEmpty ) return;

				Span<BoundsEntry> entries = primitives.Length <= 128 ? stackalloc BoundsEntry[primitives.Length] : new BoundsEntry[primitives.Length];
				for ( int i = 0; i < entries.Length; i++ )
					entries[i] = new( shape.Kind == UICssBoxBatched.ShapeKind.PolygonPath ? SegmentBounds( primitives[i].A )
						: PrimitiveBounds( primitives[i], shape.Circle.z * 0.5f ), i );
				int nodeCount = 0;
				Build( entries, _nodes, ref nodeCount );
			}

			readonly record struct BoundsEntry( Rect Bounds, int Primitive );

			readonly struct BoundsComparer( bool horizontal ) : IComparer<BoundsEntry>
			{
				public int Compare( BoundsEntry a, BoundsEntry b ) => horizontal
					? a.Bounds.Center.x.CompareTo( b.Bounds.Center.x )
					: a.Bounds.Center.y.CompareTo( b.Bounds.Center.y );
			}

			static void Build( Span<BoundsEntry> entries, Span<UICssBoxBatched.PathNode> nodes, ref int nodeCount )
			{
				var min = entries[0].Bounds.Position;
				var max = entries[0].Bounds.BottomRight;
				for ( int i = 1; i < entries.Length; i++ )
				{
					min = Vector2.Min( min, entries[i].Bounds.Position );
					max = Vector2.Max( max, entries[i].Bounds.BottomRight );
				}
				int index = nodeCount++;
				if ( entries.Length > 1 )
				{
					entries.Sort( new BoundsComparer( max.x - min.x > max.y - min.y ) );
					int left = entries.Length / 2;
					Build( entries[..left], nodes, ref nodeCount );
					Build( entries[left..], nodes, ref nodeCount );
				}
				nodes[index] = new UICssBoxBatched.PathNode
				{
					Bounds = new Vector4( min.x, min.y, max.x, max.y ),
					Next = nodeCount,
					Primitive = entries.Length == 1 ? entries[0].Primitive : -1,
				};
			}
		}
	}
}
