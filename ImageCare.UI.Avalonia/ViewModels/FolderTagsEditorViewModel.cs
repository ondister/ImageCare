using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Annotations;
using ImageCare.UI.Avalonia.Localization;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

/// <summary>
/// Edits the <c>folder-tags.txt</c> descriptor of a single folder. Tags inherited from parent
/// folders are shown for context but are never written to the selected folder's descriptor.
/// </summary>
public sealed class FolderTagsEditorViewModel : ViewModelBase
{
    private readonly IFolderTagsStore _folderTagsStore;
    private readonly LocalizationManager _localization;
    private readonly ILogger _logger;

    private string _newTag = string.Empty;
    private string _status = string.Empty;
    private bool _isDirty;
    private bool _isBusy;
    private bool _isAvailable = true;

    public FolderTagsEditorViewModel(
        string folderPath,
        IFolderTagsStore folderTagsStore,
        LocalizationManager localization,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        _folderTagsStore = folderTagsStore ?? throw new ArgumentNullException(nameof(folderTagsStore));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        FolderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        var folderName = Path.GetFileName(FolderPath);
        FolderName = string.IsNullOrEmpty(folderName) ? FolderPath : folderName;

        try
        {
            DescriptorPath = _folderTagsStore.GetDescriptorPath(FolderPath);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            DescriptorPath = string.Empty;
        }

        Tags = new ObservableCollection<string>();
        InheritedTags = new ObservableCollection<string>();
        Tags.CollectionChanged += OnTagsCollectionChanged;
        InheritedTags.CollectionChanged += OnInheritedTagsCollectionChanged;

        AddTagCommand = CreateCommand(AddTag, CanAddTag)
            .ObservesProperty(() => NewTag)
            .ObservesProperty(() => IsBusy)
            .ObservesProperty(() => IsAvailable);
        RemoveTagCommand = CreateCommand<string>(RemoveTag, CanRemoveTag)
            .ObservesProperty(() => IsBusy)
            .ObservesProperty(() => IsAvailable);
        SaveCommand = CreateAsyncCommand(SaveAsync, CanSave)
            .ObservesProperty(() => IsDirty)
            .ObservesProperty(() => IsBusy)
            .ObservesProperty(() => IsAvailable);
        CloseCommand = CreateCommand(RequestClose);
    }

    public string FolderPath { get; }

    public string FolderName { get; }

    public string DescriptorPath { get; }

    public string Title => _localization.Get("FolderTags_Editor_Title", FolderName);

    public ObservableCollection<string> Tags { get; }

    public ObservableCollection<string> InheritedTags { get; }

    public ICommand AddTagCommand { get; }

    public ICommand RemoveTagCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand CloseCommand { get; }

    public event EventHandler? CloseRequested;

    public string NewTag
    {
        get => _newTag;
        set => SetProperty(ref _newTag, value ?? string.Empty);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set => SetProperty(ref _isAvailable, value);
    }

    public bool HasOwnTags => Tags.Count > 0;

    public bool HasInheritedTags => InheritedTags.Count > 0;

    /// <summary>
    /// Reads the current descriptor and the inherited tag set. The folder must already exist.
    /// </summary>
    public void Load()
    {
        try
        {
            IsAvailable = Directory.Exists(FolderPath);
            ReplaceTags(Tags, _folderTagsStore.Read(FolderPath) ?? []);
            ReplaceTags(InheritedTags, ResolveInheritedTags());

            IsDirty = false;
            Status = IsAvailable ? string.Empty : _localization["FolderTags_Editor_Unavailable"];
        }
        catch (Exception exception)
        {
            _isDirty = false;
            _logger.Error(exception, "Failed to load folder tags for {Path}", FolderPath);
            Status = _localization.Get("FolderTags_Editor_LoadFailed", exception.Message);
        }
        finally
        {
            RaisePropertyChanged(nameof(IsDirty));
            RaisePropertyChanged(nameof(HasOwnTags));
            RaisePropertyChanged(nameof(HasInheritedTags));
        }
    }

    public void AddTag()
    {
        if (!IsAvailable || IsBusy)
        {
            return;
        }

        var normalized = MediaAnnotations.NormalizeTag(NewTag);
        if (normalized.Length == 0)
        {
            Status = _localization["FolderTags_Editor_EmptyTag"];
            return;
        }

        if (normalized[0] == '#')
        {
            Status = _localization["FolderTags_Editor_CommentTag"];
            return;
        }

        if (Tags.Any(tag => string.Equals(tag, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            Status = _localization["FolderTags_Editor_TagExists"];
            return;
        }

        if (InheritedTags.Any(tag => string.Equals(tag, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            Status = _localization["FolderTags_Editor_TagInherited"];
            return;
        }

        Tags.Add(normalized);
        NewTag = string.Empty;
        MarkDirty();
    }

    public void RemoveTag(string tag)
    {
        if (!IsAvailable || IsBusy || string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        var existing = Tags.FirstOrDefault(item => string.Equals(item, tag, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            return;
        }

        Tags.Remove(existing);
        MarkDirty();
    }

    public async Task SaveAsync()
    {
        if (!IsDirty || IsBusy || !IsAvailable)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (!Directory.Exists(FolderPath))
            {
                IsAvailable = false;
                Status = _localization["FolderTags_Editor_Unavailable"];
                return;
            }

            await _folderTagsStore.WriteAsync(FolderPath, Tags.ToArray(), CancellationToken.None);
            IsDirty = false;
            Status = _localization["FolderTags_Editor_Saved"];
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save folder tags for {Path}", FolderPath);
            Status = _localization.Get("FolderTags_Editor_SaveFailed", exception.Message);
        }
        finally
        {
            IsBusy = false;
            RaisePropertyChanged(nameof(IsDirty));
        }
    }

    private bool CanAddTag()
    {
        return IsAvailable && !IsBusy && !string.IsNullOrWhiteSpace(NewTag);
    }

    private bool CanRemoveTag(string tag)
    {
        return IsAvailable && !IsBusy && !string.IsNullOrWhiteSpace(tag);
    }

    private bool CanSave()
    {
        return IsAvailable && !IsBusy && IsDirty;
    }

    private void MarkDirty()
    {
        IsDirty = true;
        Status = _localization["FolderTags_Editor_Unsaved"];
    }

    private IReadOnlyList<string> ResolveInheritedTags()
    {
        var parentPath = Path.GetDirectoryName(FolderPath);
        if (string.IsNullOrEmpty(parentPath))
        {
            return [];
        }

        return FolderTagsInheritance.Resolve(parentPath, _folderTagsStore.Read);
    }

    private void ReplaceTags(ObservableCollection<string> target, IEnumerable<string> tags)
    {
        target.Clear();
        foreach (var tag in tags)
        {
            target.Add(tag);
        }
    }

    private void RequestClose()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnTagsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(HasOwnTags));
    }

    private void OnInheritedTagsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(HasInheritedTags));
    }
}
