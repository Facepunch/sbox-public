namespace Sandbox.Generator
{
	internal static class DiagnosticIds
	{
		// Lets try and organize these in the future so people can ignore shit they don't care about

		// Replication 200-299
		public const string ReplicatedUnknownType = "SB200";
		public const string ReplicatedStaticUnsupported = "SB201";
		public const string ReplicatedNonAutoProp = "SB202";
		public const string ReplicatedEntityComponent = "SB203";
		public const string ReplicatedListNotIList = "SB204";
		public const string ReplicatedDictionaryNotIDictionary = "SB205";
		public const string ReplicatedDictionaryUnsupportedType = "SB206";

		// ConCmd
		public const string ConCmdNotStatic = "SB300";

		// Hotload 400-499
		public const string HotloadInheritedFieldsNotInitialized = "SB400";
	}
}
