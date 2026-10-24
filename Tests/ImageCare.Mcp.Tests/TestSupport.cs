using System;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;

using ImageCare.Mcp;

using Moq;

using Serilog;

namespace ImageCare.Mcp.Tests;

/// <summary>
/// Builds a disposable temp folder and an <see cref="ImageCareMcpServices"/> instance whose core
/// services are inert mocks. The smoke tests only exercise protocol plumbing, so no real media
/// pipeline has to run.
/// </summary>
internal static class TestSupport
{
    public static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "ImageCare.Mcp.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup: a leftover temp folder must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }

    public static string CreateMediaFile(string directory, string name, DateTime timestampUtc, int padding = 0)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[16 + padding]);
        File.SetCreationTimeUtc(path, timestampUtc);
        File.SetLastWriteTimeUtc(path, timestampUtc);
        return path;
    }

    public static ImageCareMcpServices CreateServices(
        Configuration configuration,
        string dataDirectory,
        Ui.IMcpUiHost? uiHost = null)
    {
        _ = dataDirectory;

        return new ImageCareMcpServices
        {
            FileSystemService = Mock.Of<IFileSystemService>(),
            MediaPreviewService = Mock.Of<IMediaPreviewService>(),
            AnnotationsService = Mock.Of<IMediaAnnotationsService>(),
            PreviewOperations = Mock.Of<IMediaPreviewOperationsService>(),
            FolderTagApplicationService = Mock.Of<IFolderTagApplicationService>(),
            FolderTagsStore = Mock.Of<IFolderTagsStore>(),
            ConfigurationService = CreateConfigurationService(configuration),
            Logger = new LoggerConfiguration().CreateLogger(),
            UiHost = uiHost
        };
    }

    public static IConfigurationService CreateConfigurationService(Configuration configuration)
    {
        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .SetupGet(service => service.Configuration)
            .Returns(new Lazy<Configuration>(() => configuration));
        return configurationService.Object;
    }
}
