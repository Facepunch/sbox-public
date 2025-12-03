using System.Linq;
using Sandbox.MovieMaker;

namespace Editor.MovieMaker.BlockDisplays;

#nullable enable

public interface ICurveBlockItem : IBlockItem
{
	IPropertyBlock Block { get; }
	IReadOnlyList<Element> Elements { get; }
	IReadOnlyList<(float Min, float Max)> Ranges { get; }

	IEnumerable<MovieTimeRange> GetPaintHints( MovieTimeRange timeRange );
	void Read( MovieTime time, Span<float> result );
}

public record struct Element( string Name, Color Color, float? Min = null, float? Max = null );

public abstract partial class CurveBlockItem<T> : PropertyBlockItem<T>, ICurveBlockItem
{
	private readonly (float Min, float Max)[] _ranges;

	IPropertyBlock ICurveBlockItem.Block => Block;

	public IReadOnlyList<Element> Elements { get; }

	public IReadOnlyList<(float Min, float Max)> Ranges
	{
		get
		{
			UpdateRanges();
			return _ranges;
		}
	}

	protected CurveBlockItem( params Element[] elements )
	{
		Elements = elements;

		_ranges = new (float, float)[elements.Length];
	}

	private void UpdateRanges()
	{
		if ( !_rangesDirty ) return;

		_rangesDirty = false;

		if ( Elements.Count <= 0 ) return;

		for ( var i = 0; i < Elements.Count; ++i )
		{
			_ranges[i].Min = Elements[i].Min ?? float.PositiveInfinity;
			_ranges[i].Max = Elements[i].Max ?? float.NegativeInfinity;
		}

		foreach ( var tile in _tiles )
		{
			var tileRanges = tile.Ranges;

			for ( var i = 0; i < Elements.Count; ++i )
			{
				_ranges[i].Min = Math.Min( tileRanges[i].Min, _ranges[i].Min );
				_ranges[i].Max = Math.Max( tileRanges[i].Max, _ranges[i].Max );
			}
		}
	}

	protected abstract void Decompose( T value, Span<float> result );

	public void Read( MovieTime time, Span<float> result )
	{
		Decompose( Block.GetValue( time ), result );
	}
}

#region Scalars

public sealed class BooleanBlockItem() : CurveBlockItem<bool>(
	new Element( "X", Color.White, 0f, 1f ) )
{
	protected override void Decompose( bool value, Span<float> result )
	{
		result[0] = value ? 1f : 0f;
	}
}

public sealed class IntBlockItem() : CurveBlockItem<int>(
	new Element( "X", Color.White ) )
{
	protected override void Decompose( int value, Span<float> result )
	{
		result[0] = value;
	}
}

public sealed class FloatBlockItem() : CurveBlockItem<float>(
	new Element( "X", Color.White ) )
{
	protected override void Decompose( float value, Span<float> result )
	{
		result[0] = value;
	}
}

#endregion

#region Vectors

public sealed class Vector2BlockItem() : CurveBlockItem<Vector2>(
	new Element( "X", Theme.Red ),
	new Element( "Y", Theme.Green ) )
{
	protected override void Decompose( Vector2 value, Span<float> result )
	{
		result[0] = value.x;
		result[1] = value.y;
	}
}

public sealed class Vector3BlockItem() : CurveBlockItem<Vector3>(
	new Element( "X", Theme.Red ),
	new Element( "Y", Theme.Green ),
	new Element( "Z", Theme.Blue ) )
{
	protected override void Decompose( Vector3 value, Span<float> result )
	{
		result[0] = value.x;
		result[1] = value.y;
		result[2] = value.z;
	}
}

public sealed class Vector4BlockItem() : CurveBlockItem<Vector4>(
	new Element( "X", Theme.Red ),
	new Element( "Y", Theme.Green ),
	new Element( "Z", Theme.Blue ),
	new Element( "W", Color.White ) )
{
	protected override void Decompose( Vector4 value, Span<float> result )
	{
		result[0] = value.x;
		result[1] = value.y;
		result[2] = value.z;
		result[3] = value.w;
	}
}

#endregion Vectors

#region Rotation

public sealed class AnglesBlockItem() : CurveBlockItem<Angles>(
	new Element( "P", Theme.Red.Lighten( 0.25f ), -180f, 180f ),
	new Element( "Y", Theme.Green.Lighten( 0.25f ), -180f, 180f ),
	new Element( "R", Theme.Blue.Lighten( 0.25f ), -180f, 180f ) )
{
	protected override void Decompose( Angles value, Span<float> result )
	{
		result[0] = value.pitch;
		result[1] = value.yaw;
		result[2] = value.roll;
	}
}

public sealed class RotationBlockItem() : CurveBlockItem<Rotation>(
	new Element( "X", Theme.Red.Lighten( 0.25f ), -1f, 1f ),
	new Element( "Y", Theme.Green.Lighten( 0.25f ), -1f, 1f ),
	new Element( "Z", Theme.Blue.Lighten( 0.25f ), -1f, 1f ),
	new Element( "W", Color.White, -1f, 1f ) )
{
	protected override void Decompose( Rotation value, Span<float> result )
	{
		// Decompose it as the forward vector + how much the right vector is pointing up,
		// because that looks nice and smooth

		var forward = value.Forward;
		var right = value.Right;

		result[0] = forward.x;
		result[1] = forward.y;
		result[2] = forward.z;
		result[3] = right.z;
	}
}

#endregion

#region Transform

public sealed class TransformBlockItem() : CurveBlockItem<Transform>(
	new Element( "X", Theme.Red ),
	new Element( "Y", Theme.Green ),
	new Element( "Z", Theme.Blue ),
	new Element( "x", Theme.Red.Lighten( 0.25f ), -1f, 1f ),
	new Element( "y", Theme.Green.Lighten( 0.25f ), -1f, 1f ),
	new Element( "z", Theme.Blue.Lighten( 0.25f ), -1f, 1f ),
	new Element( "w", Color.White, -1f, 1f ) )
{
	protected override void Decompose( Transform value, Span<float> result )
	{
		var forward = value.Forward;
		var right = value.Right;

		result[0] = value.Position.x;
		result[1] = value.Position.y;
		result[2] = value.Position.z;

		result[3] = forward.x;
		result[4] = forward.y;
		result[5] = forward.z;
		result[6] = right.z;
	}
}

#endregion

#region Color

public sealed class ColorBlockItem() : CurveBlockItem<Color>(
	new Element( "R", Color.Red, 0f, 1f ),
	new Element( "G", Color.Green, 0f, 1f ),
	new Element( "B", Color.Blue, 0f, 1f ),
	new Element( "A", Color.White, 0f, 1f ) )
{
	protected override void Decompose( Color value, Span<float> result )
	{
		result[0] = value.r;
		result[1] = value.g;
		result[2] = value.b;
		result[3] = value.a;
	}
}

#endregion
