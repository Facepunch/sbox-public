using System;

namespace Services;

[TestClass]
public class LeaderboardTest
{
	[TestMethod]
	public async Task Basic()
	{
		var board = Sandbox.Services.Leaderboards.GetFromStat( "facepunch.ss1", "zombies_killed" );
		board.MaxEntries = 10;
		board.Offset = 0;

		await board.Refresh();

		System.Console.WriteLine( $"Entries: {board.TotalEntries}" );

		foreach ( var entry in board.Entries )
		{
			System.Console.WriteLine( $"{entry.Rank} - {entry.DisplayName} - {entry.Value}" );
		}

		Assert.IsTrue( board.TotalEntries > 0 );
		Assert.AreEqual( 10, board.Entries.Length );
		Assert.AreEqual( 1, board.Entries.First().Rank );
	}

	[TestMethod]
	[DataRow( "gb" )]
	[DataRow( "us" )]
	public async Task Country( string countrycode )
	{
		var board = Sandbox.Services.Leaderboards.GetFromStat( "facepunch.ss1", "zombies_killed" );
		board.SetCountryCode( countrycode );
		board.MaxEntries = 100;

		await board.Refresh();

		System.Console.WriteLine( $"Entries: {board.TotalEntries}" );

		foreach ( var entry in board.Entries )
		{
			System.Console.WriteLine( $"{entry.Rank} - {entry.DisplayName} - {entry.Value} [{entry.CountryCode}]" );
			Assert.AreEqual( countrycode, entry.CountryCode );
		}
	}

	[TestMethod]
	public async Task Year()
	{
		var board = Sandbox.Services.Leaderboards.GetFromStat( "facepunch.ss1", "zombies_killed" );
		board.FilterByYear();
		board.SetDatePeriod( new System.DateTime( 2024, 9, 1 ) );
		board.MaxEntries = 10;

		await board.Refresh();

		System.Console.WriteLine( $"Entries: {board.TotalEntries} - {board.TimePeriodDescription}" );

		foreach ( var entry in board.Entries )
		{
			System.Console.WriteLine( $"{entry.Rank} - {entry.DisplayName} - {entry.Value} [{entry.CountryCode}]" );
		}

		Assert.IsTrue( board.TotalEntries >= 2588 );
	}

	[TestMethod]
	public async Task Month()
	{
		var board = Sandbox.Services.Leaderboards.GetFromStat( "facepunch.ss1", "zombies_killed" );
		board.FilterByMonth();
		board.SetDatePeriod( new System.DateTime( 2024, 8, 1 ) );
		board.MaxEntries = 10;

		await board.Refresh();

		System.Console.WriteLine( $"Entries: {board.TotalEntries} - {board.TimePeriodDescription}" );

		foreach ( var entry in board.Entries )
		{
			System.Console.WriteLine( $"{entry.Rank} - {entry.DisplayName} - {entry.Value} [{entry.CountryCode}]" );
		}

		Assert.IsTrue( board.TotalEntries >= 1 );
	}

	[TestMethod]
	public async Task Week()
	{
		var board = Sandbox.Services.Leaderboards.GetFromStat( "facepunch.ss1", "zombies_killed" );
		board.FilterByWeek();
		board.SetDatePeriod( new System.DateTime( 2024, 8, 15 ) );
		board.MaxEntries = 10;

		await board.Refresh();

		System.Console.WriteLine( $"Entries: {board.TotalEntries} - {board.TimePeriodDescription}" );

		foreach ( var entry in board.Entries )
		{
			System.Console.WriteLine( $"{entry.Rank} - {entry.DisplayName} - {entry.Value} [{entry.CountryCode}]" );
		}

		Assert.IsTrue( board.TotalEntries >= 1 );
	}

	[TestMethod]
	public async Task Day()
	{
		var board = Sandbox.Services.Leaderboards.GetFromStat( "facepunch.ss1", "zombies_killed" );
		board.FilterByDay();
		board.SetDatePeriod( new System.DateTime( 2024, 8, 21 ) );
		board.MaxEntries = 10;

		await board.Refresh();

		System.Console.WriteLine( $"Entries: {board.TotalEntries} - {board.TimePeriodDescription}" );

		foreach ( var entry in board.Entries )
		{
			System.Console.WriteLine( $"{entry.Rank} - {entry.DisplayName} - {entry.Value} [{entry.CountryCode}]" );
		}

		Assert.IsTrue( board.TotalEntries >= 1 );
	}

	[TestMethod]
	public async Task CenterOnSteamId()
	{
		var board = Sandbox.Services.Leaderboards.GetFromStat( "facepunch.ss1", "zombies_killed" );
		board.FilterByWeek();
		board.SetDatePeriod( new System.DateTime( 2024, 8, 20 ) );
		board.MaxEntries = 10;

		await board.Refresh();

		Assert.IsTrue( board.TotalEntries >= 463 );
		Assert.IsFalse( board.Entries.Any( x => x.SteamId == 76561197960279927 ), "Not in the list" );

		System.Console.WriteLine( $"Entries: {board.TotalEntries}" );

		foreach ( var entry in board.Entries )
		{
			System.Console.WriteLine( $"{entry.Rank} - {entry.DisplayName} - {entry.Value} [{entry.CountryCode}]" );
		}

		board.CenterOnSteamId( 76561197960279927 );
		await board.Refresh();

		System.Console.WriteLine( $"Entries: {board.TotalEntries}" );

		foreach ( var entry in board.Entries )
		{
			System.Console.WriteLine( $"{entry.Rank} - {entry.DisplayName} - {entry.Value} [{entry.CountryCode}]" );
		}

		Assert.IsTrue( board.TotalEntries >= 1 );
		Assert.IsTrue( board.Entries.Any( x => x.SteamId == 76561197960279927 ) );
	}

	[TestMethod]
	public async Task IncludeSteamId()
	{
		var board = Sandbox.Services.Leaderboards.GetFromStat( "facepunch.ss1", "zombies_killed" );
		board.FilterByWeek();
		board.SetDatePeriod( new System.DateTime( 2024, 8, 20 ) );
		board.MaxEntries = 10;

		await board.Refresh();

		Assert.IsTrue( board.TotalEntries >= 463 );
		Assert.IsFalse( board.Entries.Any( x => x.SteamId == 76561197960279927 ), "Not in the list" );

		System.Console.WriteLine( $"Entries: {board.TotalEntries}" );

		foreach ( var entry in board.Entries )
		{
			System.Console.WriteLine( $"{entry.Rank} - {entry.DisplayName} - {entry.Value} [{entry.CountryCode}]" );
		}

		board.IncludeSteamIds( 76561197960279927 );
		await board.Refresh();

		System.Console.WriteLine( $"Entries: {board.TotalEntries}" );

		foreach ( var entry in board.Entries )
		{
			System.Console.WriteLine( $"{entry.Rank} - {entry.DisplayName} - {entry.Value} [{entry.CountryCode}]" );
		}

		Assert.IsTrue( board.TotalEntries >= 1 );
		Assert.IsTrue( board.Entries.Any( x => x.SteamId == 76561197960279927 ) );
	}

	[TestMethod]
	public async Task Ascending()
	{
		var board = Sandbox.Services.Leaderboards.GetFromStat( "facepunch.ss1", "zombies_killed" );
		board.FilterByMonth();
		board.SetSortAscending();
		board.SetDatePeriod( new System.DateTime( 2024, 8, 1 ) );
		board.MaxEntries = 100;

		await board.Refresh();

		System.Console.WriteLine( $"Entries: {board.TotalEntries}" );

		foreach ( var entry in board.Entries )
		{
			System.Console.WriteLine( $"{entry.Rank} - {entry.DisplayName} - {entry.Value} [{entry.CountryCode}]" );
		}

		Assert.IsTrue( board.TotalEntries >= 1 );
	}

	[TestMethod]
	public async Task MinValue()
	{
		var board = Sandbox.Services.Leaderboards.GetFromStat( "facepunch.ss1", "victory_elapsed_time" );
		board.FilterByYear();
		board.SetAggregationMin();
		board.SetSortAscending();
		board.SetDatePeriod( new System.DateTime( 2024, 8, 1 ) );
		board.MaxEntries = 100;

		await board.Refresh();

		System.Console.WriteLine( $"Entries: {board.TotalEntries} - {board.TimePeriodDescription}" );

		foreach ( var entry in board.Entries )
		{
			System.Console.WriteLine( $"{entry.Rank} - {entry.DisplayName} - {entry.Value} [{entry.Timestamp}]" );
			Assert.AreNotEqual( default( DateTimeOffset ), entry.Timestamp );
		}

		Assert.IsTrue( board.TotalEntries >= 1 );
	}

}
