namespace ImageCare.Core.Services.ConfigurationService;

public interface IConfigurationService
{
    Lazy<Configuration> Configuration { get; }

    void SaveConfiguration();
}