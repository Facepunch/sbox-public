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
		Painter.Path.Data PathData, UICssBoxBatched.GradientInstance BackgroundGradient, Texture BackgroundImage, Matrix Transform );

	internal List<Instance> Instances
	{
		get
		{
			var shapes = Batcher.Shapes;
			var gradients = Batcher.Gradients;
			var matrices = Batcher.Transforms;
			var paths = Field<Dictionary<Painter.Path.Data, int>>( Batcher, "_pathLookup" );
			var textures = Field<IEnumerable>( Batcher, "_textures" ).Cast<object>()
				.Select( use => (Texture)use.GetType().GetProperty( "Texture" ).GetValue( use ) ).ToArray();
			return Batcher.Instances.Select( gpu => new Instance( gpu,
				gpu.ShapeIndex >= 0 ? shapes[gpu.ShapeIndex] : default,
				paths.FirstOrDefault( pair => pair.Value == gpu.ShapeIndex ).Key,
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
