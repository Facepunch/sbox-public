namespace SystemTest;

[TestClass]
public class StringExtensions
{
	[TestMethod]
	public void SplitQuotesStrings()
	{
		{
			var parts = "one two three".SplitQuotesStrings();

			Assert.IsNotNull( parts );
			Assert.AreEqual( 3, parts.Length );
			Assert.AreEqual( "one", parts[0] );
			Assert.AreEqual( "two", parts[1] );
			Assert.AreEqual( "three", parts[2] );
		}

		{
			var parts = "one \"two three\"".SplitQuotesStrings();

			Assert.IsNotNull( parts );
			Assert.AreEqual( 2, parts.Length );
			Assert.AreEqual( "one", parts[0] );
			Assert.AreEqual( "two three", parts[1] );
		}

		{
			var parts = "one \"t\\\"w\\\"o\" three".SplitQuotesStrings();

			Assert.IsNotNull( parts );
			Assert.AreEqual( 3, parts.Length );
			Assert.AreEqual( "one", parts[0] );
			Assert.AreEqual( "t\"w\"o", parts[1] );
			Assert.AreEqual( "three", parts[2] );
		}

		{
			var parts = "  \"one\" \"two\"   \"three\" ".SplitQuotesStrings();

			Assert.IsNotNull( parts );
			Assert.AreEqual( 3, parts.Length );
			Assert.AreEqual( "one", parts[0] );
			Assert.AreEqual( "two", parts[1] );
			Assert.AreEqual( "three", parts[2] );
		}

		{
			var parts = "\"one \" 'two' three".SplitQuotesStrings();

			Assert.IsNotNull( parts );
			Assert.AreEqual( 3, parts.Length );
			Assert.AreEqual( "one ", parts[0] );
			Assert.AreEqual( "two", parts[1] );
			Assert.AreEqual( "three", parts[2] );
		}

		{
			var parts = "one \"two\" \"\" four".SplitQuotesStrings();

			Assert.IsNotNull( parts );
			Assert.AreEqual( 4, parts.Length );
			Assert.AreEqual( "one", parts[0] );
			Assert.AreEqual( "two", parts[1] );
			Assert.AreEqual( "", parts[2] );
			Assert.AreEqual( "four", parts[3] );
		}

		{
			var parts = "one \"two's company three is a crow'd\"".SplitQuotesStrings();

			Assert.IsNotNull( parts );
			Assert.AreEqual( 2, parts.Length );
			Assert.AreEqual( "one", parts[0] );
			Assert.AreEqual( "two's company three is a crow'd", parts[1] );
		}
	}

	[TestMethod]
	public void QuoteSafe()
	{
		Assert.AreEqual( "\"\"", "".QuoteSafe() );
		Assert.AreEqual( "\"\"", "".QuoteSafe( false ) );
		Assert.AreEqual( "\"\"", "".QuoteSafe( true ) );

		var str = "test";
		Assert.AreEqual( "\"test\"", str.QuoteSafe() );
		Assert.AreEqual( "\"test\"", str.QuoteSafe( false ) );
		Assert.AreEqual( "test", str.QuoteSafe( true ) );

		str = "hello sir";
		Assert.AreEqual( "\"hello sir\"", str.QuoteSafe() );
		Assert.AreEqual( "\"hello sir\"", str.QuoteSafe( false ) );
		Assert.AreEqual( "\"hello sir\"", str.QuoteSafe( true ) );

		str = "http://facepunch.com/hellosir";
		Assert.AreEqual( "\"http://facepunch.com/hellosir\"", str.QuoteSafe() );
		Assert.AreEqual( "\"http://facepunch.com/hellosir\"", str.QuoteSafe( false ) );
		Assert.AreEqual( "http://facepunch.com/hellosir", str.QuoteSafe( true ) );

	}

	[TestMethod]
	public void TitleCase()
	{
		Assert.AreEqual( "Hello World", "Hello World".ToTitleCase() );
		Assert.AreEqual( "Hello World", "hello world".ToTitleCase() );
		Assert.AreEqual( "Hello World", "hello-world".ToTitleCase() );
		Assert.AreEqual( "Hello World", "hello.world".ToTitleCase() );
		Assert.AreEqual( "Hello World", "hello_world".ToTitleCase() );
		Assert.AreEqual( "Hello World", "helloWorld".ToTitleCase() );
		Assert.AreEqual( "Hello World 10", "helloWorld10".ToTitleCase() );
		Assert.AreEqual( "HELLO WORLD", "HELLO WORLD".ToTitleCase() );
		Assert.AreEqual( "Hello World", "Hello    World".ToTitleCase() );
		Assert.AreEqual( "Hello World", "__hello_world".ToTitleCase() );
	}

	[TestMethod]
	public void TitleCaseDate()
	{
		Assert.AreEqual( "Hello World 2022-09-08", "HelloWorld2022-09-08".ToTitleCase() );
		Assert.AreEqual( "Hello World 2022-09-08", "Hello-World-2022-09-08".ToTitleCase() );
		Assert.AreEqual( "2022-09-08", "-2022-09-08-".ToTitleCase() );
		Assert.AreEqual( "2022-09", "-2022-09-".ToTitleCase() );
	}


	[TestMethod]
	public void Wildcards()
	{
		Assert.IsTrue( "one two three".WildcardMatch( "*two*" ) );
		Assert.IsFalse( "one two three".WildcardMatch( "*banana*" ) );
		Assert.IsTrue( "one two three".WildcardMatch( "*three" ) );
		Assert.IsTrue( "one two three".WildcardMatch( "one*" ) );
		Assert.IsFalse( "one two three".WildcardMatch( "apple*" ) );
		Assert.IsTrue( "one two three".WildcardMatch( "ONE Two Thr*" ) );
		Assert.IsTrue( "one two three".WildcardMatch( "one two three" ) );
		Assert.IsTrue( "one two three".WildcardMatch( "one TWO three" ) );
		Assert.IsFalse( "one two three".WildcardMatch( "seven eight nine" ) );
	}

	[TestMethod]
	public void StringFloatEval()
	{
		Assert.AreEqual( "1+1".ToFloatEval(), 2 );
		Assert.AreEqual( "10*10".ToFloatEval(), 100 );
		Assert.AreEqual( "2 + 3 * 2".ToFloatEval(), 8 );

		// should not be accessible
		Assert.AreEqual( "Regex.Match(\"Test 34 Hello/-World\", @\"\\d+\").Value".ToFloatEval(), 0 );
		Assert.AreEqual( "new(Random).Next(1,10)".ToFloatEval(), 0 );
		Assert.AreEqual( "Enumerable.Range(1,4).Cast().Sum(x =>(int)x)".ToFloatEval(), 0 );
		Assert.AreEqual( "((x, y) => x * y)(4, 2)".ToFloatEval(), 0 );
	}

	[TestMethod]
	[DataRow( "hello", ".world", "hello.world" )]
	[DataRow( "hello", "world", "hello.world" )]
	[DataRow( "hello.WORLD", "world", "hello.WORLD" )]
	[DataRow( "hello.txt", ".world", "hello.world" )]
	[DataRow( "hello.txt", "world", "hello.world" )]
	[DataRow( "folder/hello.txt", "world", "folder/hello.world" )]
	[DataRow( "folder\\hello.txt", "world", "folder\\hello.world" )]
	[DataRow( "folder/hello.world.txt", "pdf", "folder/hello.world.pdf" )]
	public void WithExtension( string path, string ext, string expected )
	{
		Assert.AreEqual( expected, path.WithExtension( ext ) );
	}
}
