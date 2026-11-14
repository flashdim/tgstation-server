using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

using Tgstation.Server.Api.Models;
using Tgstation.Server.Host.Components.Repository;
using Tgstation.Server.Host.Configuration;
using Tgstation.Server.Host.IO;
using Tgstation.Server.Host.Jobs;
using Tgstation.Server.Host.System;
using Tgstation.Server.Host.Utils;

namespace Tgstation.Server.Host.Components.Engine.Tests
{
	[TestClass]
	public sealed class TestOpenDreamInstaller
	{
		[TestMethod]
		public async Task TestDownloadsUseSameRepositoryIfItExists()
		{
			await RepoDownloadTest(false);
		}

		[TestMethod]
		public async Task TestDownloadsCloneRepositoryIfItDoesntExists()
		{
			await RepoDownloadTest(true);
		}

		[TestMethod]
		[DataRow(false)]
		[DataRow(true)]
		public async Task TestBuildPassesWarningPolicyToNestedProcesses(bool suppressOutput)
		{
			var ioManager = new Mock<IIOManager>();
			ioManager.Setup(x => x.ConcatPath(It.IsNotNull<string[]>())).Returns<string[]>(Path.Combine);
			ioManager.Setup(x => x.DirectoryExists(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			ioManager.Setup(x => x.FileExists(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var process = new Mock<IProcess>();
			process.SetupGet(x => x.Lifetime).Returns(Task.FromResult<int?>(1));
			process.Setup(x => x.GetCombinedOutput(It.IsAny<CancellationToken>())).ReturnsAsync(String.Empty);
			var executor = new Mock<IProcessExecutor>();
			var existingWarnings = Environment.GetEnvironmentVariable("WarningsNotAsErrors");
			var expectedWarnings = String.IsNullOrEmpty(existingWarnings) ? "NU1508" : $"{existingWarnings};NU1508";
			executor.Setup(x => x.LaunchProcess(
				It.IsAny<string>(),
				It.IsAny<string>(),
				It.Is<string>(args => args.Contains("run -c Release --project OpenDreamPackageTool")),
				It.IsAny<CancellationToken>(),
				It.Is<IReadOnlyDictionary<string, string>>(env => env["WarningsNotAsErrors"] == expectedWarnings),
				null,
				!suppressOutput,
				true,
				false)).ReturnsAsync(process.Object).Verifiable();

			var generalOptions = new Mock<IOptionsMonitor<GeneralConfiguration>>();
			generalOptions.SetupGet(x => x.CurrentValue).Returns(new GeneralConfiguration { OpenDreamSuppressInstallOutput = suppressOutput });
			var installer = new OpenDreamInstaller(
				ioManager.Object,
				Mock.Of<ILogger<OpenDreamInstaller>>(),
				Mock.Of<IPlatformIdentifier>(),
				executor.Object,
				Mock.Of<IRepositoryManager>(),
				Mock.Of<IAsyncDelayer>(),
				Mock.Of<IHttpClientFactory>(),
				generalOptions.Object,
				Mock.Of<IOptionsMonitor<SessionConfiguration>>());

			await Assert.ThrowsExactlyAsync<JobException>(() => installer.Install(
				new EngineVersion { Engine = EngineType.OpenDream },
				"install",
				false,
				CancellationToken.None).AsTask());
			executor.Verify();
		}

		static async Task RepoDownloadTest(bool needsClone)
		{
			var mockGeneralConfigOptions = new Mock<IOptionsMonitor<GeneralConfiguration>>();
			var generalConfig = new GeneralConfiguration();
			var mockSessionConfigOptions = new Mock<IOptionsMonitor<SessionConfiguration>>();
			var sessionConfig = new SessionConfiguration();
			Assert.IsNotNull(generalConfig.OpenDreamGitUrl);
			mockGeneralConfigOptions.SetupGet(x => x.CurrentValue).Returns(generalConfig);
			mockSessionConfigOptions.SetupGet(x => x.CurrentValue).Returns(sessionConfig);

			var cloneAttempts = 0;
			var mockRepository = new Mock<IRepository>();
			mockRepository.Setup(x => x.CommittishIsParent(It.IsNotNull<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var mockRepositoryManager = new Mock<IRepositoryManager>();
			mockRepositoryManager.Setup(x => x.CloneRepository(
				generalConfig.OpenDreamGitUrl,
				null,
				null,
				null,
				It.IsNotNull<JobProgressReporter>(),
				true,
				It.IsAny<CancellationToken>()))
				.Callback(() => ++cloneAttempts)
				.ReturnsAsync(needsClone ? mockRepository.Object : null)
				.Verifiable(Times.Exactly(1));

			mockRepositoryManager.Setup(x => x.LoadRepository(
				It.IsAny<CancellationToken>()))
				.Returns(() =>
				{
					Assert.AreEqual(1, cloneAttempts);
					return ValueTask.FromResult(mockRepository.Object);
				})
				.Verifiable(Times.Exactly(needsClone ? 0 : 1));

			var installer = new OpenDreamInstaller(
				Mock.Of<IIOManager>(),
				Mock.Of<ILogger<OpenDreamInstaller>>(),
				Mock.Of<IPlatformIdentifier>(),
				Mock.Of<IProcessExecutor>(),
				mockRepositoryManager.Object,
				Mock.Of<IAsyncDelayer>(),
				Mock.Of<IHttpClientFactory>(),
				mockGeneralConfigOptions.Object,
				mockSessionConfigOptions.Object);

			var data = await installer.DownloadVersion(
				new EngineVersion
				{
					Engine = EngineType.OpenDream,
					SourceSHA = new string('a', Limits.MaximumCommitShaLength),
				},
				new JobProgressReporter(),
				CancellationToken.None);


			Assert.IsNotNull(data);

			mockRepositoryManager.VerifyAll();
		}
	}
}
