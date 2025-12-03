using System.Collections.Immutable;

namespace Sandbox.MovieMaker.Compiled;

#nullable enable

/// <summary>
/// Helper methods for working with <see cref="MovieClip"/>, <see cref="ICompiledTrack"/>, or <see cref="ICompiledBlock"/>.
/// </summary>
public static class CompiledClipExtensions
{
	/// <summary>
	/// Create a nested <see cref="ICompiledReferenceTrack"/> that targets a <see cref="Sandbox.GameObject"/> with
	/// the given <paramref name="name"/>.
	/// </summary>
	public static CompiledReferenceTrack<GameObject> GameObject( this CompiledReferenceTrack<GameObject> track, string name, Guid? id = null, Guid? referenceId = null ) =>
		new( id ?? Guid.NewGuid(), name, track, referenceId );

	/// <summary>
	/// Create a nested <see cref="ICompiledReferenceTrack"/> that targets a <see cref="Sandbox.Component"/> with
	/// the given <paramref name="type"/>.
	/// </summary>
	public static ICompiledReferenceTrack Component( this CompiledReferenceTrack<GameObject> track,
		Type type,
		Guid? id = null,
		Guid? referenceId = null ) =>
		(ICompiledReferenceTrack)Activator.CreateInstance(
			typeof( CompiledReferenceTrack<> ).MakeGenericType( type ),
			id ?? Guid.NewGuid(), type.Name, track, referenceId )!;

	/// <summary>
	/// Create a nested <see cref="ICompiledReferenceTrack"/> that targets a <see cref="Sandbox.Component"/> with
	/// the type <typeparamref name="T"/>.
	/// </summary>
	public static CompiledReferenceTrack<T> Component<T>( this CompiledReferenceTrack<GameObject> track, Guid? id = null )
		where T : Component => new( id ?? Guid.NewGuid(), typeof( T ).Name, track );

	/// <summary>
	/// Create a nested <see cref="ICompiledPropertyTrack"/> that targets a property with the given <paramref name="name"/>
	/// in the parent track.
	/// </summary>
	public static CompiledPropertyTrack<T> Property<T>( this ICompiledTrack track, string name, IEnumerable<ICompiledPropertyBlock<T>>? blocks = null ) =>
		new( name, track, blocks?.ToImmutableArray() ?? ImmutableArray<ICompiledPropertyBlock<T>>.Empty );

	/// <summary>
	/// Create a nested <see cref="ICompiledPropertyTrack"/> that targets a property with the given <paramref name="name"/>
	/// in the parent track.
	/// </summary>
	public static ICompiledPropertyTrack Property( this ICompiledTrack track, string name, Type type, IEnumerable<ICompiledPropertyBlock>? blocks = null ) =>
		(ICompiledPropertyTrack)Activator.CreateInstance(
			typeof( CompiledPropertyTrack<> ).MakeGenericType( type ),
			name, track, blocks ?? [] )!;

	/// <summary>
	/// <para>
	/// Helper for creating a compiled child track with the given <paramref name="name"/> and value <paramref name="type"/>.
	/// </para>
	/// <para>
	/// Some special cases if the parent track is a <see cref="Sandbox.GameObject"/> reference track:
	/// <list type="bullet">
	/// <item>
	/// <term><paramref name="type"/> is <see cref="Sandbox.GameObject"/></term>
	/// <description>Returns a game object reference track</description>
	/// </item>
	/// <item>
	/// <term><paramref name="type"/> extends <see cref="Sandbox.Component"/></term>
	/// <description>Returns a component reference track</description>
	/// </item>
	/// </list>
	/// By default, returns a property track.
	/// </para>
	/// </summary>
	public static ICompiledTrack Child( this ICompiledTrack track, string name, Type type )
	{
		if ( track is CompiledReferenceTrack<GameObject> goTrack )
		{
			if ( type == typeof( GameObject ) )
			{
				return goTrack.GameObject( name );
			}

			if ( type.IsAssignableTo( typeof( Component ) ) )
			{
				return goTrack.Component( type );
			}
		}

		return track.Property( name, type );
	}

	/// <summary>
	/// Returns a clone of <paramref name="track"/> with an appended <see cref="CompiledConstantBlock{T}"/> with the given
	/// <paramref name="timeRange"/> and <paramref name="value"/>.
	/// </summary>
	public static CompiledPropertyTrack<T> WithConstant<T>( this CompiledPropertyTrack<T> track,
		MovieTimeRange timeRange, T value )
	{
		return track with
		{
			// ReSharper disable once UseCollectionExpression
			Blocks = track.Blocks.Concat( [new CompiledConstantBlock<T>( timeRange, value )] ).ToImmutableArray()
		};
	}

	/// <summary>
	/// Returns a clone of <paramref name="track"/> with an appended <see cref="CompiledSampleBlock{T}"/> with the given
	/// <paramref name="timeRange"/>, <paramref name="sampleRate"/>, and list of sample <paramref name="values"/>.
	/// </summary>
	public static CompiledPropertyTrack<T> WithSamples<T>( this CompiledPropertyTrack<T> track,
		MovieTimeRange timeRange, int sampleRate, IEnumerable<T> values )
	{
		return track with
		{
			// ReSharper disable UseCollectionExpression
			Blocks = track.Blocks.Concat( [new CompiledSampleBlock<T>( timeRange, 0d, sampleRate, values.ToImmutableArray() )] ).ToImmutableArray()
			// ReSharper enable UseCollectionExpression
		};
	}

	/// <summary>
	/// Interpreting <paramref name="samples"/> as an array of samples taken at a given <paramref name="sampleRate"/>, read
	/// a sample from the array at the given <paramref name="time"/> offset from the first sample. Optionally uses <paramref name="interpolator"/>
	/// to interpolate between samples.
	/// </summary>
	public static T Sample<T>( this IReadOnlyList<T> samples, MovieTime time, int sampleRate, IInterpolator<T>? interpolator )
	{
		var i0 = time.GetFrameIndex( sampleRate, out var remainder );
		var i1 = i0 + 1;

		if ( i0 < 0 ) return samples[0];
		if ( i1 >= samples.Count ) return samples[^1];

		var x0 = samples[i0];

		if ( interpolator is null )
		{
			return x0;
		}

		var t = (float)(remainder.TotalSeconds * sampleRate);
		var x1 = samples[i1];

		return interpolator.Interpolate( x0, x1, t );
	}
}
