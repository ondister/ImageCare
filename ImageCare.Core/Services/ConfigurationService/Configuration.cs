using System.Text.Json.Serialization;

namespace ImageCare.Core.Services.ConfigurationService;

public sealed class Configuration
{
    [JsonInclude]
    public string LastSourceDirectoryPath { get; set; } = string.Empty;

    [JsonInclude]
    public string LastTargetDirectoryPath { get; set; } = string.Empty;
}