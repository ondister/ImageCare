using System.Text.Json.Serialization;

namespace ImageCare.Core.Services.ConfigurationService;

public sealed class FileApplicationAssociation
{
    [JsonInclude]
    public string Name { get; set; } = string.Empty;

    [JsonInclude]
    public string FileExtension { get; set; } = string.Empty;

    [JsonInclude]
    public string ApplicationPath { get; set; } = string.Empty;
}