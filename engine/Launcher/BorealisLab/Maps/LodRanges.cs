using Borealis.Backend.Lods;
using System.Globalization;
using System.IO;
using System.Text;
using V3 = System.Numerics.Vector3;

namespace Sandbox.BorealisLab.Maps;

/// <summary>
/// <c>-real -lod-ranges</c>: what generated LODs make of each material's aggregate ranges, and the level each view would
/// pick for them with the culls' rule (the coarsest level whose error, in pixels at the bounds' nearest point, stays under
/// 0.75 of <see cref="Borealis.Backend.BackendDevice.LodPixels"/>, as a cleared slice picks). Ranges whose bounds lie
/// outside a view's cone are left out of its counts. Writes <c>lod-ranges.md</c> beside the run's results.
/// </summary>
internal static class LodRanges
{
	const float Hysteresis = 0.75f;
	const int Rows = 40;

	public static void Write( LodStore store, float lodPixels, int width, IReadOnlyList<MapView> views, string folder )
	{
		var ranges = store.Ranges();
		var md = new StringBuilder();
		md.AppendLine( "# Generated LODs by material" );
		md.AppendLine();
		md.AppendLine( FormattableString.Invariant( $"{ranges.Count:N0} ranges asked for; levels at {lodPixels:0.##} px, a level picked as the culls pick it with a cleared slice (under {Hysteresis} of the limit)." ) );

		foreach ( var view in views )
		{
			var forward = V3.Normalize( view.LookAt - view.Position );
			var halfAngle = view.FieldOfView * MathF.PI / 360f;
			var scale = width * 0.5f / (MathF.Tan( halfAngle ) * lodPixels);
			// The cone holding the frame's corners: a 16:9 frame's diagonal is about 1.15 times its width
			var coneCos = MathF.Cos( MathF.Atan( MathF.Tan( halfAngle ) * 1.15f ) );

			var byMaterial = new Dictionary<string, (int Ranges, long Base, long Drawn, int Simplest, int WithLevels, int AtLevel0)>();
			long total = 0, drawn = 0;
			foreach ( var (label, triangles, simplest, levels, errors) in ranges )
			{
				if ( label.Material is null ) continue;
				var toCenter = label.Center - view.Position;
				var radius = label.Extents.Length();
				var distance = toCenter.Length();
				if ( distance > radius && V3.Dot( toCenter, forward ) < distance * coneCos - radius ) continue;

				var nearest = V3.Max( V3.Abs( toCenter ) - label.Extents, V3.Zero ).Length();
				var level = 0;
				if ( nearest > 0 )
				{
					var units = radius * scale / nearest;
					for ( int l = 0; l < levels.Length && l < errors.Length; l++ )
					{
						if ( errors[l] * units <= Hysteresis ) level = l + 1;
					}
				}

				var picked = level == 0 ? triangles : levels[level - 1];
				byMaterial.TryGetValue( label.Material, out var row );
				byMaterial[label.Material] = (row.Ranges + 1, row.Base + triangles, row.Drawn + picked, row.Simplest + (simplest ? 1 : 0), row.WithLevels + (levels.Length > 0 ? 1 : 0), row.AtLevel0 + (levels.Length > 0 && level == 0 ? 1 : 0));
				total += triangles;
				drawn += picked;
			}

			md.AppendLine();
			md.AppendLine( $"## {view.Name}" );
			md.AppendLine();
			md.AppendLine( FormattableString.Invariant( $"In view: {total:N0} triangles in the ranges, {drawn:N0} at the levels picked ({(total > 0 ? 100.0 * drawn / total : 0):0.0}%)." ) );
			md.AppendLine();
			md.AppendLine( "| Material | Ranges | Triangles | At picked levels | Kept | No level worth making | With levels | With levels, drawn whole |" );
			md.AppendLine( "|---|---|---|---|---|---|---|---|" );
			foreach ( var (material, row) in byMaterial.OrderByDescending( x => x.Value.Drawn ).Take( Rows ) )
				md.AppendLine( FormattableString.Invariant( $"| {material} | {row.Ranges} | {row.Base:N0} | {row.Drawn:N0} | {100.0 * row.Drawn / Math.Max( row.Base, 1 ):0}% | {row.Simplest} | {row.WithLevels} | {row.AtLevel0} |" ) );
		}

		Directory.CreateDirectory( folder );
		File.WriteAllText( Path.Combine( folder, "lod-ranges.md" ), md.ToString() );
	}
}
