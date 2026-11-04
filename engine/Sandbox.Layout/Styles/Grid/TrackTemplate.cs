using System.Collections.ObjectModel;
using System.Text;

namespace Sandbox.Layout;

/// <summary>How many times a <c>repeat()</c> component repeats.</summary>
internal enum RepetitionKind : byte
{
	Count,
	AutoFill,
	AutoFit,
}

/// <summary>
/// One entry of a <c>grid-template-columns</c> / <c>grid-template-rows</c> list: a single track, or a
/// <c>repeat()</c> of several tracks. Line names sit between tracks.
/// </summary>
internal sealed class TrackTemplateComponent : IEquatable<TrackTemplateComponent>
{
	private readonly string[] _leadingNames;
	private readonly string[] _repeatLeadingNames;
	private readonly TrackSizingFunction[] _tracks;
	private readonly string[][] _trailingNames;
	private readonly bool _explicitRepeat;

	/// <summary>Line names before the first track of this component.</summary>
	public IReadOnlyList<string> LeadingNames { get; }

	/// <summary>Line names inside <c>repeat()</c>, before the first track of each repetition.</summary>
	public IReadOnlyList<string> RepeatLeadingNames { get; }

	/// <summary>The tracks. One entry unless this is a <c>repeat()</c>.</summary>
	public IReadOnlyList<TrackSizingFunction> Tracks { get; }

	/// <summary>Line names between/after the repeated tracks. Length is Tracks.Count; entry i follows track i.</summary>
	public IReadOnlyList<ReadOnlyCollection<string>> TrailingNames { get; }

	public RepetitionKind Repetition { get; }
	public int RepeatCount { get; }

	internal string[] LeadingNameItems => _leadingNames;
	internal string[] RepeatLeadingNameItems => _repeatLeadingNames;
	internal TrackSizingFunction[] TrackItems => _tracks;
	internal string[][] TrailingNameItems => _trailingNames;

	public bool IsRepeat => Repetition != RepetitionKind.Count || RepeatCount != 1 || _explicitRepeat;
	public bool IsAutoRepeat => Repetition != RepetitionKind.Count;

	public TrackTemplateComponent( TrackSizingFunction track, string[] leadingNames = null, string[] trailingNames = null )
	{
		_leadingNames = Copy( leadingNames );
		_repeatLeadingNames = [];
		_tracks = [track];
		_trailingNames = [Copy( trailingNames )];
		LeadingNames = Array.AsReadOnly( _leadingNames );
		RepeatLeadingNames = Array.AsReadOnly( _repeatLeadingNames );
		Tracks = Array.AsReadOnly( _tracks );
		TrailingNames = ReadOnlyNames( _trailingNames );
		Repetition = RepetitionKind.Count;
		RepeatCount = 1;
	}

	public TrackTemplateComponent(
		RepetitionKind repetition,
		int count,
		TrackSizingFunction[] tracks,
		string[] leadingNames,
		string[][] trailingNames,
		string[] repeatLeadingNames = null )
	{
		if ( tracks is null || tracks.Length == 0 )
		{
			throw new ArgumentException( "A repeated component must contain at least one track.", nameof( tracks ) );
		}

		Repetition = repetition;
		RepeatCount = repetition == RepetitionKind.Count ? Math.Max( 1, count ) : 1;
		_tracks = (TrackSizingFunction[])tracks.Clone();
		_leadingNames = Copy( leadingNames );
		_repeatLeadingNames = Copy( repeatLeadingNames );
		_trailingNames = new string[_tracks.Length][];

		for ( int i = 0; i < _trailingNames.Length; i++ )
		{
			_trailingNames[i] = Copy( trailingNames is not null && i < trailingNames.Length ? trailingNames[i] : null );
		}

		LeadingNames = Array.AsReadOnly( _leadingNames );
		RepeatLeadingNames = Array.AsReadOnly( _repeatLeadingNames );
		Tracks = Array.AsReadOnly( _tracks );
		TrailingNames = ReadOnlyNames( _trailingNames );
		_explicitRepeat = true;
	}

	private static string[] Copy( string[] values )
	{
		return values is null or { Length: 0 } ? [] : (string[])values.Clone();
	}

	private static ReadOnlyCollection<ReadOnlyCollection<string>> ReadOnlyNames( string[][] names )
	{
		var result = new ReadOnlyCollection<string>[names.Length];
		for ( int i = 0; i < names.Length; i++ )
		{
			result[i] = Array.AsReadOnly( names[i] );
		}

		return Array.AsReadOnly( result );
	}

	public bool Equals( TrackTemplateComponent other )
	{
		if ( other is null )
		{
			return false;
		}

		if ( Repetition != other.Repetition || RepeatCount != other.RepeatCount )
		{
			return false;
		}

		if ( _tracks.Length != other._tracks.Length )
		{
			return false;
		}

		for ( int i = 0; i < _tracks.Length; i++ )
		{
			if ( !_tracks[i].Equals( other._tracks[i] ) )
			{
				return false;
			}
		}

		if ( !NamesEqual( _leadingNames, other._leadingNames )
			|| !NamesEqual( _repeatLeadingNames, other._repeatLeadingNames ) )
		{
			return false;
		}

		for ( int i = 0; i < _trailingNames.Length; i++ )
		{
			if ( !NamesEqual( _trailingNames[i], other._trailingNames[i] ) )
			{
				return false;
			}
		}

		return true;
	}

	internal static bool NamesEqual( string[] a, string[] b )
	{
		if ( a.Length != b.Length )
		{
			return false;
		}

		for ( int i = 0; i < a.Length; i++ )
		{
			if ( !string.Equals( a[i], b[i], StringComparison.Ordinal ) )
			{
				return false;
			}
		}

		return true;
	}

	public override bool Equals( object obj ) => obj is TrackTemplateComponent other && Equals( other );

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add( (int)Repetition );
		hash.Add( RepeatCount );

		foreach ( var track in _tracks )
		{
			hash.Add( track );
		}

		foreach ( var name in _leadingNames )
		{
			hash.Add( name, StringComparer.Ordinal );
		}

		foreach ( var name in _repeatLeadingNames )
		{
			hash.Add( name, StringComparer.Ordinal );
		}

		foreach ( var names in _trailingNames )
		{
			foreach ( var name in names )
			{
				hash.Add( name, StringComparer.Ordinal );
			}
		}

		return hash.ToHashCode();
	}
}

/// <summary>A parsed <c>grid-template-columns</c> / <c>grid-template-rows</c> value.</summary>
internal sealed class TrackList : IEquatable<TrackList>
{
	private readonly TrackTemplateComponent[] _components;

	public static readonly TrackList None = new( Array.Empty<TrackTemplateComponent>() );

	public IReadOnlyList<TrackTemplateComponent> Components { get; }

	/// <summary>Whether any component carries <c>[line names]</c>. Grids without them skip building a name table.</summary>
	public bool HasLineNames { get; }

	internal TrackTemplateComponent[] ComponentItems => _components;

	public TrackList( TrackTemplateComponent[] components )
	{
		_components = components is null or { Length: 0 }
			? []
			: (TrackTemplateComponent[])components.Clone();

		if ( Array.Exists( _components, static component => component is null ) )
		{
			throw new ArgumentException( "Track list components cannot be null", nameof( components ) );
		}

		Components = Array.AsReadOnly( _components );
		foreach ( var component in _components )
		{
			if ( component.LeadingNameItems.Length > 0 || component.RepeatLeadingNameItems.Length > 0 )
			{
				HasLineNames = true;
			}

			foreach ( var names in component.TrailingNameItems )
			{
				if ( names.Length > 0 )
				{
					HasLineNames = true;
				}
			}
		}
	}

	public TrackList( params TrackSizingFunction[] tracks )
	{
		tracks ??= [];
		_components = new TrackTemplateComponent[tracks.Length];
		for ( int i = 0; i < tracks.Length; i++ )
		{
			_components[i] = new TrackTemplateComponent( tracks[i] );
		}

		Components = Array.AsReadOnly( _components );
	}

	public bool IsNone => _components.Length == 0;

	/// <summary>Whether this list contains an <c>auto-fill</c> or <c>auto-fit</c> repeat.</summary>
	public bool HasAutoRepeat
	{
		get
		{
			foreach ( var component in _components )
			{
				if ( component.IsAutoRepeat )
				{
					return true;
				}
			}

			return false;
		}
	}

	public bool Equals( TrackList other )
	{
		if ( other is null )
		{
			return false;
		}

		if ( ReferenceEquals( this, other ) )
		{
			return true;
		}

		if ( _components.Length != other._components.Length )
		{
			return false;
		}

		for ( int i = 0; i < _components.Length; i++ )
		{
			if ( !_components[i].Equals( other._components[i] ) )
			{
				return false;
			}
		}

		return true;
	}

	public override bool Equals( object obj ) => obj is TrackList other && Equals( other );

	public override int GetHashCode()
	{
		var hash = new HashCode();
		foreach ( var component in _components )
		{
			hash.Add( component );
		}

		return hash.ToHashCode();
	}

	public override string ToString()
	{
		if ( IsNone )
		{
			return "none";
		}

		var builder = new StringBuilder();
		foreach ( var component in _components )
		{
			if ( builder.Length > 0 )
			{
				builder.Append( ' ' );
			}

			if ( component.LeadingNameItems.Length > 0 )
			{
				builder.Append( '[' ).Append( string.Join( ' ', component.LeadingNameItems ) ).Append( "] " );
			}

			if ( component.IsRepeat )
			{
				builder.Append( "repeat(" );
				builder.Append( component.Repetition switch
				{
					RepetitionKind.AutoFill => "auto-fill",
					RepetitionKind.AutoFit => "auto-fit",
					_ => component.RepeatCount.ToString(),
				} );
				builder.Append( ", " );

				if ( component.RepeatLeadingNameItems.Length > 0 )
				{
					builder.Append( '[' ).Append( string.Join( ' ', component.RepeatLeadingNameItems ) ).Append( "] " );
				}

				for ( int i = 0; i < component.TrackItems.Length; i++ )
				{
					if ( i > 0 )
					{
						builder.Append( ' ' );
					}

					builder.Append( component.TrackItems[i] );
					if ( component.TrailingNameItems[i].Length > 0 )
					{
						builder.Append( " [" ).Append( string.Join( ' ', component.TrailingNameItems[i] ) ).Append( ']' );
					}
				}

				builder.Append( ')' );
			}
			else
			{
				builder.Append( component.TrackItems[0] );
				if ( component.TrailingNameItems[0].Length > 0 )
				{
					builder.Append( " [" ).Append( string.Join( ' ', component.TrailingNameItems[0] ) ).Append( ']' );
				}
			}
		}

		return builder.ToString();
	}
}
