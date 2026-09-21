using Sandbox;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

/// <summary>
/// Represents a 4x4 matrix.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public struct Matrix : System.IEquatable<Matrix>
{
	internal System.Numerics.Matrix4x4 _numerics;

	public static Matrix CreateWorld( Vector3 position, Vector3 forward, Vector3 up ) => System.Numerics.Matrix4x4.CreateWorld( position, forward, up );
	public static Matrix CreateRotation( Rotation rot ) => System.Numerics.Matrix4x4.CreateFromQuaternion( rot._quat );
	public static Matrix CreateRotation( Vector3 angles ) => CreateRotationX( angles.x ) * CreateRotationY( angles.y ) * CreateRotationZ( angles.z );
	public static Matrix CreateRotationX( float degrees ) => System.Numerics.Matrix4x4.CreateRotationX( degrees.DegreeToRadian() );
	public static Matrix CreateRotationX( float degrees, Vector3 center ) => System.Numerics.Matrix4x4.CreateRotationX( degrees.DegreeToRadian(), center );
	public static Matrix CreateRotationY( float degrees ) => System.Numerics.Matrix4x4.CreateRotationY( degrees.DegreeToRadian() );
	public static Matrix CreateRotationY( float degrees, Vector3 center ) => System.Numerics.Matrix4x4.CreateRotationY( degrees.DegreeToRadian(), center );
	public static Matrix CreateRotationZ( float degrees ) => System.Numerics.Matrix4x4.CreateRotationZ( degrees.DegreeToRadian() );
	public static Matrix CreateRotationZ( float degrees, Vector3 center ) => System.Numerics.Matrix4x4.CreateRotationZ( degrees.DegreeToRadian(), center );
	public static Matrix CreateTranslation( Vector3 vec ) => System.Numerics.Matrix4x4.CreateTranslation( vec );
	public static Matrix CreateScale( Vector3 scales ) => System.Numerics.Matrix4x4.CreateScale( scales );
	public static Matrix CreateScale( Vector3 scales, Vector3 centerPoint ) => System.Numerics.Matrix4x4.CreateScale( scales, centerPoint );
	public static Matrix CreateSkew( Vector2 skew ) => new System.Numerics.Matrix4x4( 1.0f, MathF.Tan( skew.y.DegreeToRadian() ), 0.0f, 0.0f, MathF.Tan( skew.x.DegreeToRadian() ), 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f );
	public static Matrix CreateSkewX( float degrees ) => new System.Numerics.Matrix4x4( 1.0f, 0.0f, 0.0f, 0.0f, MathF.Tan( degrees.DegreeToRadian() ), 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f );
	public static Matrix CreateSkewY( float degrees ) => new System.Numerics.Matrix4x4( 1.0f, MathF.Tan( degrees.DegreeToRadian() ), 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f );
	public static Matrix CreateMatrix3D( float[] matrix ) => new System.Numerics.Matrix4x4( matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5], matrix[6], matrix[7], matrix[8], matrix[9], matrix[10], matrix[11], matrix[12], matrix[13], matrix[14], matrix[15] );

	public static implicit operator Matrix( System.Numerics.Matrix4x4 value ) => new Matrix { _numerics = value };
	public static implicit operator System.Numerics.Matrix4x4( Matrix value ) => value._numerics;
	public static Matrix operator *( Matrix value1, Matrix value2 ) => value1._numerics * value2._numerics;

	public Matrix( float m11, float m12, float m13, float m14, float m21, float m22, float m23, float m24, float m31, float m32, float m33, float m34, float m41, float m42, float m43, float m44 )
	{
		_numerics = new Matrix4x4( m11, m12, m13, m14, m21, m22, m23, m24, m31, m32, m33, m34, m41, m42, m43, m44 );
	}


	/// <summary>
	/// Returns the multiplicative identity matrix.
	/// </summary>
	public static Matrix Identity => System.Numerics.Matrix4x4.Identity;

	/// <summary>
	/// Returns inverse of this matrix.
	/// </summary>
	public Matrix Inverted
	{
		get
		{
			System.Numerics.Matrix4x4.Invert( _numerics, out var inv );
			return inv;
		}
	}

	/// <summary>
	/// Performs linear interpolation from one matrix to another.
	/// </summary>
	public static Matrix Lerp( Matrix ma, Matrix mb, float frac ) => System.Numerics.Matrix4x4.Lerp( ma._numerics, mb._numerics, frac );

	/// <summary>
	/// Performs spherical interpolation from one matrix to another.
	/// </summary>
	public static Matrix Slerp( Matrix ma, Matrix mb, float frac )
	{
		System.Numerics.Matrix4x4.Decompose( ma, out var _, out var ra, out var _ );
		System.Numerics.Matrix4x4.Decompose( mb, out var _, out var rb, out var _ );

		var mo = Matrix.Identity;
		mo *= (Matrix)System.Numerics.Matrix4x4.CreateFromQuaternion( Quaternion.Slerp( ra, rb, frac ) );

		return mo;
	}

	/// <summary>
	/// Formats the matrix and returns it as a string.
	/// </summary>
	/// <returns></returns>
	public override string ToString()
	{
		return $"M11: {_numerics.M11:0.####}, M12: {_numerics.M12:0.####}, M13: {_numerics.M13:0.####}, M14: {_numerics.M14:0.####} \n" +
				$"M21: {_numerics.M21:0.####}, M22: {_numerics.M22:0.####}, M23: {_numerics.M23:0.####}, M24: {_numerics.M24:0.####} \n" +
				$"M31: {_numerics.M31:0.####}, M32: {_numerics.M32:0.####}, M33: {_numerics.M33:0.####}, M34: {_numerics.M34:0.####} \n" +
				$"M41: {_numerics.M41:0.####}, M42: {_numerics.M42:0.####}, M43: {_numerics.M43:0.####}, M44: {_numerics.M44:0.####} ";
	}

	/// <summary>
	/// Returns transposed version of this matrix, meaning columns in this matrix become rows in the returned matrix and rows in this matrix become columns in the returned one.
	/// </summary>
	/// <returns></returns>
	public Matrix Transpose()
	{
		return Matrix4x4.Transpose( _numerics );
	}

	/// <summary>
	/// Transforms a vector by a 4x4 matrix
	/// </summary>
	public Vector4 Transform( Vector4 v )
	{
		return System.Numerics.Vector4.Transform( v._vec, _numerics );
	}

	/// <summary>
	/// Transforms a vector by a 4x4 matrix
	/// </summary>
	public Vector3 Transform( Vector3 v )
	{
		return System.Numerics.Vector3.Transform( v._vec, _numerics );
	}

	/// <summary>
	/// Transforms a vector by a 4x4 matrix
	/// </summary>
	public Vector2 Transform( Vector2 v )
	{
		return System.Numerics.Vector2.Transform( v._vec, _numerics );
	}

	/// <summary>
	/// Transforms a rect and returns the axis-aligned bounds of its four transformed corners.
	/// </summary>
	public Rect Transform( in Rect rect )
	{
		var a = Transform( rect.TopLeft );
		var b = Transform( rect.TopRight );
		var c = Transform( rect.BottomLeft );
		var d = Transform( rect.BottomRight );
		var min = Vector2.Min( Vector2.Min( a, b ), Vector2.Min( c, d ) );
		var max = Vector2.Max( Vector2.Max( a, b ), Vector2.Max( c, d ) );
		return new Rect( min, max - min );
	}

	/// <summary>
	/// Transforms a normal vector by a specified 4x4 matrix
	/// </summary>
	public Vector3 TransformNormal( Vector3 v )
	{
		return System.Numerics.Vector3.TransformNormal( v._vec, _numerics );
	}

	/// <summary>
	/// Converts a <see cref="Matrix"/> from Source to SteamVr coordinate system and scale
	/// </summary>
	/// <remarks>
	/// Source: X=forwards, Y=left, Z=up, scale = inches.
	/// SteamVr: X=right, Y=up, Z=backwards, scale = meters.
	/// </remarks>
	internal Matrix ToSteamVrCoordinateSystem
	{
		// [ 11 12 13 14 ]
		// [ 21 22 23 24 ]
		// [ 31 32 33 34 ]
		// [ 41 42 43 44 ]
		//
		// [  22 -23  21 -24 ]
		// [ -32  33 -31  34 ]
		// [  12 -13  11 -14 ]
		// [ -42  43 -41  44 ]
		get
		{
			var m = _numerics;
			return new Matrix
			{
				_numerics = new System.Numerics.Matrix4x4(
					 m.M22, -m.M23, m.M21, -m.M24,
					-m.M32, m.M33, -m.M31, m.M34,
					 m.M12, -m.M13, m.M11, -m.M14,
					-m.M42.InchToMeter(), m.M43.InchToMeter(), -m.M41.InchToMeter(), m.M44
				)
			};
		}
	}

	internal Matrix FromSteamVrCoordinateSystem
	{
		get
		{
			var m = _numerics;
			return new Matrix
			{
				_numerics = new System.Numerics.Matrix4x4(
					 m.M33, m.M31, -m.M32, -m.M34,
					 m.M13, m.M11, -m.M12, -m.M14,
					-m.M23, -m.M21, m.M22, m.M24,
					-m.M43.MeterToInch(), -m.M41.MeterToInch(), m.M42.MeterToInch(), m.M44
				)
			};
		}
	}

	public float M11 { readonly get => _numerics.M11; set => _numerics.M11 = value; }
	public float M12 { readonly get => _numerics.M12; set => _numerics.M12 = value; }
	public float M13 { readonly get => _numerics.M13; set => _numerics.M13 = value; }
	public float M14 { readonly get => _numerics.M14; set => _numerics.M14 = value; }
	public float M21 { readonly get => _numerics.M21; set => _numerics.M21 = value; }
	public float M22 { readonly get => _numerics.M22; set => _numerics.M22 = value; }
	public float M23 { readonly get => _numerics.M23; set => _numerics.M23 = value; }
	public float M24 { readonly get => _numerics.M24; set => _numerics.M24 = value; }
	public float M31 { readonly get => _numerics.M31; set => _numerics.M31 = value; }
	public float M32 { readonly get => _numerics.M32; set => _numerics.M32 = value; }
	public float M33 { readonly get => _numerics.M33; set => _numerics.M33 = value; }
	public float M34 { readonly get => _numerics.M34; set => _numerics.M34 = value; }
	public float M41 { readonly get => _numerics.M41; set => _numerics.M41 = value; }
	public float M42 { readonly get => _numerics.M42; set => _numerics.M42 = value; }
	public float M43 { readonly get => _numerics.M43; set => _numerics.M43 = value; }
	public float M44 { readonly get => _numerics.M44; set => _numerics.M44 = value; }


	internal Transform ExtractTransform()
	{
		Matrix4x4.Decompose( _numerics, out var scale, out var rot, out var pos );

		return new Transform()
		{
			Position = pos,
			Rotation = rot,
			Scale = scale
		};
	}

	internal static Matrix FromTransform( Transform transform )
	{
		var mat = (CreateScale( transform.Scale )
			* CreateRotation( transform.Rotation )
			* CreateTranslation( transform.Position ));

		return mat;
	}

	/// <summary>
	/// Create a projection matrix. The matrix will be in the correct format for the engine.
	/// </summary>
	public static unsafe Matrix CreateProjection( float zNear, float zFar, float fovX, float aspectRatio, Vector4? clipSpace = default )
	{
		Matrix cameraToProjection = default;

		Vector3* nearPoints = stackalloc Vector3[4];

		CalcFarPlaneCameraRelativePoints( nearPoints, new Vector3( 0, 0, 1 ), new Vector3( 0, 1, 0 ), new Vector3( -1, 0, 0 ), zNear, fovX, aspectRatio, clipSpace?.x ?? -1, clipSpace?.y ?? -1, clipSpace?.z ?? 1, clipSpace?.w ?? 1 );

		float l = nearPoints[0].x;
		float r = nearPoints[1].x;
		float b = nearPoints[0].y;
		float t = nearPoints[2].y;
		float zn = zNear;
		float zf = zFar;

		float width = r - l;
		float height = t - b;
		float reverseDepth = zn - zf;

		cameraToProjection = new Matrix4x4();

		cameraToProjection._numerics.M11 = (2f * zn) / width;
		cameraToProjection._numerics.M22 = (2f * zn) / height;
		cameraToProjection._numerics.M13 = (l + r) / width;
		cameraToProjection._numerics.M23 = (t + b) / height;

		if ( float.IsPositiveInfinity( zFar ) )
		{
			cameraToProjection._numerics.M33 = -1f;
			cameraToProjection._numerics.M34 = -zn;
			cameraToProjection._numerics.M43 = -1f;
		}
		else
		{
			cameraToProjection._numerics.M33 = zf / reverseDepth;
			cameraToProjection._numerics.M34 = (zn * zf) / reverseDepth;
			cameraToProjection._numerics.M43 = -1f;
		}

		return cameraToProjection;
	}

	/// <summary>
	/// Create a projection matrix. The matrix will be in the correct format for the engine, and will also be reverse z.
	/// </summary>
	public static Matrix CreateObliqueProjection( in Transform cameraTransform, in Sandbox.Plane clipPlane, in Matrix projectionMatrix )
	{
		var normal = cameraTransform.Rotation.Inverse * clipPlane.Normal;
		normal = new Vector3( normal.y, -normal.z, normal.x ).Normal;

		Matrix m = projectionMatrix;

		Vector4 q = default;

		q.x = (MathF.Sign( normal.x ) - m.M13) / m.M11;
		q.y = (MathF.Sign( normal.y ) - m.M23) / m.M22;
		q.z = 1f;
		q.w = (1f - m.M33) / m.M34;

		var plane = new Vector4( normal, Vector3.Dot( cameraTransform.Position - clipPlane.Position, clipPlane.Normal ) );
		var c = plane * (1.0f / System.Numerics.Vector4.Dot( plane, q ));

		m.M31 = -c.x;
		m.M32 = -c.y;
		m.M33 = -c.z;
		m.M34 = c.w;

		return m;
	}

	static unsafe void CalcFarPlaneCameraRelativePoints( Vector3* pointsOut, Vector3 forward, Vector3 up, Vector3 left, float farPlane, float fovX, float aspect, float clipBLX = -1.0f, float clipBLY = -1.0f, float clipTRX = 1.0f, float clipTRY = 1.0f )
	{
		Vector3 forwardShift = farPlane * forward;
		Vector3 upShift;
		Vector3 rightShift;

		if ( fovX == -1f )
		{
			upShift = up;
			rightShift = -left;
		}
		else
		{
			float tanX = MathF.Tan( MathF.PI / 180f * (fovX * 0.5f) );
			float tanY = tanX / MathF.Max( 1.0f / 1024.0f, aspect );

			upShift = farPlane * tanY * up;
			rightShift = farPlane * tanX * -left;
		}

		pointsOut[0] = forwardShift + clipBLX * rightShift + clipBLY * upShift;
		pointsOut[1] = forwardShift + clipTRX * rightShift + clipBLY * upShift;
		pointsOut[2] = forwardShift + clipBLX * rightShift + clipTRY * upShift;
		pointsOut[3] = forwardShift + clipTRX * rightShift + clipTRY * upShift;
	}

	static Matrix s_matZReversal = new( 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, -1.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f );

	Matrix ApplyReverseZ()
	{
		return s_matZReversal * this;
	}



	#region equality
	public static bool operator ==( Matrix left, Matrix right ) => left.Equals( right );
	public static bool operator !=( Matrix left, Matrix right ) => !(left == right);
	public readonly override bool Equals( object obj ) => obj is Matrix o && Equals( o );

	public readonly bool Equals( Matrix other )
	{
		ref readonly float left = ref _numerics.M11;
		ref readonly float right = ref other._numerics.M11;

		if ( Vector512.IsHardwareAccelerated )
		{
			return Vector512.EqualsAll(
				Vector512.LoadUnsafe( in left ),
				Vector512.LoadUnsafe( in right ) );
		}

		if ( Vector256.IsHardwareAccelerated )
		{
			return Vector256.EqualsAll(
				Vector256.LoadUnsafe( in left ),
				Vector256.LoadUnsafe( in right ) )
				&& Vector256.EqualsAll(
					Vector256.LoadUnsafe( in left, 8 ),
					Vector256.LoadUnsafe( in right, 8 ) );
		}

		return _numerics == other._numerics;
	}

	public readonly override int GetHashCode() => _numerics.GetHashCode();
	#endregion
}
