﻿namespace ImageCare.Core.Domain;

public sealed class FileModel
{
    public FileModel(string? name, string fullName)
    {
        Name = name;
        FullName = fullName;
    }

    public string? Name { get; }

    public string FullName { get; }
}