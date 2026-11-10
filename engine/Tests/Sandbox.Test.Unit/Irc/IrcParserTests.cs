using Sandbox.Twitch;

namespace IrcTests;

/// <summary>
/// Tests for <see cref="IrcParser"/>, which turns raw Twitch IRC lines into an <see cref="IrcMessage"/>.
/// These are pure - they don't touch the engine - so they're safe to run alongside everything else.
/// </summary>
[TestClass]
[TestCategory( "Irc" )]
public class IrcParserTest
{
	/// <summary>
	/// A tagged PRIVMSG - the common case - should yield the command, sender, channel, body and
	/// every IRCv3 tag, including a tag that was sent with an empty value.
	/// </summary>
	[TestMethod]
	public void PrivMsg_WithTags_ParsesEveryField()
	{
		var line = "@badges=broadcaster/1,subscriber/0;color=#1E90FF;display-name=CoolUser;emotes= " +
			":cooluser!cooluser@cooluser.tmi.twitch.tv PRIVMSG #channel :Hello world";

		var message = IrcParser.Parse( line );

		Assert.AreEqual( IrcCommand.PrivMsg, message.Command );
		Assert.AreEqual( "cooluser", message.User );
		Assert.AreEqual( "channel", message.Channel );
		Assert.AreEqual( "Hello world", message.Message );

		Assert.AreEqual( "CoolUser", message.GetTag( "display-name" ) );
		Assert.AreEqual( "#1E90FF", message.GetTag( "color" ) );
		Assert.AreEqual( "broadcaster/1,subscriber/0", message.GetTag( "badges" ) );
		Assert.AreEqual( "", message.GetTag( "emotes" ) );
		Assert.IsNull( message.GetTag( "not-a-real-tag" ) );
	}

	/// <summary>
	/// A PING is server-originated: a command and trailing text, but no prefix, channel or tags.
	/// </summary>
	[TestMethod]
	public void Ping_HasTrailingButNoUserChannelOrTags()
	{
		var message = IrcParser.Parse( "PING :tmi.twitch.tv" );

		Assert.AreEqual( IrcCommand.Ping, message.Command );
		Assert.AreEqual( "tmi.twitch.tv", message.Message );
		Assert.IsNull( message.User );
		Assert.IsNull( message.Channel );
		Assert.IsNull( message.GetTag( "anything" ) );
	}

	/// <summary>
	/// A JOIN carries a prefix and channel but no trailing text, so the message body is null.
	/// </summary>
	[TestMethod]
	public void Join_ParsesUserAndChannel()
	{
		var message = IrcParser.Parse( ":cooluser!cooluser@cooluser.tmi.twitch.tv JOIN #channel" );

		Assert.AreEqual( IrcCommand.Join, message.Command );
		Assert.AreEqual( "cooluser", message.User );
		Assert.AreEqual( "channel", message.Channel );
		Assert.IsNull( message.Message );
	}

	/// <summary>
	/// A PART mirrors JOIN - prefix and channel, no body.
	/// </summary>
	[TestMethod]
	public void Part_ParsesUserAndChannel()
	{
		var message = IrcParser.Parse( ":cooluser!cooluser@cooluser.tmi.twitch.tv PART #channel" );

		Assert.AreEqual( IrcCommand.Part, message.Command );
		Assert.AreEqual( "cooluser", message.User );
		Assert.AreEqual( "channel", message.Channel );
	}

	/// <summary>
	/// A 353 NAMES reply puts the channel partway through the params ("nick = #channel") and the
	/// space-separated member list in the trailing text - both must be picked out correctly.
	/// </summary>
	[TestMethod]
	public void NamesReply_ParsesChannelAndMemberList()
	{
		var message = IrcParser.Parse( ":cooluser.tmi.twitch.tv 353 cooluser = #channel :alice bob carol" );

		Assert.AreEqual( IrcCommand.RPL_353, message.Command );
		Assert.AreEqual( "channel", message.Channel );
		Assert.AreEqual( "alice bob carol", message.Message );
	}

	/// <summary>
	/// The auth-failure NOTICE has a server prefix with no '!', so the whole prefix is the user, the
	/// "*" target isn't a channel, and the reason comes through as the body.
	/// </summary>
	[TestMethod]
	public void Notice_AuthFailure_ParsesServerPrefixAndReason()
	{
		var message = IrcParser.Parse( ":tmi.twitch.tv NOTICE * :Login authentication failed" );

		Assert.AreEqual( IrcCommand.Notice, message.Command );
		Assert.AreEqual( "tmi.twitch.tv", message.User );
		Assert.IsNull( message.Channel );
		Assert.AreEqual( "Login authentication failed", message.Message );
	}

	/// <summary>
	/// Only the first ':' delimits the trailing text - any colons inside the message body must be
	/// preserved verbatim.
	/// </summary>
	[TestMethod]
	public void Message_WithColonsInBody_KeepsThemAll()
	{
		var message = IrcParser.Parse( ":u!u@u.tmi.twitch.tv PRIVMSG #chan :hello: world :)" );

		Assert.AreEqual( "chan", message.Channel );
		Assert.AreEqual( "hello: world :)", message.Message );
	}

	/// <summary>
	/// A command we don't handle parses to <see cref="IrcCommand.Unknown"/> rather than throwing.
	/// </summary>
	[TestMethod]
	public void UnhandledCommand_BecomesUnknown()
	{
		var message = IrcParser.Parse( ":tmi.twitch.tv USERSTATE #channel" );

		Assert.AreEqual( IrcCommand.Unknown, message.Command );
	}

	/// <summary>
	/// Tag parsing covers the three IRCv3 shapes: a valueless tag (maps to "1"), a normal key=value,
	/// and a key with an explicitly empty value.
	/// </summary>
	[TestMethod]
	public void Tags_HandleValueless_Valued_AndEmpty()
	{
		var message = IrcParser.Parse( "@first;color=red;empty= :u!u@u.tmi.twitch.tv PRIVMSG #c :hi" );

		Assert.AreEqual( "1", message.GetTag( "first" ) );
		Assert.AreEqual( "red", message.GetTag( "color" ) );
		Assert.AreEqual( "", message.GetTag( "empty" ) );
	}

	/// <summary>
	/// A cheer is a normal PRIVMSG that carries "bits" and "first-msg" tags - the parser must surface
	/// both alongside the message body.
	/// </summary>
	[TestMethod]
	public void PrivMsg_Cheer_ExposesBitsAndFirstMessageTags()
	{
		var line = "@bits=100;first-msg=1;display-name=Cheerer;user-id=555 " +
			":cheerer!cheerer@cheerer.tmi.twitch.tv PRIVMSG #channel :cheer100 nice";

		var message = IrcParser.Parse( line );

		Assert.AreEqual( IrcCommand.PrivMsg, message.Command );
		Assert.AreEqual( "100", message.GetTag( "bits" ) );
		Assert.AreEqual( "1", message.GetTag( "first-msg" ) );
		Assert.AreEqual( "555", message.GetTag( "user-id" ) );
		Assert.AreEqual( "cheer100 nice", message.Message );
	}

	/// <summary>
	/// A resub USERNOTICE parses to <see cref="IrcCommand.UserNotice"/> with its msg-id and msg-param
	/// detail tags intact, and the resub message as the body.
	/// </summary>
	[TestMethod]
	public void UserNotice_Resub_ExposesMsgParamTags()
	{
		var line = "@msg-id=resub;msg-param-cumulative-months=12;msg-param-streak-months=3;" +
			"msg-param-sub-plan=1000;display-name=Subby;login=subby;user-id=777 " +
			":tmi.twitch.tv USERNOTICE #channel :Loving the stream";

		var message = IrcParser.Parse( line );

		Assert.AreEqual( IrcCommand.UserNotice, message.Command );
		Assert.AreEqual( "resub", message.GetTag( "msg-id" ) );
		Assert.AreEqual( "12", message.GetTag( "msg-param-cumulative-months" ) );
		Assert.AreEqual( "3", message.GetTag( "msg-param-streak-months" ) );
		Assert.AreEqual( "1000", message.GetTag( "msg-param-sub-plan" ) );
		Assert.AreEqual( "subby", message.GetTag( "login" ) );
		Assert.AreEqual( "Loving the stream", message.Message );
	}

	/// <summary>
	/// A raid USERNOTICE carries the raider and viewer count in camelCase msg-param tags.
	/// </summary>
	[TestMethod]
	public void UserNotice_Raid_ExposesRaiderTags()
	{
		var line = "@msg-id=raid;msg-param-displayName=BigStreamer;msg-param-login=bigstreamer;" +
			"msg-param-viewerCount=250 :tmi.twitch.tv USERNOTICE #channel";

		var message = IrcParser.Parse( line );

		Assert.AreEqual( IrcCommand.UserNotice, message.Command );
		Assert.AreEqual( "raid", message.GetTag( "msg-id" ) );
		Assert.AreEqual( "BigStreamer", message.GetTag( "msg-param-displayName" ) );
		Assert.AreEqual( "250", message.GetTag( "msg-param-viewerCount" ) );
	}

	/// <summary>
	/// The RECONNECT command (Twitch asking us to reconnect) parses to its own command.
	/// </summary>
	[TestMethod]
	public void Reconnect_IsRecognised()
	{
		var message = IrcParser.Parse( ":tmi.twitch.tv RECONNECT" );

		Assert.AreEqual( IrcCommand.Reconnect, message.Command );
	}
}
