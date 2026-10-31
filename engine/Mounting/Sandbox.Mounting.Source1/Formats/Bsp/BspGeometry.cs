using System;
using System.IO;
using System.Collections.Generic;

namespace Sandbox.Mounting.Source1;

internal static class BspGeometry
{
	internal static void GetDisplacementCorners( BspFile bsp, BspFace face, Span<Vector3> corners )
	{
		Span<Vector3> quad = stackalloc Vector3[4];
		int firstCorner = 0;
		float bestDist = float.MaxValue;
		for ( int i = 0; i < 4; i++ )
		{
			int surfEdge = bsp.SurfEdges[face.FirstEdge + i];
			quad[i] = surfEdge >= 0 ? bsp.Vertices[bsp.Edges[surfEdge].V0] : bsp.Vertices[bsp.Edges[-surfEdge].V1];
			float distance = Vector3.DistanceBetween( quad[i], bsp.DispInfos[face.DispInfo].StartPosition );
			if ( distance < bestDist )
			{
				bestDist = distance;
				firstCorner = i;
			}
		}

		for ( int i = 0; i < 4; i++ )
			corners[i] = quad[(i + firstCorner) & 3];
	}

	internal static Vector3 DisplacementBasePosition( ReadOnlySpan<Vector3> corners, float tx, float ty )
	{
		float sx = 1.0f - tx;
		float sy = 1.0f - ty;
		return ty * (sx * corners[1] + tx * corners[2]) + sy * (sx * corners[0] + tx * corners[3]);
	}

	internal static ReadOnlySpan<ushort> DisplacementTriangleTags( BspFile bsp, BspDispInfo disp )
	{
		int side = 1 << disp.Power;
		return bsp.DispTriTags.Length == 0 ? [] : bsp.DispTriTags.AsSpan( disp.DispTriStart, side * side * 2 );
	}

	internal static void AddDisplacementIndices( List<int> indices, int vertBase, int size, ReadOnlySpan<ushort> tags )
	{
		int start = indices.Count;
		for ( int y = 0; y < size - 1; y++ )
		{
			for ( int x = 0; x < size - 1; x++ )
			{
				int tl = vertBase + x + y * size;
				int tr = tl + 1;
				int bl = tl + size;
				int br = bl + 1;
				if ( ((x + y) & 1) != 0 )
				{
					indices.Add( bl );
					indices.Add( tl );
					indices.Add( tr );
					indices.Add( bl );
					indices.Add( tr );
					indices.Add( br );
				}
				else
				{
					indices.Add( bl );
					indices.Add( tl );
					indices.Add( br );
					indices.Add( br );
					indices.Add( tl );
					indices.Add( tr );
				}
			}
		}

		int write = start;
		for ( int triangle = 0; triangle < tags.Length; triangle++ )
		{
			const int DISPTRI_TAG_REMOVE = 1 << 5;
			if ( (tags[triangle] & DISPTRI_TAG_REMOVE) != 0 )
				continue;
			int read = start + triangle * 3;
			indices[write++] = indices[read];
			indices[write++] = indices[read + 1];
			indices[write++] = indices[read + 2];
		}

		if ( !tags.IsEmpty )
			indices.RemoveRange( write, indices.Count - write );
	}

	internal static bool IsRenderable( BspFile bsp, BspFace face )
	{
		if ( face.TexInfo == -1 )
			return false;
		ref var info = ref bsp.TexInfos[face.TexInfo];
		const int hiddenFlags = 0x2 | 0x4 | 0x40 | 0x80 | 0x100 | 0x200;
		return info.TexData != -1 && (info.Flags & hiddenFlags) == 0;
	}

	internal static string[] BuildLightstylePatterns( BspFile bsp )
	{
		var defaults = new[]
		{
			"m",
			"mmnmmommommnonmmonqnmmo",
			"abcdefghijklmnopqrstuvwxyzyxwvutsrqponmlkjihgfedcba",
			"mmmmmaaaaammmmmaaaaaabcdefgabcdefg",
			"mamamamamama",
			"jklmnopqrstuvwxyzyxwvutsrqponmlkj",
			"nmonqnmomnmomomno",
			"mmmaaaabcdefgmmmmaaaammmaamm",
			"mmmaaammmaaammmabcdefaaaammmmabcdefmmmaaaa",
			"aaaaaaaazzzzzzzz",
			"mmamammmmammamamaaamammma",
			"abcdefghijklmnopqrrqponmlkjihgfedcba",
			"mmnnmmnnnmmnn"
		};
		var patterns = new string[LightmapCodec.StyleCount];
		defaults.CopyTo( patterns, 0 );
		patterns[63] = "a";
		foreach ( var entity in bsp.Entities )
		{
			bool projected = entity.Get( "classname" ) == "env_projectedtexture";
			if ( !projected && (entity.Get( "classname" ) is not ("light" or "light_spot" or "light_glspot" or "light_directional" or "light_environment") || string.IsNullOrEmpty( entity.Get( "targetname" ) )) )
				continue;
			int style = entity.Integer( "style" );
			if ( style < 32 || style >= patterns.Length )
				continue;
			int defaultStyle = entity.Integer( "defaultstyle" );
			string pattern = entity.Get( "pattern" );
			if ( string.IsNullOrEmpty( pattern ) )
				pattern = defaultStyle > 0 && defaultStyle < defaults.Length ? defaults[defaultStyle] : "m";
			patterns[style] = (entity.Integer( "spawnflags" ) & (projected ? 4 : 1)) != 0 ? "a" : pattern;
		}

		return patterns;
	}
}
