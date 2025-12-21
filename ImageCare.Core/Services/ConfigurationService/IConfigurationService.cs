using ImageCare.Core.Domain.Configuration;

namespace ImageCare.Core.Services.ConfigurationService;

public interface IConfigurationService
{
	IObservable<Configuration> ConfigurationSaved { get; }

	Lazy<Configuration> Configuration { get; }

	void SaveConfiguration();
}