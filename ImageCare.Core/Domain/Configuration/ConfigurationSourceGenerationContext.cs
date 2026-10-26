
using System.Text.Json.Serialization;

using ImageCare.Core.Services.MediaPreviewOperationsService;

namespace ImageCare.Core.Domain.Configuration
{
    [JsonSourceGenerationOptions(AllowTrailingCommas = true, WriteIndented = true)]
    [JsonSerializable(typeof(Configuration))]
    [JsonSerializable(typeof(MediaSortDirection))]
    [JsonSerializable(typeof(FileApplicationAssociation))]
    [JsonSerializable(typeof(List<FileApplicationAssociation>))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(List<DeletedMediaFile>))]
    [JsonSerializable(typeof(List<DateTimeOffset>))]
    [JsonSerializable(typeof(AdaptiveHelpProgressEntry))]
    [JsonSerializable(typeof(List<AdaptiveHelpProgressEntry>))]
    [JsonSerializable(typeof(AdaptiveHelpDetailLevel))]
    [JsonSerializable(typeof(AdaptiveHelpUserGoal))]
    [JsonSerializable(typeof(AdaptiveHelpMasteryLevel))]
    [JsonSerializable(typeof(string))]

    internal partial class ConfigurationSourceGenerationContext : JsonSerializerContext
    {
    }
}
