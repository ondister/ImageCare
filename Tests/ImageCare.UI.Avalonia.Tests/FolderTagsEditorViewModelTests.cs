using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ImageCare.Core.Domain.Annotations;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.ViewModels;

using Serilog;

namespace ImageCare.UI.Avalonia.Tests;

[TestFixture]
public class FolderTagsEditorViewModelTests
{
    private string _root = null!;
    private string _child = null!;
    private FakeFolderTagsStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "ImageCareFolderTagsEditor", Guid.NewGuid().ToString("N"));
        _child = Path.Combine(_root, "Child");
        Directory.CreateDirectory(_child);
        _store = new FakeFolderTagsStore();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void Load_ReadsOwnAndInheritedTags()
    {
        _store.Descriptors[_root] = ["parent tag"];
        _store.Descriptors[_child] = ["own tag"];
        var viewModel = CreateViewModel();

        viewModel.Load();

        Assert.That(viewModel.Tags, Is.EqualTo(new[] { "own tag" }));
        Assert.That(viewModel.InheritedTags, Is.EqualTo(new[] { "parent tag" }));
        Assert.That(viewModel.HasOwnTags, Is.True);
        Assert.That(viewModel.HasInheritedTags, Is.True);
        Assert.That(viewModel.IsDirty, Is.False);
        Assert.That(viewModel.IsAvailable, Is.True);
        Assert.That(viewModel.Title, Does.Contain("Child"));
    }

    [Test]
    public void AddTag_NormalizesInput_AndRejectsDuplicatesAndComments()
    {
        _store.Descriptors[_root] = ["inherited"];
        var viewModel = CreateViewModel();
        viewModel.Load();

        viewModel.NewTag = "  New   tag ";
        viewModel.AddTagCommand.Execute(null);

        Assert.That(viewModel.Tags, Is.EqualTo(new[] { "New tag" }));
        Assert.That(viewModel.NewTag, Is.Empty);
        Assert.That(viewModel.IsDirty, Is.True);

        viewModel.NewTag = "new TAG";
        viewModel.AddTagCommand.Execute(null);
        Assert.That(viewModel.Tags, Has.Count.EqualTo(1));
        Assert.That(viewModel.Status, Is.EqualTo(LocalizationManager.Instance["FolderTags_Editor_TagExists"]));

        viewModel.NewTag = "inherited";
        viewModel.AddTagCommand.Execute(null);
        Assert.That(viewModel.Tags, Has.Count.EqualTo(1));
        Assert.That(viewModel.Status, Is.EqualTo(LocalizationManager.Instance["FolderTags_Editor_TagInherited"]));

        viewModel.NewTag = "#comment";
        viewModel.AddTagCommand.Execute(null);
        Assert.That(viewModel.Tags, Has.Count.EqualTo(1));
        Assert.That(viewModel.Status, Is.EqualTo(LocalizationManager.Instance["FolderTags_Editor_CommentTag"]));
    }

    [Test]
    public async Task RemoveTag_MarksDirty_AndSaveWritesRemainingTags()
    {
        _store.Descriptors[_child] = ["first", "second"];
        var viewModel = CreateViewModel();
        viewModel.Load();

        viewModel.RemoveTag("first");
        Assert.That(viewModel.Tags, Is.EqualTo(new[] { "second" }));
        Assert.That(viewModel.IsDirty, Is.True);

        await viewModel.SaveAsync();

        Assert.That(_store.LastWrittenPath, Is.EqualTo(_child));
        Assert.That(_store.LastWrittenTags, Is.EqualTo(new[] { "second" }));
        Assert.That(viewModel.IsDirty, Is.False);
        Assert.That(viewModel.Status, Is.EqualTo(LocalizationManager.Instance["FolderTags_Editor_Saved"]));
    }

    [Test]
    public void CloseCommand_RaisesCloseRequested()
    {
        var viewModel = CreateViewModel();
        var closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        viewModel.CloseCommand.Execute(null);

        Assert.That(closeRequested, Is.True);
    }

    private FolderTagsEditorViewModel CreateViewModel()
    {
        return new FolderTagsEditorViewModel(
            _child,
            _store,
            LocalizationManager.Instance,
            new LoggerConfiguration().CreateLogger());
    }

    private sealed class FakeFolderTagsStore : IFolderTagsStore
    {
        private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        public Dictionary<string, string[]> Descriptors { get; } = new(PathComparer);

        public string? LastWrittenPath { get; private set; }

        public string[]? LastWrittenTags { get; private set; }

        public string GetDescriptorPath(string directoryPath)
        {
            return Path.Combine(directoryPath, FolderTagsFile.FileName);
        }

        public bool HasDescriptor(string directoryPath)
        {
            return Descriptors.ContainsKey(Normalize(directoryPath));
        }

        public IReadOnlyList<string>? Read(string directoryPath)
        {
            return Descriptors.TryGetValue(Normalize(directoryPath), out var tags) ? tags : null;
        }

        public Task WriteAsync(
            string directoryPath,
            IEnumerable<string> tags,
            CancellationToken cancellationToken = default)
        {
            var normalizedPath = Normalize(directoryPath);
            var normalizedTags = tags.Select(MediaAnnotations.NormalizeTag).ToArray();
            LastWrittenPath = normalizedPath;
            LastWrittenTags = normalizedTags;

            if (normalizedTags.Length == 0)
            {
                Descriptors.Remove(normalizedPath);
            }
            else
            {
                Descriptors[normalizedPath] = normalizedTags;
            }

            return Task.CompletedTask;
        }

        private static string Normalize(string path)
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
    }
}
