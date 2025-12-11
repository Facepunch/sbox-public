using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Sandbox.MovieMaker.Compiled;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>
/// Stores a raw gameplay recording.
/// </summary>
public sealed partial record ProjectSourceClip( Guid Id, MovieClip Clip, JsonObject? Metadata )
{
	private bool PrintMembers( StringBuilder builder )
	{
		builder.Append( $"Id = {Id}" );

		return true;
	}
}
