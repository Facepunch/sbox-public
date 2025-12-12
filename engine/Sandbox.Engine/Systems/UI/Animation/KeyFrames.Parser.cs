namespace Sandbox.UI;

public partial class KeyFrames
{
	internal static KeyFrames Parse( ref Parse p, StyleSheet sheet )
	{
		// starts with @keyframes
		if ( !p.TrySkip( "@keyframes", 0, true ) )
			return null;

		p.SkipWhitespaceAndNewlines();

		var frames = new KeyFrames();

		// then has a name for the keyframes
		frames.Name = p.ReadWord( " {", true );

		// assert restrictions on name here
		if ( string.IsNullOrWhiteSpace( frames.Name ) )
			throw new System.Exception( $"Expected name after @keyframes {p.FileAndLine}" );

		p.SkipWhitespaceAndNewlines();

		// should then have a {
		if ( !p.TrySkip( "{" ) )
			throw new System.Exception( $"Expected {{ {p.FileAndLine}" );

		// the inner section has a percentage (or from/to), followed by a block of styles
		// this is repeated with no separator.
		while ( !p.IsEnd && p.Current != '}' )
		{
			var intervals = new List<float>();

			// read all percentages
			while ( !p.IsEnd )
			{
				p.SkipWhitespaceAndNewlines();

				// read the percentage
				if ( !p.TryReadLength( out var length ) )
					throw new System.Exception( $"Expected % {p.FileAndLine}" );

				// make sure they're not feeding us 100px or some shit
				if ( length.Unit != LengthUnit.Percentage )
					throw new System.Exception( $"Expected % {p.FileAndLine}" );

				var interval = length.GetFraction();
				intervals.Add( interval );

				// this is technically allowed but lets put our foot down right now
				if ( frames.Blocks.Any( x => x.Interval == interval ) )
					throw new System.Exception( $"Duplicate interval ({length}) {p.FileAndLine}" );

				p.SkipWhitespaceAndNewlines();

				// if the next character is {, break out - otherwise expect another value
				if ( p.Current == '{' )
					break;
				else if ( !char.IsNumber( p.Current ) )
					throw new System.Exception( $"Expected {{ or , {p.FileAndLine}" );
			}

			// next we should have a value style block enclosed in { }.
			var styles = new Styles();
			StyleParser.ParseStyles( ref p, styles, true, sheet );

			foreach ( var interval in intervals )
			{
				var block = new Block();
				block.Interval = interval;
				block.Styles = styles;

				frames.Blocks.Add( block );
			}

			// strip any whitespace so if we're at the end we'll see the }
			p.SkipWhitespaceAndNewlines();
		}

		if ( p.IsEnd )
			throw new System.Exception( $"Expected }} {p.FileAndLine}" );

		// should end in } if we parsed properly
		if ( !p.TrySkip( "}" ) )
			throw new System.Exception( $"Expected }} {p.FileAndLine}" );

		frames.Blocks = frames.Blocks.OrderBy( x => x.Interval ).ToList();

		return frames;
	}
}
