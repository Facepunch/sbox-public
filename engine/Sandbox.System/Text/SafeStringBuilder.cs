using System;
using System.Collections.Generic;
using System.Text;

namespace Sandbox.Internal;

/// <summary>
/// Calls to <c>new StringBuilder()</c> in addon code will map to this class.
/// You can use it directly but you probably shouldn't.
/// </summary>
public sealed class SafeStringBuilder
{
	private readonly StringBuilder _inner;
	private readonly object _syncLock = new object();

	public SafeStringBuilder() => _inner = new StringBuilder();
	public SafeStringBuilder( int capacity ) => _inner = new StringBuilder( capacity );
	public SafeStringBuilder( int capacity, int maxCapacity ) => _inner = new StringBuilder( capacity, maxCapacity );
	public SafeStringBuilder( string value ) => _inner = new StringBuilder( value );
	public SafeStringBuilder( string value, int capacity ) => _inner = new StringBuilder( value, capacity );
	public SafeStringBuilder( string value, int startIndex, int length, int capacity ) => _inner = new StringBuilder( value, startIndex, length, capacity );

	public int Length
	{
		get => _inner.Length;
		set { lock ( _syncLock ) { _inner.Length = value; } }
	}

	public int Capacity
	{
		get => _inner.Capacity;
		set { lock ( _syncLock ) { _inner.Capacity = value; } }
	}

	public int MaxCapacity => _inner.MaxCapacity;

	public char this[int index]
	{
		get => _inner[index];
		set { lock ( _syncLock ) { _inner[index] = value; } }
	}

	public int EnsureCapacity( int capacity ) { lock ( _syncLock ) { return _inner.EnsureCapacity( capacity ); } }

	public override string ToString() => _inner.ToString();
	public string ToString( int startIndex, int length ) => _inner.ToString( startIndex, length );

	public void CopyTo( int sourceIndex, char[] destination, int destinationIndex, int count ) =>
		_inner.CopyTo( sourceIndex, destination, destinationIndex, count );

	public void CopyTo( int sourceIndex, Span<char> destination, int count ) =>
		_inner.CopyTo( sourceIndex, destination, count );

	public bool Equals( SafeStringBuilder sb )
	{
		if ( sb is null ) return false;
		if ( ReferenceEquals( sb, this ) ) return true;
		string a, b;
		lock ( _syncLock ) { a = _inner.ToString(); }
		lock ( sb._syncLock ) { b = sb._inner.ToString(); }
		return a == b;
	}

	public bool Equals( ReadOnlySpan<char> span ) => _inner.Equals( span );

	public SafeStringBuilder Clear() { lock ( _syncLock ) { _inner.Clear(); return this; } }
	public SafeStringBuilder Remove( int startIndex, int length ) { lock ( _syncLock ) { _inner.Remove( startIndex, length ); return this; } }

	public SafeStringBuilder Append( bool value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }
	public SafeStringBuilder Append( byte value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }
	public SafeStringBuilder Append( char value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }
	public SafeStringBuilder Append( char value, int repeatCount ) { lock ( _syncLock ) { _inner.Append( value, repeatCount ); return this; } }
	public SafeStringBuilder Append( char[] value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }
	public SafeStringBuilder Append( char[] value, int startIndex, int charCount ) { lock ( _syncLock ) { _inner.Append( value, startIndex, charCount ); return this; } }
	public SafeStringBuilder Append( decimal value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }
	public SafeStringBuilder Append( double value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }
	public SafeStringBuilder Append( float value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }
	public SafeStringBuilder Append( int value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }
	public SafeStringBuilder Append( long value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }
	public SafeStringBuilder Append( object value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }
	public SafeStringBuilder Append( ReadOnlyMemory<char> value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }
	public SafeStringBuilder Append( ReadOnlySpan<char> value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }
	public SafeStringBuilder Append( sbyte value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }
	public SafeStringBuilder Append( short value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }
	public SafeStringBuilder Append( string value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }
	public SafeStringBuilder Append( string value, int startIndex, int count ) { lock ( _syncLock ) { _inner.Append( value, startIndex, count ); return this; } }
	public SafeStringBuilder Append( uint value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }
	public SafeStringBuilder Append( ulong value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }
	public SafeStringBuilder Append( ushort value ) { lock ( _syncLock ) { _inner.Append( value ); return this; } }

	public SafeStringBuilder Append( SafeStringBuilder value )
	{
		if ( value is null ) return this;
		if ( ReferenceEquals( value, this ) )
		{
			lock ( _syncLock ) { _inner.Append( _inner.ToString() ); return this; }
		}
		string str;
		lock ( value._syncLock ) { str = value._inner.ToString(); }
		lock ( _syncLock ) { _inner.Append( str ); return this; }
	}

	public SafeStringBuilder AppendLine() { lock ( _syncLock ) { _inner.AppendLine(); return this; } }
	public SafeStringBuilder AppendLine( string value ) { lock ( _syncLock ) { _inner.AppendLine( value ); return this; } }
	public SafeStringBuilder AppendLine( ReadOnlySpan<char> value ) { lock ( _syncLock ) { _inner.Append( value ).AppendLine(); return this; } }

	public SafeStringBuilder AppendFormat( string format, object arg0 ) { lock ( _syncLock ) { _inner.AppendFormat( format, arg0 ); return this; } }
	public SafeStringBuilder AppendFormat( string format, object arg0, object arg1 ) { lock ( _syncLock ) { _inner.AppendFormat( format, arg0, arg1 ); return this; } }
	public SafeStringBuilder AppendFormat( string format, object arg0, object arg1, object arg2 ) { lock ( _syncLock ) { _inner.AppendFormat( format, arg0, arg1, arg2 ); return this; } }
	public SafeStringBuilder AppendFormat( string format, params object[] args ) { lock ( _syncLock ) { _inner.AppendFormat( format, args ); return this; } }
	public SafeStringBuilder AppendFormat( IFormatProvider provider, string format, object arg0 ) { lock ( _syncLock ) { _inner.AppendFormat( provider, format, arg0 ); return this; } }
	public SafeStringBuilder AppendFormat( IFormatProvider provider, string format, object arg0, object arg1 ) { lock ( _syncLock ) { _inner.AppendFormat( provider, format, arg0, arg1 ); return this; } }
	public SafeStringBuilder AppendFormat( IFormatProvider provider, string format, object arg0, object arg1, object arg2 ) { lock ( _syncLock ) { _inner.AppendFormat( provider, format, arg0, arg1, arg2 ); return this; } }
	public SafeStringBuilder AppendFormat( IFormatProvider provider, string format, params object[] args ) { lock ( _syncLock ) { _inner.AppendFormat( provider, format, args ); return this; } }

	public SafeStringBuilder AppendJoin<T>( char separator, IEnumerable<T> values ) { lock ( _syncLock ) { _inner.AppendJoin( separator, values ); return this; } }
	public SafeStringBuilder AppendJoin<T>( string separator, IEnumerable<T> values ) { lock ( _syncLock ) { _inner.AppendJoin( separator, values ); return this; } }
	public SafeStringBuilder AppendJoin( char separator, params object[] values ) { lock ( _syncLock ) { _inner.AppendJoin( separator, values ); return this; } }
	public SafeStringBuilder AppendJoin( char separator, params string[] values ) { lock ( _syncLock ) { _inner.AppendJoin( separator, values ); return this; } }
	public SafeStringBuilder AppendJoin( string separator, params object[] values ) { lock ( _syncLock ) { _inner.AppendJoin( separator, values ); return this; } }
	public SafeStringBuilder AppendJoin( string separator, params string[] values ) { lock ( _syncLock ) { _inner.AppendJoin( separator, values ); return this; } }

	public SafeStringBuilder Insert( int index, bool value ) { lock ( _syncLock ) { _inner.Insert( index, value ); return this; } }
	public SafeStringBuilder Insert( int index, byte value ) { lock ( _syncLock ) { _inner.Insert( index, value ); return this; } }
	public SafeStringBuilder Insert( int index, char value ) { lock ( _syncLock ) { _inner.Insert( index, value ); return this; } }
	public SafeStringBuilder Insert( int index, char[] value ) { lock ( _syncLock ) { _inner.Insert( index, value ); return this; } }
	public SafeStringBuilder Insert( int index, char[] value, int startIndex, int charCount ) { lock ( _syncLock ) { _inner.Insert( index, value, startIndex, charCount ); return this; } }
	public SafeStringBuilder Insert( int index, decimal value ) { lock ( _syncLock ) { _inner.Insert( index, value ); return this; } }
	public SafeStringBuilder Insert( int index, double value ) { lock ( _syncLock ) { _inner.Insert( index, value ); return this; } }
	public SafeStringBuilder Insert( int index, float value ) { lock ( _syncLock ) { _inner.Insert( index, value ); return this; } }
	public SafeStringBuilder Insert( int index, int value ) { lock ( _syncLock ) { _inner.Insert( index, value ); return this; } }
	public SafeStringBuilder Insert( int index, long value ) { lock ( _syncLock ) { _inner.Insert( index, value ); return this; } }
	public SafeStringBuilder Insert( int index, object value ) { lock ( _syncLock ) { _inner.Insert( index, value ); return this; } }
	public SafeStringBuilder Insert( int index, ReadOnlySpan<char> value ) { lock ( _syncLock ) { _inner.Insert( index, value ); return this; } }
	public SafeStringBuilder Insert( int index, sbyte value ) { lock ( _syncLock ) { _inner.Insert( index, value ); return this; } }
	public SafeStringBuilder Insert( int index, short value ) { lock ( _syncLock ) { _inner.Insert( index, value ); return this; } }
	public SafeStringBuilder Insert( int index, string value ) { lock ( _syncLock ) { _inner.Insert( index, value ); return this; } }
	public SafeStringBuilder Insert( int index, string value, int count ) { lock ( _syncLock ) { _inner.Insert( index, value, count ); return this; } }
	public SafeStringBuilder Insert( int index, uint value ) { lock ( _syncLock ) { _inner.Insert( index, value ); return this; } }
	public SafeStringBuilder Insert( int index, ulong value ) { lock ( _syncLock ) { _inner.Insert( index, value ); return this; } }
	public SafeStringBuilder Insert( int index, ushort value ) { lock ( _syncLock ) { _inner.Insert( index, value ); return this; } }

	public SafeStringBuilder Replace( char oldChar, char newChar ) { lock ( _syncLock ) { _inner.Replace( oldChar, newChar ); return this; } }
	public SafeStringBuilder Replace( char oldChar, char newChar, int startIndex, int count ) { lock ( _syncLock ) { _inner.Replace( oldChar, newChar, startIndex, count ); return this; } }
	public SafeStringBuilder Replace( string oldValue, string newValue ) { lock ( _syncLock ) { _inner.Replace( oldValue, newValue ); return this; } }
	public SafeStringBuilder Replace( string oldValue, string newValue, int startIndex, int count ) { lock ( _syncLock ) { _inner.Replace( oldValue, newValue, startIndex, count ); return this; } }
}
