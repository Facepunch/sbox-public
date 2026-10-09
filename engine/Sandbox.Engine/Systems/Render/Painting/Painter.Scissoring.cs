namespace Sandbox;

public readonly ref partial struct Painter
{
	/// <summary>
	/// A stack of rounded rects a pixel has to be inside all of. Each one lives in the layout space of the
	/// panel that clips, reached from screen space through its matrix, so nested rounded clips all keep their
	/// corners. Plain rects in the same space merge into one entry. At MaxClips, the newest bounds are merged
	/// into the top entry; that fallback is not exact for rounded clips or clips in different coordinate spaces.
	/// </summary>
	internal struct Scissoring : IEquatable<Scissoring>
	{
		public const int MaxClips = 4;

		public struct Clip
		{
			public Rect Rect;
			public BorderRadii Radii;
			public Matrix Matrix;

			internal readonly Clip ForShader()
			{
				// CPU clips retain full inverses for composition. The shader only has projected XY:
				// eliminate its unknown Z using local Z = 0 to obtain the plane's inverse homography.
				var m = Matrix;
				if ( m.M33 == 0 || !float.IsFinite( m.M33 ) )
					return new Clip { Matrix = Matrix.Identity };

				var z = new Vector4( m.M31, m.M32, 0, m.M34 );
				var x = new Vector4( m.M11, m.M12, 0, m.M14 ) - z * (m.M13 / m.M33);
				var y = new Vector4( m.M21, m.M22, 0, m.M24 ) - z * (m.M23 / m.M33);
				var w = new Vector4( m.M41, m.M42, 0, m.M44 ) - z * (m.M43 / m.M33);
				if ( !x.IsFinite || !y.IsFinite || !w.IsFinite )
					return new Clip { Matrix = Matrix.Identity };

				return new Clip
				{
					Rect = Rect,
					Radii = Radii,
					Matrix = new Matrix(
						x.x, x.y, 0, x.w,
						y.x, y.y, 0, y.w,
						0, 0, m.M33, 0,
						w.x, w.y, 0, w.w )
				};
			}
		}

		[System.Runtime.CompilerServices.InlineArray( MaxClips )]
		public struct ClipList
		{
			Clip _element;
		}

		public ClipList Clips;
		public int Count;

		/// <summary>
		/// Keep what's outside instead - box-shadows use this to stay out of their panel
		/// </summary>
		public bool Invert;

		public readonly bool IsEmpty => Count == 0;

		public static Scissoring Single( in Rect rect, in BorderRadii radii, in Matrix matrix, bool invert = false )
		{
			var s = new Scissoring { Invert = invert };
			s.Push( rect, radii, matrix );
			return s;
		}

		public void Push( in Rect rect, in BorderRadii radii, in Matrix matrix )
		{
			if ( Count > 0 )
			{
				ref var top = ref Clips[Count - 1];

				var mergeable = top.Radii.IsZero && radii.IsZero && top.Matrix == matrix;
				if ( mergeable || Count == MaxClips )
				{
					top.Rect = Sandbox.Rect.Intersect( top.Rect, rect );
					return;
				}
			}

			Clips[Count++] = new Clip { Rect = rect, Radii = radii, Matrix = matrix };
		}

		public readonly bool Equals( in Scissoring other )
		{
			if ( Count != other.Count || Invert != other.Invert ) return false;

			for ( int i = 0; i < Count; i++ )
			{
				ref readonly var a = ref Clips[i];
				ref readonly var b = ref other.Clips[i];
				if ( a.Rect != b.Rect || !a.Radii.Equals( in b.Radii ) || a.Matrix != b.Matrix )
					return false;
			}

			return true;
		}

		readonly bool IEquatable<Scissoring>.Equals( Scissoring other ) => Equals( in other );

		public readonly override bool Equals( object obj ) => obj is Scissoring other && Equals( in other );

		public readonly override int GetHashCode()
		{
			var hash = HashCode.Combine( Count, Invert );
			for ( int i = 0; i < Count; i++ )
			{
				ref readonly var c = ref Clips[i];
				hash = HashCode.Combine( hash, c.Rect, c.Radii.TopLeft, c.Radii.TopRight, c.Radii.BottomLeft, c.Radii.BottomRight, c.Matrix );
			}
			return hash;
		}
	}
}
