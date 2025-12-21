using System.Reactive.Linq;
using System.Reactive.Subjects;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FileAssociationsService;

using Moq;

namespace ImageCare.Core.Tests.Services.FileAssociationsService;

[TestFixture]
public class ConfigurationFileAssociationsServiceTests
{
	[Test]
	public void GetAssociations_WithMatchingExtensions_ReturnsAssociatedApplications()
	{
		var configuration = CreateConfigurationWithAssociations();
		var configServiceMock = CreateConfigurationServiceMock(configuration);

		using var service = new ConfigurationFileAssociationsService(configServiceMock.Object);

		var associations = service.GetAssociations(MediaFormat.MediaFormatJpg).ToList();

		Assert.That(associations, Has.Count.EqualTo(2));
		Assert.That(associations[0].Name, Is.EqualTo("Photoshop"));
		Assert.That(associations[1].Name, Is.EqualTo("Paint.NET"));
	}

	[Test]
	public void GetAssociations_WithNoMatchingExtensions_ReturnsEmptyCollection()
	{
		var configuration = CreateConfigurationWithAssociations();
		var configServiceMock = CreateConfigurationServiceMock(configuration);

		using var service = new ConfigurationFileAssociationsService(configServiceMock.Object);

		var associations = service.GetAssociations(MediaFormat.MediaFormatUnknown);

		Assert.That(associations, Is.Empty);
	}

	[Test]
	public void GetAssociations_WithDuplicateApplications_ReturnsDistinctApplications()
	{
		var configuration = new Configuration
		{
			ApplicationAssociationPairs = new List<FileApplicationAssociation>
			{
				new() { Name = "SameApp", FileExtension = ".jpg", ApplicationPath = "C:\\App.exe" },
				new() { Name = "SameApp", FileExtension = ".jpeg", ApplicationPath = "C:\\App.exe" },
				new() { Name = "SameApp", FileExtension = ".jpg", ApplicationPath = "C:\\App.exe" }
			}
		};
		var configServiceMock = CreateConfigurationServiceMock(configuration);

		using var service = new ConfigurationFileAssociationsService(configServiceMock.Object);

		var associations = service.GetAssociations(MediaFormat.MediaFormatJpg).ToList();

		Assert.That(associations, Has.Count.EqualTo(1));
		Assert.That(associations[0].Name, Is.EqualTo("SameApp"));
	}

	[Test]
	public void GetAssociations_WithMultipleMediaFormats_ReturnsFormatSpecificAssociations()
	{
		var configuration = CreateConfigurationWithMultipleFormats();
		var configServiceMock = CreateConfigurationServiceMock(configuration);

		using var service = new ConfigurationFileAssociationsService(configServiceMock.Object);

		var jpgAssociations = service.GetAssociations(MediaFormat.MediaFormatJpg).ToList();
		var arwAssociations = service.GetAssociations(MediaFormat.MediaFormatArw).ToList();

		Assert.That(jpgAssociations, Has.Count.EqualTo(2));
		Assert.That(arwAssociations, Has.Count.EqualTo(1));
		Assert.That(arwAssociations[0].Name, Is.EqualTo("Sony Viewer"));
	}

	[Test]
	public void GetAssociations_AfterConfigurationSave_ClearsCacheAndReloadsAssociations()
	{
		var initialConfig = CreateConfigurationWithAssociations();
		var updatedConfig = CreateUpdatedConfiguration();

		var configSavedSubject = new Subject<Configuration>();

		var configServiceMock = new Mock<IConfigurationService>();
		configServiceMock.Setup(x => x.ConfigurationSaved).Returns(configSavedSubject);

		// First setup returns initial config
		configServiceMock.Setup(x => x.Configuration).Returns(new Lazy<Configuration>(() => initialConfig));

		using var service = new ConfigurationFileAssociationsService(configServiceMock.Object);

		var firstCall = service.GetAssociations(MediaFormat.MediaFormatJpg).ToList();

		// Re-setup to return updated config
		configServiceMock.Setup(x => x.Configuration).Returns(new Lazy<Configuration>(() => updatedConfig));
		configSavedSubject.OnNext(updatedConfig);

		var secondCall = service.GetAssociations(MediaFormat.MediaFormatJpg).ToList();

		Assert.That(firstCall, Has.Count.EqualTo(2));
		Assert.That(secondCall, Has.Count.EqualTo(1));
		Assert.That(secondCall[0].Name, Is.EqualTo("Updated App"));
	}

	[Test]
	public void GetAssociations_WithEmptyConfiguration_ReturnsEmptyCollection()
	{
		var emptyConfig = new Configuration { ApplicationAssociationPairs = new List<FileApplicationAssociation>() };
		var configServiceMock = CreateConfigurationServiceMock(emptyConfig);

		using var service = new ConfigurationFileAssociationsService(configServiceMock.Object);

		var associations = service.GetAssociations(MediaFormat.MediaFormatJpg);

		Assert.That(associations, Is.Empty);
	}

	[Test]
	public void GetAssociations_WithCaseInsensitiveExtensions_MatchesCorrectly()
	{
		var configuration = new Configuration
		{
			ApplicationAssociationPairs = new List<FileApplicationAssociation>
			{
				new() { Name = "App1", FileExtension = ".JPG", ApplicationPath = "C:\\App1.exe" },
				new() { Name = "App2", FileExtension = ".jpeg", ApplicationPath = "C:\\App2.exe" },
				new() { Name = "App3", FileExtension = ".JpEg", ApplicationPath = "C:\\App3.exe" }
			}
		};
		var configServiceMock = CreateConfigurationServiceMock(configuration);

		using var service = new ConfigurationFileAssociationsService(configServiceMock.Object);

		var associations = service.GetAssociations(MediaFormat.MediaFormatJpg).ToList();

		Assert.That(associations, Has.Count.EqualTo(3));
	}

	[Test]
	public void GetAssociations_AfterDispose_ThrowsObjectDisposedException()
	{
		var configServiceMock = CreateConfigurationServiceMock(new Configuration());
		var service = new ConfigurationFileAssociationsService(configServiceMock.Object);

		service.Dispose();

		Assert.Throws<ObjectDisposedException>(() =>
			                                       service.GetAssociations(MediaFormat.MediaFormatJpg));
	}

	[Test]
	public void MultipleDisposeCalls_DoNotThrowExceptions()
	{
		var configServiceMock = CreateConfigurationServiceMock(new Configuration());
		var service = new ConfigurationFileAssociationsService(configServiceMock.Object);

		Assert.DoesNotThrow(() =>
		{
			service.Dispose();
			service.Dispose();
		});
	}

	[Test]
	public void GetAssociations_CachesResultsForSameMediaFormat()
	{
		var configuration = CreateConfigurationWithAssociations();
		var configServiceMock = CreateConfigurationServiceMock(configuration);

		using var service = new ConfigurationFileAssociationsService(configServiceMock.Object);

		var firstCall = service.GetAssociations(MediaFormat.MediaFormatJpg);
		var secondCall = service.GetAssociations(MediaFormat.MediaFormatJpg);

		Assert.That(secondCall, Is.SameAs(firstCall));
	}

	private static Configuration CreateConfigurationWithAssociations()
	{
		return new Configuration
		{
			ApplicationAssociationPairs = new List<FileApplicationAssociation>
			{
				new() { Name = "Photoshop", FileExtension = ".jpg", ApplicationPath = "C:\\Photoshop.exe" },
				new() { Name = "Paint.NET", FileExtension = ".jpeg", ApplicationPath = "C:\\Paint.NET.exe" },
				new() { Name = "Video Editor", FileExtension = ".mp4", ApplicationPath = "C:\\VideoEditor.exe" }
			}
		};
	}

	private static Configuration CreateConfigurationWithMultipleFormats()
	{
		return new Configuration
		{
			ApplicationAssociationPairs = new List<FileApplicationAssociation>
			{
				new() { Name = "Photoshop", FileExtension = ".jpg", ApplicationPath = "C:\\Photoshop.exe" },
				new() { Name = "Paint.NET", FileExtension = ".jpeg", ApplicationPath = "C:\\Paint.NET.exe" },
				new() { Name = "Sony Viewer", FileExtension = ".arw", ApplicationPath = "C:\\SonyViewer.exe" }
			}
		};
	}

	private static Configuration CreateUpdatedConfiguration()
	{
		return new Configuration
		{
			ApplicationAssociationPairs = new List<FileApplicationAssociation>
			{
				new() { Name = "Updated App", FileExtension = ".jpg", ApplicationPath = "C:\\UpdatedApp.exe" }
			}
		};
	}

	private static Mock<IConfigurationService> CreateConfigurationServiceMock(Configuration configuration)
	{
		var configServiceMock = new Mock<IConfigurationService>();
		configServiceMock.Setup(x => x.ConfigurationSaved).Returns(Observable.Never<Configuration>());
		configServiceMock.Setup(x => x.Configuration).Returns(new Lazy<Configuration>(() => configuration));
		return configServiceMock;
	}
}