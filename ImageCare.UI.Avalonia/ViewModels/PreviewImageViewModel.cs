﻿using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.Messaging;

using ImageCare.Core.Domain;
using ImageCare.Core.Services;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.UI.Avalonia.Behaviors;
using ImageCare.UI.Avalonia.Collections;
using ImageCare.UI.Avalonia.Messages;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Regions;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class PreviewImageViewModel : ViewModelBase, IRecipient<FolderSelectedMessage>, IRecipient<ImagePreviewSelectedMessage>
{
    private readonly IFileSystemImageService _imageService;
    private readonly IFolderService _folderService;
    private readonly IFileSystemWatcherService _fileSystemWatcherService;
    private readonly IFileOperationsService _fileOperationsService;
    private readonly SynchronizationContext _synchronizationContext;
    private ImagePreviewViewModel? _selectedPreview;
    private CompositeDisposable fileSystemWatcherCompositeDisposable;

    public PreviewImageViewModel(IFileSystemImageService imageService,
                                 IFolderService folderService,
                                 IFileSystemWatcherService fileSystemWatcherService,
                                 IFileOperationsService fileOperationsService,
                                 ImagePreviewDropHandler imagePreviewDropHandler,
                                 SynchronizationContext synchronizationContext)
    {
        _imageService = imageService;
        _folderService = folderService;
        _fileSystemWatcherService = fileSystemWatcherService;
        _fileOperationsService = fileOperationsService;
        _synchronizationContext = synchronizationContext;

        WeakReferenceMessenger.Default.Register<FolderSelectedMessage>(this);
        WeakReferenceMessenger.Default.Register<ImagePreviewSelectedMessage>(this);

        ImagePreviews = [];
        ImagePreviewDropHandler = imagePreviewDropHandler;
    }

    public SortedObservableCollection<ImagePreviewViewModel> ImagePreviews { get; }

    public ImagePreviewDropHandler ImagePreviewDropHandler { get; }

    public string Mode { get; private set; }

    public ImagePreviewViewModel? SelectedPreview
    {
        get => _selectedPreview;
        set
        {
            if (_selectedPreview != null)
            {
                _selectedPreview.Selected = false;
            }

            SetProperty(ref _selectedPreview, value);
            if (_selectedPreview != null)
            {
                _selectedPreview.Selected = true;
                WeakReferenceMessenger.Default.Send(new ImagePreviewSelectedMessage(new ImagePreview(_selectedPreview.Title, _selectedPreview.Url, _selectedPreview.MediaFormat), Mode));
            }
        }
    }

    public string SelectedFolderPath { get; set; }

    public void Receive(FolderSelectedMessage message)
    {
        if (message.Mode == Mode)
        {
            LoadImagePreviewsAsync(message.Value);

            SelectedFolderPath = message.Value.Path;

            if (!string.IsNullOrWhiteSpace(SelectedFolderPath))
            {
                _fileSystemWatcherService.SetWatchingDirectory(SelectedFolderPath);
            }
        }
    }

    /// <inheritdoc />
    public void Receive(ImagePreviewSelectedMessage message)
    {
        if (message.Mode != Mode)
        {
            SelectedPreview = null;
        }
    }

    /// <inheritdoc />
    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
        Mode = (string)navigationContext.Parameters["mode"];

        fileSystemWatcherCompositeDisposable = new CompositeDisposable
        {
            _fileSystemWatcherService.FileCreated.DistinctUntilChanged(model => model.FullName).Subscribe(OnFileCreated),
            _fileSystemWatcherService.FileDeleted.DistinctUntilChanged(model => model.FullName).Subscribe(OnFileDeleted),
            _fileSystemWatcherService.FileRenamed.DistinctUntilChanged(model => model.NewFileModel.FullName).Subscribe(OnFileRenamed)
        };
    }

    /// <inheritdoc />
    public override void OnNavigatedFrom(NavigationContext navigationContext)
    {
        fileSystemWatcherCompositeDisposable.Dispose();
    }

    private async Task LoadImagePreviewsAsync(DirectoryModel directoryModel)
    {
        try
        {
            ImagePreviews.Clear();

            var files = await _folderService.GetFileModelAsync(directoryModel, "*");
            await foreach (var previewImage in _imageService.GetImagePreviewsAsync(files))
            {
                ImagePreviews.Add(new ImagePreviewViewModel(previewImage, _imageService, _fileOperationsService));
            }
        }
        catch (Exception exception) { }
    }

    private void OnFileCreated(FileModel fileModel)
    {
        CreateImagePreviewFromPath(fileModel.FullName);
    }

    private void OnFileDeleted(FileModel fileModel)
    {
        RemoveImagePreviewByPath(fileModel.FullName);
    }

    private void OnFileRenamed(FileRenamedModel model)
    {
        RemoveImagePreviewByPath(model.OldFileModel.FullName);

        CreateImagePreviewFromPath(model.NewFileModel.FullName);
    }

    private void CreateImagePreviewFromPath(string filePath)
    {
        _imageService.GetImagePreviewAsync(filePath)
                     .ContinueWith(
                         task =>
                         {
                             if (task.Result == null)
                             {
                                 return;
                             }

                             ImagePreviews.InsertItem(new ImagePreviewViewModel(task.Result, _imageService, _fileOperationsService));
                         });
    }

    private void RemoveImagePreviewByPath(string filePath)
    {
        var imagePreviewViewModel = ImagePreviews.FirstOrDefault(vm => vm.Url.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        if (imagePreviewViewModel != null)
        {
            var indexToRemove = ImagePreviews.IndexOf(imagePreviewViewModel);
            ImagePreviews.Remove(imagePreviewViewModel);

            if (ImagePreviews.Count > indexToRemove)
            {
                _synchronizationContext.Post(d => { SelectedPreview = ImagePreviews[indexToRemove]; },null);
            }

            if (ImagePreviews.Count == 0)
            {
                WeakReferenceMessenger.Default.Send(new ImagePreviewSelectedMessage(ImagePreview.Empty, Mode));
                _synchronizationContext.Post(d => { SelectedPreview = null; }, null);
            }
        }
    }
}