using System;
using System.Collections.Immutable;

namespace Sandbox.UI
{
	[SkipHotload]
	public partial struct PanelTransform
	{
		// Considering how many entries these are gonna have in reality
		// we might be best to just hardcode like 6 entries
		public ImmutableList<Entry> List;

		public Matrix BuildTransform( float width, float height, Vector2 perspectiveOrigin )
		{
			var tx = Matrix.Identity;

			if ( List == null )
				return tx;

			foreach ( var e in List )
			{
				Matrix m = Matrix.Identity;
				if ( e.Type == EntryType.Perspective )
				{
					m *= Matrix.CreateTranslation( new Vector3( perspectiveOrigin, 0.0f ) );
					m *= e.ToMatrix( width, height );
					m *= Matrix.CreateTranslation( new Vector3( -perspectiveOrigin, 0.0f ) );
				}
				else
				{
					m = e.ToMatrix( width, height );
				}

				tx = m * tx;
			}

			return tx;
		}

		/// <summary>
		/// Returns true if this is empty.
		/// </summary>
		public bool IsEmpty()
		{
			return Entries == 0;
		}

		public readonly int Entries => List?.Count ?? 0;

		readonly Entry GetEntry( int i )
		{
			if ( i >= Entries ) return new Entry { Type = EntryType.Invalid };

			return List[i];
		}

		readonly Entry GetEntry( EntryType type )
		{
			if ( List == null )
				return new Entry { Type = EntryType.Invalid };

			foreach ( var e in List )
			{
				if ( e.Type == type )
					return e;
			}

			return new Entry { Type = EntryType.Invalid };
		}

		public bool AddTranslate( Length? lengthX, Length? lengthY, Length? lengthZ = null )
		{
			if ( !lengthX.HasValue || !lengthY.HasValue )
				return false;

			if ( List == null )
				List = ImmutableList.Create<Entry>();

			List = List.Add( new Entry { Type = EntryType.Translate, X = lengthX.Value, Y = lengthY.Value, Z = lengthZ ?? new Length() } );
			return true;
		}

		public bool AddTranslateX( Length? length )
		{
			if ( !length.HasValue )
				return false;

			if ( List == null )
				List = ImmutableList.Create<Entry>();

			List = List.Add( new Entry { Type = EntryType.Translate, X = length.Value } );
			return true;
		}

		public bool AddTranslateY( Length? length )
		{
			if ( !length.HasValue )
				return false;

			if ( List == null )
				List = ImmutableList.Create<Entry>();

			List = List.Add( new Entry { Type = EntryType.Translate, Y = length.Value } );
			return true;
		}

		public bool AddTranslateZ( Length? length )
		{
			if ( !length.HasValue )
				return false;

			if ( List == null )
				List = ImmutableList.Create<Entry>();

			List = List.Add( new Entry { Type = EntryType.Translate, Z = length.Value } );
			return true;
		}

		public bool AddScale( float scale )
		{
			if ( List == null )
				List = ImmutableList.Create<Entry>();

			List = List.Add( new Entry { Type = EntryType.Scale, Data = Vector3.One * scale } );
			return true;
		}

		public bool AddScale( Vector3 scale )
		{
			if ( List == null )
				List = ImmutableList.Create<Entry>();

			List = List.Add( new Entry { Type = EntryType.Scale, Data = scale } );
			return true;
		}

		public bool AddSkew( float x, float y, float z )
		{
			if ( List == null )
				List = ImmutableList.Create<Entry>();

			List = List.Add( new Entry { Type = EntryType.Skew, Data = new Vector3( x, y, z ) } );
			return true;
		}

		public bool AddRotation( float x, float y, float z )
		{
			if ( List == null )
				List = ImmutableList.Create<Entry>();

			List = List.Add( new Entry { Type = EntryType.Rotation, Data = new Vector3( x, y, z ) } );
			return true;
		}

		public bool AddRotation( Vector3 angles ) => AddRotation( angles.x, angles.y, angles.z );

		public bool AddMatrix3D( Matrix matrix )
		{
			if ( List == null )
				List = ImmutableList.Create<Entry>();

			List = List.Add( new Entry { Type = EntryType.Matrix, Matrix = matrix } );
			return true;
		}

		public bool AddPerspective( Length d )
		{
			if ( List == null )
				List = ImmutableList.Create<Entry>();

			List = List.Add( new Entry { Type = EntryType.Perspective, X = d } );
			return true;
		}

		internal static PanelTransform? Lerp( PanelTransform? a, PanelTransform? b, float delta, Vector2? dimensions = null )
		{
			var ma = a ?? new PanelTransform();
			var mb = b ?? new PanelTransform();
			var entries = Math.Max( ma.Entries, mb.Entries );
			if ( entries <= 0 ) return new PanelTransform();

			var builder = ImmutableList.CreateBuilder<Entry>();

			for ( int i = 0; i < ma.Entries; i++ )
			{
				var from = ma.GetEntry( i );
				var to = mb.GetEntry( from.Type );

				if ( to.Type == EntryType.Invalid )
					to = from.GetDefault();

				if ( dimensions != null )
					builder.Add( Entry.Lerp( from, to, delta, dimensions.Value ) );
				else
					builder.Add( Entry.Lerp( from, to, delta ) );
			}

			for ( int i = 0; i < mb.Entries; i++ )
			{
				var to = mb.GetEntry( i );
				var from = ma.GetEntry( to.Type );
				if ( from.Type != EntryType.Invalid )
					continue;

				if ( dimensions != null )
					builder.Add( Entry.Lerp( to.GetDefault(), to, delta, dimensions.Value ) );
				else
					builder.Add( Entry.Lerp( to.GetDefault(), to, delta ) );
			}

			var o = new PanelTransform();
			o.List = builder.ToImmutable();

			return o;
		}

		internal readonly PanelTransform GetScaled( float scale )
		{
			if ( Entries <= 0 ) return new PanelTransform();

			var builder = ImmutableList.CreateBuilder<Entry>();

			for ( int i = 0; i < Entries; i++ )
			{
				var e = GetEntry( i );

				Length.Scale( ref e.X, scale );
				Length.Scale( ref e.Y, scale );
				Length.Scale( ref e.Z, scale );

				builder.Add( e );
			}

			var o = new PanelTransform();
			o.List = builder.ToImmutable();

			return o;
		}

		public override bool Equals( object obj )
		{
			return obj is PanelTransform transform &&
				   EqualityComparer<ImmutableList<Entry>>.Default.Equals( List, transform.List );
		}

		public readonly override int GetHashCode()
		{
			return HashCode.Combine( List );
		}

		public static bool operator ==( PanelTransform a, PanelTransform b )
		{
			return a.Equals( b );
		}

		public static bool operator !=( PanelTransform a, PanelTransform b )
		{
			return !a.Equals( b );
		}

		public struct Entry
		{
			public EntryType Type;
			public Vector3 Data;
			public Matrix Matrix;

			public Length X;
			public Length Y;
			public Length Z;

			internal static Entry Lerp( Entry a, Entry b, float delta )
			{
				var data = Vector3.Lerp( a.Data, b.Data, delta, false );

				return new Entry
				{
					Type = a.Type,
					Data = data,
					Matrix = Matrix.Lerp( a.Matrix, b.Matrix, delta ),
					X = Length.Lerp( a.X, b.X, delta ) ?? b.X,
					Y = Length.Lerp( a.Y, b.Y, delta ) ?? b.Y,
					Z = Length.Lerp( a.Z, b.Z, delta ) ?? b.Z,
				};
			}

			internal static Entry Lerp( Entry a, Entry b, float delta, Vector2 dimensions )
			{
				var data = Vector3.Lerp( a.Data, b.Data, delta, false );

				return new Entry
				{
					Type = a.Type,
					Data = data,
					Matrix = Matrix.Lerp( a.Matrix, b.Matrix, delta ),
					X = Length.Lerp( a.X, b.X, delta, dimensions.x ) ?? b.X,
					Y = Length.Lerp( a.Y, b.Y, delta, dimensions.y ) ?? b.Y,
					Z = Length.Lerp( a.Z, b.Z, delta ) ?? b.Z,
				};
			}

			public Matrix ToMatrix( float width, float height )
			{
				switch ( Type )
				{
					case EntryType.Rotation:
						{
							return Matrix.CreateRotation( Data );
						}

					case EntryType.Scale:
						{
							return Matrix.CreateScale( Data );
						}

					case EntryType.Translate:
						{
							return Matrix.CreateTranslation( new Vector3( X.GetPixels( width ), Y.GetPixels( height ), Z.GetPixels( 0 ) ) );
						}

					case EntryType.Skew:
						{
							var ax = MathF.Tan( Data.x.DegreeToRadian() );
							var ay = MathF.Tan( Data.y.DegreeToRadian() );

							return Matrix.CreateMatrix3D( [
								1.0f, ay, 0.0f, 0.0f,
								ax, 1.0f, 0.0f, 0.0f,
								0.0f, 0.0f, 1.0f, 0.0f,
								0.0f, 0.0f, 0.0f, 1.0f
							] );
						}
					case EntryType.Matrix:
						{
							return Matrix;
						}
					case EntryType.Perspective:
						{
							return Matrix.CreateMatrix3D( [
								1.0f, 0.0f, 0.0f, 0.0f,
								0.0f, 1.0f, 0.0f, 0.0f,
								0.0f, 0.0f, 1.0f, -1.0f / MathF.Max(X.GetPixels( width ), 1.0f),
								0.0f, 0.0f, 0.0f, 1.0f
							] );
						}
					default:
						return Matrix.Identity;
				}
			}

			public Entry GetDefault()
			{
				return new Entry
				{
					Type = this.Type,
					Data = Vector3.One,
					Matrix = Matrix.Identity,
					X = new Length { Unit = X.Unit },
					Y = new Length { Unit = Y.Unit },
					Z = new Length { Unit = Z.Unit },
				};
			}
		}

		public enum EntryType
		{
			Invalid,
			Rotation,
			Scale,
			Translate,
			Skew,
			Matrix,
			Perspective,
		}

	}
}
