/* Copyright (c) 2011 Khaled Mamou (kmamou at gmail dot com)
 All rights reserved.


 Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

 1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.

 2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.

 3. The names of the contributors may not be used to endorse or promote products derived from this software without specific prior written permission.

 THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.*/
// Managed implementation of the voxel decomposition used by the Hammer map compiler.
using System;

namespace Editor.MapEditor;

internal static partial class ConvexDecomposition
{
	internal static List<Vector3[]> Build( ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> indices )
	{
		var result = new List<Vector3[]>();
		if ( indices.IsEmpty || positions.IsEmpty )
			return result;
		var vertices = new Point[positions.Length];
		for ( int i = 0; i < vertices.Length; i++ )
			vertices[i] = new Point( positions[i].x, positions[i].y, positions[i].z );
		var grid = Grid.Voxelize( vertices, indices );
		if ( grid.Cells.Count == 0 )
			return result;
		foreach ( var hull in Decompose( grid.Cells, vertices.Aggregate( Point.Min ), grid.Step ) )
		{
			if ( hull.Vertices.Count < 4 )
				continue;
			var points = new Vector3[hull.Vertices.Count];
			for ( int i = 0; i < points.Length; i++ )
			{
				var p = hull.Vertices[i];
				points[i] = new Vector3( (float)p.X, (float)p.Y, (float)p.Z );
			}
			result.Add( points );
		}

		return result;
	}
}
