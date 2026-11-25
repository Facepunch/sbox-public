namespace Sandbox;

public sealed partial class WaterBody
{
	/// <summary>
	/// The surface renderer's attributes, written only when a value changes. Every write changes the set (its version), so
	/// writing a frame's unchanged values would make the renderer lower the set again, and while a frame handed to the render
	/// thread may read it, copy it first. Values are compared exactly; a different set (the renderer's scene object was made
	/// again) is written whole.
	/// </summary>
	sealed class SurfaceAttributes
	{
		readonly Dictionary<string, System.Numerics.Vector4> _values = new();
		readonly Dictionary<string, object> _objects = new();
		RenderAttributes _target;

		/// <summary>Write into <paramref name="target"/> from now on.</summary>
		public SurfaceAttributes For( RenderAttributes target )
		{
			if ( !ReferenceEquals( target, _target ) )
			{
				Forget();
				_target = target;
			}

			return this;
		}

		/// <summary>Forget what was written, so the next writes all go through.</summary>
		public void Forget()
		{
			_values.Clear();
			_objects.Clear();
			_target = null;
		}

		bool Changed( string name, System.Numerics.Vector4 value )
		{
			if ( _values.TryGetValue( name, out var written ) && written == value )
				return false;

			_values[name] = value;
			return true;
		}

		public void Set( string name, float value )
		{
			if ( Changed( name, new( value, 0, 0, 0 ) ) )
				_target.Set( name, value );
		}

		public void Set( string name, int value )
		{
			if ( Changed( name, new( value, 0, 0, 0 ) ) )
				_target.Set( name, value );
		}

		public void Set( string name, bool value )
		{
			if ( Changed( name, new( value ? 1 : 0, 0, 0, 0 ) ) )
				_target.Set( name, value );
		}

		public void Set( string name, Vector2 value )
		{
			if ( Changed( name, new( value.x, value.y, 0, 0 ) ) )
				_target.Set( name, value );
		}

		public void Set( string name, Vector3 value )
		{
			if ( Changed( name, new( value.x, value.y, value.z, 0 ) ) )
				_target.Set( name, value );
		}

		public void Set( string name, Vector4 value )
		{
			if ( Changed( name, new( value.x, value.y, value.z, value.w ) ) )
				_target.Set( name, value );
		}

		public void Set( string name, GpuBuffer value )
		{
			if ( _objects.TryGetValue( name, out var written ) && ReferenceEquals( written, value ) )
				return;

			_objects[name] = value;
			_target.Set( name, value );
		}
	}
}
