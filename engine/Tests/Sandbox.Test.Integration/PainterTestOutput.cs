using Sandbox.Rendering;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Sandbox;

/// <summary>
/// Reads the instances and tables produced by a command-list painter.
/// </summary>
internal sealed class PainterTestOutput : IDisposable
{
	internal readonly PainterBatcher Batcher;
	readonly bool _ownsBatcher;

	internal PainterTestOutput( PainterBatcher batcher = null )
	{
		_ownsBatcher = batcher is null;
		Batcher = batcher ?? new( new CommandList() );
	}

	internal List<Painter.ClipEntry> DrawClips => Batcher.DrawClips;
	internal BlendMode BlendMode => Field<BlendMode>( Batcher, "_blendMode" );

	internal readonly record struct Instance( UICssBoxBatched.BoxInstance GPU, UICssBoxBatched.BorderShape BorderShapeData,
		PathSnapshot PathData, UICssBoxBatched.GradientInstance BackgroundGradient, Texture BackgroundImage, Matrix Transform );

	internal sealed class PathSnapshot( UICssBoxBatched.PathPrimitive[] primitives )
	{
		internal ReadOnlySpan<UICssBoxBatched.PathPrimitive> Primitives => primitives;
	}

	PathSnapshot ReadPath( int shapeIndex )
	{
		if ( shapeIndex < 0 ) return null;
		var shape = Batcher.Shapes[shapeIndex];
		if ( shape.PathCount == 0 ) return null;
		if ( shape.Kind == UICssBoxBatched.ShapeKind.PolygonPath && shape.PathNodeCount == -1 )
		{
			// Expose contour edges to geometry assertions regardless of their storage format.
			var points = Batcher.PolygonPoints.Skip( shape.PathOffset ).Take( shape.PathCount ).ToArray();
			return new( points.Select( ( point, i ) => new UICssBoxBatched.PathPrimitive
			{
				Kind = UICssBoxBatched.PathPrimitiveKind.Segment,
				A = new Vector4( point.x, point.y, points[(i + 1) % points.Length].x, points[(i + 1) % points.Length].y )
			} ).ToArray() );
		}
		return new( Batcher.Paths.Skip( shape.PathOffset ).Take( shape.PathCount ).ToArray() );
	}

	internal List<Instance> Instances
	{
		get
		{
			var shapes = Batcher.Shapes;
			var gradients = Batcher.Gradients;
			var matrices = Batcher.Transforms;
			var textures = Field<IEnumerable>( Batcher, "_textures" ).Cast<object>()
				.Select( use => (Texture)use.GetType().GetProperty( "Texture" ).GetValue( use ) ).ToArray();
			return Batcher.Instances.Select( gpu => new Instance( gpu,
				gpu.ShapeIndex >= 0 ? shapes[gpu.ShapeIndex] : default,
				ReadPath( gpu.ShapeIndex ),
				gpu.TextureIndex < 0 ? gradients[-gpu.TextureIndex - 1] : default,
				gpu.TextureIndex >= 0 && gpu.BackgroundRect != Vector4.Zero ? textures.FirstOrDefault( texture => texture.Index == gpu.TextureIndex ) : null,
				matrices[gpu.TransformIndex].Mat ) ).ToList();
		}
	}

	internal static T Field<T>( object value, string name )
	{
		return (T)value.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( value );
	}

	internal void Clear()
	{
		Batcher.CommandList.Rewind( 0 );
		Batcher.Clear();
	}

	public void Dispose()
	{
		Clear();
		if ( _ownsBatcher ) Batcher.Dispose();
	}
}
