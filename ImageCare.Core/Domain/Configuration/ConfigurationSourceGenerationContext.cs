
using System.Text.Json.Serialization;

namespace ImageCare.Core.Domain.Configuration
{
    [JsonSourceGenerationOptions(AllowTrailingCommas = true, WriteIndented = true)]
    [JsonSerializable(typeof(Configuration))]
    [JsonSerializable(typeof(FileApplicationAssociation))]
    [JsonSerializable(typeof(List<FileApplicationAssociation>))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(string))]

    internal partial class ConfigurationSourceGenerationContext : JsonSerializerContext
    {
    }
}
