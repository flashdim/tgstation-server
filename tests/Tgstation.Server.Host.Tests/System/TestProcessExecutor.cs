using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

using Tgstation.Server.Host.IO;

namespace Tgstation.Server.Host.System.Tests
{
	[TestClass]
	public sealed class TestProcessExecutor
	{
		[TestMethod]
		public async Task TestLaunchProcessOverridesInheritedEnvironment()
		{
			const string ExpectedPath = "process-executor-test-path";
			Assert.IsNotNull(Environment.GetEnvironmentVariable("PATH"));
			var processFeatures = new Mock<IProcessFeatures>();
			processFeatures
				.Setup(x => x.HandleProcessStart(It.IsAny<global::System.Diagnostics.Process>(), It.IsAny<CancellationToken>()))
				.Returns<global::System.Diagnostics.Process, CancellationToken>((process, _) => ValueTask.FromResult(process.Id));
			using var loggerFactory = LoggerFactory.Create(_ => { });
			var processExecutor = new ProcessExecutor(
				processFeatures.Object,
				Mock.Of<IIOManager>(),
				Mock.Of<ILogger<ProcessExecutor>>(),
				loggerFactory);

			var isWindows = OperatingSystem.IsWindows();
			await using var process = await processExecutor.LaunchProcess(
				isWindows ? "cmd.exe" : "/usr/bin/env",
				Environment.CurrentDirectory,
				isWindows ? "/c echo %PATH%" : String.Empty,
				CancellationToken.None,
				new Dictionary<string, string> { ["PATH"] = ExpectedPath },
				null,
				true,
				true);

			Assert.AreEqual<int?>(0, await process.Lifetime);
			StringAssert.Contains(await process.GetCombinedOutput(CancellationToken.None), ExpectedPath);
		}
	}
}
