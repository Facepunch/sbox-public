using Sandbox;

public partial class TestStackOverflow
{
	public static void RecurseForever()
	{
		RecurseForever();

		// this is line 9
	}

	public static void RecurseForeverInline()
	{
		System.Action f = null;

		f = () =>
		{
			f();
		};

		f();
	}
}
