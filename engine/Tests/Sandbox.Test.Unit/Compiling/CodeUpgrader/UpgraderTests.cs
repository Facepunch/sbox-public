using System;

using Sandbox.CodeUpgrader;

namespace CodeUpgraderTests;

[TestClass]
[DoNotParallelize]
public partial class UpgraderTest
{
	async Task TestAnalyzer<T>() where T : Analyzer, new()
	{
		var t = new T();
		await t.RunTests( new AnalyzerTest<T>() );
	}

	async Task TestFixer<T>() where T : Fixer, new()
	{
		var t = new T();

		var testType = typeof( FixerTest<,> ).MakeGenericType( t.Analyzer, t.GetType() );
		var test = (IFixerTest)Activator.CreateInstance( testType );

		await t.RunTests( test );
	}


	[TestMethod] public Task BroadcastAttributeAnalyzer() => TestAnalyzer<BroadcastAttributeAnalyzer>();
	[TestMethod] public Task BroadcastAttributeFix() => TestFixer<BroadcastAttributeFix>();

	[TestMethod] public Task AuthorityAttributeAnalyzer() => TestAnalyzer<AuthorityAttributeAnalyzer>();
	[TestMethod] public Task AuthorityAttributeFix() => TestFixer<AuthorityAttributeFix>();

	[TestMethod] public Task GpuBufferAnalyzer() => TestAnalyzer<GpuBufferAnalyzer>();
	[TestMethod] public Task GpuBufferFix() => TestFixer<GpuBufferFix>();

	[TestMethod] public Task ConCmdAttributeAnalyzer() => TestAnalyzer<ConCmdAnalyzer>();
	[TestMethod] public Task ConCmdAttributeFix() => TestFixer<ConCmdAttributeFix>();

	[TestMethod] public Task ConVarAttributeAnalyzer() => TestAnalyzer<ConVarAnalyzer>();
	[TestMethod] public Task ConVarAttributeFix() => TestFixer<ConVarAttributeFix>();

	[TestMethod] public Task HotloadUnsupportedAnalyzer() => TestAnalyzer<HotloadUnsupportedAnalyzer>();
	[TestMethod] public Task HotloadUnsupportedFix() => TestFixer<HotloadUnsupportedFixer>();

	[TestMethod] public Task SyncQueryAnalyzer() => TestAnalyzer<SyncQueryAnalyzer>();
	[TestMethod] public Task SyncQueryFix() => TestFixer<SyncQueryFix>();

	[TestMethod] public Task HostSyncAttributeAnalyzer() => TestAnalyzer<HostSyncAttributeAnalyzer>();
	[TestMethod] public Task HostSyncAttributeFix() => TestFixer<HostSyncAttributeFix>();
}
