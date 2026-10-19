
using System.Text.Json.Serialization;

using ImageCare.Core.Services.MediaPreviewOperationsService;

namespace ImageCare.Core.Domain.Configuration
{
    [JsonSourceGenerationOptions(AllowTrailingCommas = true, WriteIndented = true)]
    [JsonSerializable(typeof(Configuration))]
    [JsonSerializable(typeof(FileApplicationAssociation))]
    [JsonSerializable(typeof(List<FileApplicationAssociation>))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(List<DeletedMediaFile>))]
    [JsonSerializable(typeof(string))]

    internal partial class ConfigurationSourceGenerationContext : JsonSerializerContext
    {
    }
}
