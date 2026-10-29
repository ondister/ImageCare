using System;
using System.Reactive.Linq;
using System.Threading;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderClusterizationService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.FolderStatisticsService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.MediaBatchOperationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Behaviors;
using ImageCare.UI.Avalonia.Collections;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;
using ImageCare.UI.Avalonia.ViewModels.Preview;

using MapsterMapper;

using Prism.Dialogs;
using Prism.Navigation.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

/// <summary>
/// Composition root for one preview panel. The panel behaviour is split into focused facets
/// (selection, annotations, batch operations, folder tags, the image strip and folder content);
/// this type only wires them together and exposes the small surface required by the shell.
/// </summary>
internal sealed class PreviewPanelViewModel : NavigatedViewModelBase
{
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly IDialogService _dialogService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;

    public PreviewPanelViewModel(IMediaPreviewService imageService,
                                 IFolderService folderService,
                                 IFileSystemService fileSystemService,
                                 IFileSystemWatcherService fileSystemWatcherService,
                                 IMediaPreviewOperationsService fileOperationsService,
                                 IMediaBatchOperationsService batchOperationsService,
                                 IFileDialogService fileDialogService,
                                 IConfirmationDialogService confirmationDialogService,
                                 MediaSelectionCoordinator selectionCoordinator,
                                 FileTransferCoordinator fileTransferCoordinator,
                                 IFolderStatisticsService folderStatisticsService,
                                 IFolderClusterizationService folderClusterizationService,
                                 INotificationService notificationService,
                                 IConfigurationService configurationService,
                                 IMediaAnnotationsService annotationsService,
                                 IFolderTagApplicationService folderTagApplicationService,
                                 LocalizationManager localization,
                                 TrayAnalysisProgressService trayAnalysisProgressService,
                                 IDialogService dialogService,
                                 IMapper mapper,
                                 ILogger logger,
                                 ImagePreviewDropHandler imagePreviewDropHandler,
                                 SynchronizationContext synchronizationContext)
    {
        _fileOperationsService = fileOperationsService;
        _dialogService = dialogService;
        _mapper = mapper;
        _logger = logger;
        _synchronizationContext = synchronizationContext;

        TimelineVm = new TimelineViewModel(
            folderStatisticsService,
            configurationService,
            trayAnalysisProgressService,
            synchronizationContext);

        Selection = new PreviewSelectionViewModel(
            localization,
            selectionCoordinator,
            mapper,
            () => FileManagerPanel,
            preview => ImagePreviews.Contains(preview));

        var annotationCache = new PreviewAnnotationCache(
            annotationsService,
            fileSystemService,
            localization,
            logger,
            synchronizationContext);

        PreviewFolderContentCoordinator? folderContent = null;

        Annotations = new PreviewAnnotationEditorViewModel(
            Selection,
            annotationCache,
            annotationsService,
            configurationService,
            notificationService,
            localization,
            logger,
            synchronizationContext,
            () => folderContent?.FolderToken ?? CancellationToken.None);

        Batch = new PreviewBatchOperationsViewModel(
            Selection,
            Annotations,
            folderService,
            batchOperationsService,
            fileDialogService,
            confirmationDialogService,
            configurationService,
            notificationService,
            fileTransferCoordinator,
            localization,
            mapper,
            logger,
            synchronizationContext,
            () => FileManagerPanel);

        MediaStrip = new PreviewImageStripViewModel(
            imageService,
            fileSystemService,
            fileOperationsService,
            configurationService,
            Selection,
            annotationCache,
            imagePreviewDropHandler,
            mapper,
            logger,
            synchronizationContext,
            () => folderContent?.FolderToken ?? CancellationToken.None);

        folderContent = new PreviewFolderContentCoordinator(
            folderService,
            fileSystemService,
            fileSystemWatcherService,
            folderStatisticsService,
            folderClusterizationService,
            fileOperationsService,
            MediaStrip,
            annotationCache,
            TimelineVm,
            logger,
            synchronizationContext);
        FolderContent = folderContent;

        FolderTags = new PreviewFolderTagsViewModel(
            folderTagApplicationService,
            MediaStrip,
            notificationService,
            localization,
            logger,
            synchronizationContext,
            () => FolderToken,
            FolderContent.ReconcileFolderAndRefreshAsync);

        annotationCache.SetSources(
            () => MediaStrip.ImagePreviews,
            () => Selection.SelectedPreview,
            () => MediaStrip.SelectedFolderPath,
            () => FolderToken);
        annotationCache.SetReconcileRequest(FolderContent.ReconcileFolderAndRefreshAsync);

        Selection.SetActions(Batch.CopySelectionAsync, Batch.MoveSelectionAsync);

        Selection.PrimarySelectionChanged += OnPrimarySelectionChanged;
        Selection.SelectionChanged += OnSelectionChanged;
        TimelineVm.StatisticsClick.ObserveOn(_synchronizationContext).Subscribe(_ => OpenGlanceViewer());
    }

    public PreviewSelectionViewModel Selection { get; }

    public PreviewAnnotationEditorViewModel Annotations { get; }

    public PreviewBatchOperationsViewModel Batch { get; }

    public PreviewFolderTagsViewModel FolderTags { get; }

    public PreviewImageStripViewModel MediaStrip { get; }

    public PreviewFolderContentCoordinator FolderContent { get; }

    public TimelineViewModel TimelineVm { get; }

    public FileManagerPanel FileManagerPanel
    {
        get => MediaStrip.FileManagerPanel;
        private set => MediaStrip.FileManagerPanel = value;
    }

    // Members kept for the MCP UI host and shell integration. New UI code should bind the
    // facet properties above directly.
    public SortedObservableCollection<MediaPreviewViewModel> ImagePreviews => MediaStrip.ImagePreviews;

    public MediaPreviewViewModel? SelectedPreview => Selection.SelectedPreview;

    private CancellationToken FolderToken => FolderContent.FolderToken;

    /// <inheritdoc />
    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
        FileManagerPanel = navigationContext.Parameters["panel"] is FileManagerPanel panel
            ? panel
            : FileManagerPanel.Undefined;

        MediaStrip.ApplySavedSortDirection();
        FolderContent.Start();
        FolderTags.Start();
    }

    /// <inheritdoc />
    public override void OnNavigatedFrom(NavigationContext navigationContext)
    {
        FolderTags.Stop();
        FolderContent.Stop();
        MediaStrip.CancelPendingOperations();
        Annotations.CancelPendingLoad();
    }

    private void OnPrimarySelectionChanged(object? sender, EventArgs args)
    {
        if (Selection.SelectedPreview is { } selectedPreview)
        {
            _fileOperationsService.SetSelectedPreview(
                new SelectedMediaPreview(_mapper.Map<MediaPreview>(selectedPreview), FileManagerPanel));
        }

        RaisePropertyChanged(nameof(SelectedPreview));
    }

    private void OnSelectionChanged(object? sender, EventArgs args) =>
        RaisePropertyChanged(nameof(SelectedPreview));

    private void OpenGlanceViewer()
    {
        try
        {
            var parameters = new DialogParameters { { "selectedFolder", FolderContent.SelectedDirectory! } };
            _dialogService.Show("glanceViewer", parameters, null, "childWindow");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open glance viewer window");
        }
    }
}
