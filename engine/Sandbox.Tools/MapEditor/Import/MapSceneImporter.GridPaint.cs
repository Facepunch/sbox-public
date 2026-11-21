using System;
using System.IO;
using N = System.Numerics;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	sealed partial class VmapReader
	{
		Dictionary<(SourceNode Tile, GroundSample Paint), SourceNode> uniformTiles;

		readonly ref struct GroundLayerPaint
		{
			readonly ReadOnlySpan<byte> heights;
			readonly ReadOnlySpan<Color32> colors, opacity, transition;
			readonly int stride;
			public readonly bool HasPaint;
			public readonly Vector2 CellSize;

			public GroundLayerPaint( Element layer, int width, Vector2 cellSize, bool materialOverrides )
			{
				heights = GridValues<byte>( layer, "blendHeight" );
				colors = GridValues<Color32>( layer, "blendColor" );
				opacity = GridValues<Color32>( layer, "blendOpacity" );
				transition = GridValues<Color32>( layer, "blendTransitionAndPath" );
				stride = checked(width * 8 + 1);
				CellSize = cellSize;
				HasPaint = heights.ContainsAnyExcept( (byte)128 ) || HasSurfaceColorPaint( colors, materialOverrides );
			}

			// Hammer shares the outer row and column of each 9 by 9 patch with the adjacent tile.
			public PaintFlags ReadPatch( int x, int y, Span<GroundSample> patch, out bool displaced )
			{
				int start = checked(y * 8 * stride + x * 8);
				PaintFlags channels = 0;
				displaced = false;
				for ( int row = 0; row < 9; row++ )
				{
					for ( int column = 0; column < 9; column++ )
					{
						int i = checked(start + row * stride + column);
						byte height = i < heights.Length ? heights[i] : (byte)128;
						var tint = i < colors.Length ? colors[i] : new Color32( 255, 255, 255, 0 );
						patch[row * 9 + column] = new( height, tint, i < opacity.Length ? opacity[i] : new Color32( 0, 255, 0, 128 ), i < transition.Length ? transition[i].r : (byte)0, MaxRoad: i < transition.Length ? transition[i].g : byte.MaxValue );
						channels |= (PaintFlags)tint.a;
						displaced |= height != 128;
					}
				}
				return channels;
			}
		}

		void PaintGroundTile( SourceNode placement, Transform grid, int x, int y, in GroundLayerPaint layer, bool materialOverride, RoadStyle road = null, byte[] roadSamples = null )
		{
			Span<GroundSample> patch = stackalloc GroundSample[81];
			var channels = layer.ReadPatch( x, y, patch, out bool displaced );
			if ( roadSamples is null && (channels == 0 || (!materialOverride && !displaced && (channels & PaintFlags.Surface) == 0)) ) return;
			if ( roadSamples is not null )
				for ( int i = 0; i < patch.Length; i++ ) patch[i] = patch[i] with { Road = roadSamples[i] };

			var cellSize = layer.CellSize;
			var uniform = patch[0];
			var samples = roadSamples is not null || patch.ContainsAnyExcept( uniform ) ? patch.ToArray() : null;
			HashSet<SourceNode> active = null;
			ApplyPlacement( placement );
			foreach ( var proxy in placement.Children ) ApplyPlacement( proxy );

			void ApplyPlacement( SourceNode instance )
			{
				var key = (instance.Target, uniform);
				if ( samples is null && uniformTiles is not null && uniformTiles.TryGetValue( key, out var tile ) )
				{
					instance.Target = tile;
					return;
				}
				var at = grid.ToLocal( instance.Transform.WithScale( instance.Target.Transform.Scale ) );
				active ??= [];
				instance.Target = Copy( instance.Target, at );
				// Uniform paint is independent of placement.
				if ( samples is null ) (uniformTiles ??= [])[key] = instance.Target;
			}

			SourceNode Copy( SourceNode node, Transform at )
			{
				if ( !active.Add( node ) ) throw new InvalidDataException( $"Recursive Hammer tile at '{node.Name}'." );
				var copy = node.ShallowCopy();
				if ( node.Meshes.Count > 0 ) copy.Meshes = node.Meshes.Select( part => Paint( part, at ) ).ToArray();
				copy.Children = node.Children.Select( child => Copy( child, at.ToWorld( node.Transform.ToLocal( child.Transform ) ) ) ).ToArray();
				if ( node.Target is not null && string.IsNullOrEmpty( node.TargetMapPath ) )
					copy.Target = Copy( node.Target, at.WithScale( at.Scale * node.Transform.ToLocal( node.Target.Transform ).Scale ) );
				active.Remove( node );
				return copy;
			}

			SceneMeshData Paint( SceneMeshData part, Transform at )
			{
				bool moveVertices = displaced && !part.DisableHeightDisplacement;
				bool blend = roadSamples is not null || part.TransitionBlending || (channels & PaintFlags.Surface) != 0;
				if ( !moveVertices && !blend ) return part;
				var mesh = new PolygonMesh();
				mesh.MergeMesh( part.Mesh, Transform.Zero, out var vertices, out var edges, out _ );
				// Open tile edges are averaged after paint. Keep their fractional tints until that pass.
				bool hasSeams = mesh.HalfEdgeHandles.Any( edge => !edge.Face.IsValid && edge.OppositeEdge.Face.IsValid );
				var seamPaint = hasSeams ? part.SourcePaint?.ToDictionary( p => edges[p.Key], p => p.Value ) : null;
				foreach ( var (sourceVertex, vertex) in vertices )
				{
					var local = mesh.GetVertexPosition( vertex );
					var value = uniform.Value;
					if ( samples is not null )
					{
						var pos = at.PointToWorld( local );
						value = SampleGroundPaint( samples, pos.x / cellSize.x - x, pos.y / cellSize.y - y );
					}
					if ( blend && sourceVertex.Edge.IsValid && (value.Road > 0 || part.TransitionBlending || (value.Flags & PaintFlags.Surface) != 0) )
					{
						var outgoing = sourceVertex.Edge;
						do
						{
							var corner = outgoing.OppositeEdge;
							if ( corner.Face.IsValid )
							{
								var painted = edges[corner];
								var tint = value.Apply( mesh, painted, part.TransitionBlending, part.IndexedSecondaryBlend, part.SourcePaint is not null && part.SourcePaint.TryGetValue( corner, out var original ) ? original : null, road );
								if ( hasSeams && tint.HasValue ) (seamPaint ??= [])[painted] = new( tint.Value, default );
							}
							outgoing = corner.NextEdge;
						} while ( outgoing != sourceVertex.Edge );
					}
					if ( moveVertices ) mesh.SetVertexPosition( vertex, local + Vector3.Up * value.Height );
				}
				mesh.SetSmoothingAngle( part.Smoothing );
				return part with { Mesh = mesh, SourcePaint = seamPaint };
			}
		}
	}

	[Flags]
	internal enum PaintFlags : byte
	{
		BlueLayer = 1,
		GreenLayer = 2,
		RedLayer = 4,
		Sharpness = 8,
		Color = 16,
		Transition = 32,
		Layers = RedLayer | GreenLayer | BlueLayer,
		Surface = Layers | Color | Sharpness
	}

	internal readonly record struct GroundSample( byte Height, Color32 Tint, Color32 Opacity, byte Transition = 0, byte Road = 0, byte MaxRoad = 255 )
	{
		public GroundPaint Value => Weighted( 1 );
		public GroundPaint Weighted( float weight )
		{
			float channelWeight = weight * (1f / 255f);
			return new( (Height - 128) * weight, (PaintFlags)Tint.a, Transition * channelWeight, new N.Vector3( Tint.r, Tint.g, Tint.b ) * channelWeight,
				new N.Vector4( Opacity.b, Opacity.g, Opacity.r, Opacity.a ) * channelWeight, MathF.Min( MaxRoad * (1f / 255f), Road * channelWeight ) );
		}
	}

	internal readonly record struct GroundPaint( float Height, PaintFlags Flags, float Transition, N.Vector3 Tint, N.Vector4 Blend, float Road )
	{
		public N.Vector4? Apply( PolygonMesh mesh, HalfEdgeMesh.HalfEdgeHandle corner, bool transitionBlending, bool indexedSecondaryBlend, TileCornerPaint? source, RoadStyle road = null )
		{
			N.Vector4? resultTint = null;
			if ( (Flags & PaintFlags.Color) != 0 )
			{
				var color = mesh.GetVertexColor( corner );
				var original = source?.Tint ?? new N.Vector4( color.r, color.g, color.b, color.a ) / 255f;
				// Hammer lerps from the source to the painted value before packing. The order matters at byte boundaries.
				var tinted = original * new N.Vector4( Tint, 1f );
				resultTint = original + (tinted - original);
				mesh.SetVertexColor( corner, PackVertexPaint( resultTint.Value ) );
			}
			bool transitionPainted = transitionBlending && (Flags & PaintFlags.Transition) != 0;
			if ( (Flags & PaintFlags.Layers) != 0 || transitionBlending || Road > 0 )
			{
				var blend = mesh.GetVertexBlend( corner );
				var original = source?.Blend ?? new N.Vector4( blend.r, blend.g, blend.b, blend.a ) / 255f;
				var painted = original + (Blend - original);
				float r = transitionPainted || (Flags & PaintFlags.RedLayer) != 0 ? painted.X : original.X;
				float g = transitionPainted || (Flags & PaintFlags.GreenLayer) != 0 ? painted.Y : original.Y;
				float b = transitionPainted || (Flags & PaintFlags.BlueLayer) != 0 ? painted.Z : original.Z;
				if ( Road > 0 )
				{
					r *= 1 - MathF.Min( 1, Road * road.LayerOpacity.x );
					g *= 1 - MathF.Min( 1, Road * road.LayerOpacity.y );
					b *= 1 - MathF.Min( 1, Road * road.LayerOpacity.z );
				}
				if ( transitionBlending )
				{
					r = g * Transition;
					b = (1 - g) * Transition;
				}
				var packed = PackVertexPaint( new( r, g, b, 0 ) );
				if ( Road > 0 || transitionBlending || (Flags & PaintFlags.RedLayer) != 0 ) blend.r = packed.r;
				if ( Road > 0 || transitionPainted || (Flags & PaintFlags.GreenLayer) != 0 ) blend.g = packed.g;
				if ( Road > 0 || transitionBlending || (Flags & PaintFlags.BlueLayer) != 0 ) blend.b = packed.b;
				mesh.SetVertexBlend( corner, blend );
			}
			// Hammer paints the named sharpness stream. An indexed second blend stream keeps its authored values.
			if ( Road > 0 && !indexedSecondaryBlend ) mesh.SetSecondaryVertexBlend( corner, road.Sharpness );
			else if ( !indexedSecondaryBlend && ((Flags & PaintFlags.Sharpness) != 0 || transitionPainted) )
			{
				byte sharpness = PackVertexPaint( Blend ).a;
				mesh.SetSecondaryVertexBlend( corner, new( sharpness, sharpness, sharpness, sharpness ) );
			}
			return resultTint;
		}
	}

	internal static GroundPaint SampleGroundPaint( ReadOnlySpan<GroundSample> samples, float u, float v )
	{
		float x = Math.Clamp( u, 0, 1 ) * 8, y = Math.Clamp( v, 0, 1 ) * 8;
		int col = (int)x, row = (int)y * 9;
		int right = (int)MathF.Ceiling( x ), top = (int)MathF.Ceiling( y ) * 9;
		float fx = x - col, fy = y - (int)y;
		var a = samples[row + col].Weighted( (1 - fx) * (1 - fy) );
		var b = samples[row + right].Weighted( fx * (1 - fy) );
		var c = samples[top + col].Weighted( (1 - fx) * fy );
		var d = samples[top + right].Weighted( fx * fy );
		return new( a.Height + b.Height + c.Height + d.Height, a.Flags | b.Flags | c.Flags | d.Flags,
			a.Transition + b.Transition + c.Transition + d.Transition, a.Tint + b.Tint + c.Tint + d.Tint, a.Blend + b.Blend + c.Blend + d.Blend, a.Road + b.Road + c.Road + d.Road );
	}
}
