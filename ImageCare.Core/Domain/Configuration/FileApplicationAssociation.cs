using System.Text.Json.Serialization;

namespace ImageCare.Core.Domain.Configuration;

public sealed class FileApplicationAssociation
{

	public string Name { get; set; } = string.Empty;


	public string FileExtension { get; set; } = string.Empty;


	public string ApplicationPath { get; set; } = string.Empty;
}