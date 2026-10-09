namespace Sandbox.UI;

/// <summary>Eight inline stop slots. Copies own their stops; adding a stop never allocates.</summary>
[SkipHotload]
internal struct GradientStops : IEquatable<GradientStops>
{
	Styles.GradientColorOffset Stop1;
	Styles.GradientColorOffset Stop2;
	Styles.GradientColorOffset Stop3;
	Styles.GradientColorOffset Stop4;
	Styles.GradientColorOffset Stop5;
	Styles.GradientColorOffset Stop6;
	Styles.GradientColorOffset Stop7;
	Styles.GradientColorOffset Stop8;

	public int Length { readonly get; private set; }
	public readonly bool IsDefaultOrEmpty => Length == 0;

	public readonly Styles.GradientColorOffset this[int index]
	{
		get
		{
			ArgumentOutOfRangeException.ThrowIfNegative( index );
			ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual( index, Length );
			return index switch
			{
				0 => Stop1,
				1 => Stop2,
				2 => Stop3,
				3 => Stop4,
				4 => Stop5,
				5 => Stop6,
				6 => Stop7,
				7 => Stop8,
				_ => throw new ArgumentOutOfRangeException( nameof( index ) )
			};
		}
	}

	public readonly GradientStops Add( Styles.GradientColorOffset stop )
	{
		if ( Length == GradientInfo.MaxStops )
			throw new ArgumentOutOfRangeException( nameof( stop ), "Gradients support at most 8 stops." );
		var result = this;
		switch ( Length )
		{
			case 0: result.Stop1 = stop; break;
			case 1: result.Stop2 = stop; break;
			case 2: result.Stop3 = stop; break;
			case 3: result.Stop4 = stop; break;
			case 4: result.Stop5 = stop; break;
			case 5: result.Stop6 = stop; break;
			case 6: result.Stop7 = stop; break;
			case 7: result.Stop8 = stop; break;
		}
		result.Length++;
		return result;
	}

	public static GradientStops Create( ReadOnlySpan<Styles.GradientColorOffset> stops )
	{
		var result = new GradientStops();
		foreach ( var stop in stops ) result = result.Add( stop );
		return result;
	}

	public readonly bool Equals( GradientStops other )
	{
		if ( Length != other.Length ) return false;
		for ( int i = 0; i < Length; i++ )
		{
			var a = this[i];
			var b = other[i];
			if ( !a.color.Equals( b.color ) || !Nullable.Equals( a.offset, b.offset ) || a.offsetIsPixels != b.offsetIsPixels )
				return false;
		}
		return true;
	}

	public readonly override bool Equals( object obj ) => obj is GradientStops other && Equals( other );

	public readonly override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add( Length );
		for ( int i = 0; i < Length; i++ ) hash.Add( this[i].GetHashCode() );
		return hash.ToHashCode();
	}
}
