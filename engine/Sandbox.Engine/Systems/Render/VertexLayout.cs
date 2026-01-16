using NativeEngine;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// Allows for the definition of custom vertex layouts
/// </summary>
public static class VertexLayout
{
	static ConcurrentDictionary<Type, NativeEngine.VertexLayout> entries = new();

	internal static NativeEngine.VertexLayout Get<T>() where T : unmanaged
	{
		return Get( typeof( T ) );
	}

	internal static NativeEngine.VertexLayout Get( Type t )
	{
		return entries.GetOrAdd( t, Create );
	}

	private static NativeEngine.VertexLayout Create( Type t )
	{
		var layout = NativeEngine.VertexLayout.Create( t.Name, Marshal.SizeOf( t ) );

		List<string> slots = new();

		int offset = 0;
		foreach ( var f in t.GetFields( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic ) )
		{
			var attr = f.GetCustomAttribute<BaseAttribute>();
			if ( attr == null )
			{
				throw new System.NotImplementedException( $"Vertex struct '{t.FullName}' is missing layout attributes on '{f.Name}'" );
			}

			Type fieldType = f.FieldType;
			int size = fieldType.GetManagedSize();
			ColorFormat format = fieldType switch
			{
				Type x when x == typeof( float ) => ColorFormat.COLOR_FORMAT_R32_FLOAT,
				Type x when x == typeof( Vector2 ) => ColorFormat.COLOR_FORMAT_R32G32_FLOAT,
				Type x when x == typeof( Vector3 ) => ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT,
				Type x when x == typeof( Vector4 ) => ColorFormat.COLOR_FORMAT_R32G32B32A32_FLOAT,
				Type x when x == typeof( global::Color ) => ColorFormat.COLOR_FORMAT_R32G32B32A32_FLOAT,
				Type x when x == typeof( uint ) => ColorFormat.COLOR_FORMAT_R32_SINT,
				Type x when x == typeof( int ) => ColorFormat.COLOR_FORMAT_R32_UINT,
				Type x when x == typeof( char ) => ColorFormat.COLOR_FORMAT_R8_SINT,
				Type x when x == typeof( byte ) => ColorFormat.COLOR_FORMAT_R8_UINT,
				Type x when x == typeof( Color32 ) => ColorFormat.COLOR_FORMAT_R8G8B8A8_UNORM,

				// TODO - we might want to support enums?

				_ => throw new NotImplementedException( $"No case implemented for type {fieldType}" )
			};

			var index = attr.Index;

			// if it's not defined, do it using order
			// and hope to god that they stay in the right order when compiled!
			// if not we need to try to order them using the code line number like in TypeLibrary
			if ( index == -1 )
			{
				for ( int i = 0; i < 32; i++ )
				{
					var name = $"{attr.Semantic}{i}";
					if ( !slots.Contains( name ) )
					{
						index = i;
						break;
					}
				}
			}

			var str = $"{attr.Semantic}{index}";

			if ( slots.Contains( str ) )
				throw new NotImplementedException( $"Vertex struct '{t.FullName}' contains '{str}' multiple times" );

			//Log.Info( $"{t.FullName} {attr.Semantic} {index}" );

			layout.Add( attr.Semantic, index, (uint)format, offset );

			slots.Add( str );
			offset += size;
		}

		layout.Build();

		return layout;
	}



	/// <summary>
	/// Should probably be calling this on hotload, when types are changed?
	/// </summary>
	internal static void FreeAll()
	{
		foreach ( var value in entries.Values )
		{
			value.Free();
			value.Destroy();
		}
	}

	[EditorBrowsable( EditorBrowsableState.Never )]
	public abstract class BaseAttribute : System.Attribute
	{
		internal int Index = -1;

		internal virtual string Semantic { get; }
	}

	public class Position : BaseAttribute
	{
		public Position() { }
		public Position( int index ) { Index = index; }
		internal override string Semantic => "position";
	}

	public class Normal : BaseAttribute
	{
		public Normal() { }
		public Normal( int index ) { Index = index; }
		internal override string Semantic => "normal";
	}

	public class Color : BaseAttribute
	{
		public Color() { }
		public Color( int index ) { Index = index; }
		internal override string Semantic => "color";
	}

	public class TexCoord : BaseAttribute
	{
		public TexCoord() { }
		public TexCoord( int index ) { Index = index; }
		internal override string Semantic => "texcoord";
	}

	public class Tangent : BaseAttribute
	{
		public Tangent() { }
		public Tangent( int index ) { Index = index; }
		internal override string Semantic => "tangent";
	}

	public class BlendWeight : BaseAttribute
	{
		public BlendWeight() { }
		public BlendWeight( int index ) { Index = index; }
		internal override string Semantic => "blendweight";
	}

	public class BlendIndices : BaseAttribute
	{
		public BlendIndices() { }
		public BlendIndices( int index ) { Index = index; }
		internal override string Semantic => "blendindices";
	}

	/*
	 * 
		"binormal";
		"specular";
		"psize";
		"tessfactor";
		"positiont";
	*/
}
