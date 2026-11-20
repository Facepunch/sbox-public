using System;
using System.Text;

#nullable enable

namespace HotloadTests;

/// <summary>
/// A type compiled from a test's source by <see cref="HotloadFixtureTestBase.HotloadFixtureAsync"/>. One instance is
/// created from the first version of the source, then hotloaded to the second, with <see cref="Test"/> called
/// on it before and after. Lives in a non-hotloaded assembly, so both versions share it.
/// </summary>
public interface IHotloadTestFixture
{
	/// <summary>
	/// Called on the instance before and after hotload. The first version can use it to set up state.
	/// </summary>
	void Test() { }
}

/// <summary>
/// Splits a single source with diff-like markers into the two versions of a file. A line starting
/// with <c>-</c> is only in the first version, and one starting with <c>+</c> only in the second.
/// The marker is removed, so with tab indentation, marked lines line up with unmarked ones. Only
/// column 0 is checked.
/// </summary>
public static class DiffSource
{
	public static (string Before, string After) Parse( string diff )
	{
		var before = new StringBuilder();
		var after = new StringBuilder();

		foreach ( var rawLine in diff.Split( '\n' ) )
		{
			var line = rawLine.TrimEnd( '\r' );
			var marker = line.Length > 0 ? line[0] : '\0';

			if ( marker is '+' or '-' )
			{
				(marker == '+' ? after : before).Append( line, 1, line.Length - 1 ).Append( '\n' );
				continue;
			}

			before.Append( line ).Append( '\n' );
			after.Append( line ).Append( '\n' );
		}

		return (before.ToString(), after.ToString());
	}
}
