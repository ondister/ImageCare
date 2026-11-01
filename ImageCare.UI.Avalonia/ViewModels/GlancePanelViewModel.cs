using System;
using System.Threading;
using System.Threading.Tasks;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderClusterizationService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.FolderStatisticsService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.MediaBatchOperationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Glance;

using MapsterMapper;

using Prism.Dialogs;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

/// <summary>
/// Composition root for the folder overview window. Behaviour is split into focused facets
/// (preview grid, selection, annotation editor, batch operations, deleted files and folder
/// content); this type wires them together and exposes the surface required by the dialog.
/// </summary>
internal sealed class GlancePanelViewModel : ViewModelBase, IDialogAware
{
    private readonly LocalizationManager _localization;
    private readonly AdaptiveHelpService _adaptiveHelpService;
    private readonly AdaptiveHelpContextService _helpContext;
    private readonly GlanceAnnotationCache _annotationCache;
    private readonly ILogger _logger;

    private FileManagerPanel _overviewPanel = FileManagerPanel.Undefined;
    private string _title = string.Empty;

    public GlancePanelViewModel(IFolderStatisticsService folderStatisticsService,
                                IFolderClusterizationService folderClusterizationService,
                                IMediaPreviewService imageService,
                                IFolderService folderService,
                                IFileSystemWatcherService fileSystemWatcherService,
                                IMediaPreviewOperationsService fileOperationsService,
                                IMediaBatchOperationsService batchOperationsService,
                                IFileDialogService fileDialogService,
                                IConfirmationDialogService confirmationDialogService,
                                AdaptiveHelpContextService helpContext,
                                AdaptiveHelpService adaptiveHelpService,
                                IConfigurationService configurationService,
                                IMediaAnnotationsService annotationsService,
                                TrayAnalysisProgressService trayAnalysisProgressService,
                                INotificationService notificationService,
                                LocalizationManager localization,
                                IMapper mapper,
                                ILogger logger,
                                SynchronizationContext synchronizationContext,
                                DialogCloseListener requestClose)
    {
        _localization = localization;
        _adaptiveHelpService = adaptiveHelpService;
        _helpContext = helpContext;
        _logger = logger;
        RequestClose = requestClose;

        TimelineVm = new TimelineViewModel(
            folderStatisticsService,
            configurationService,
            trayAnalysisProgressService,
            synchronizationContext);

        Operations = new GlanceOperationViewModel(localization, helpContext);
        Selection = new GlanceSelectionViewModel(localization, Operations, helpContext);

        _annotationCache = new GlanceAnnotationCache(
            annotationsService,
            localization,
            logger,
            synchronizationContext);

        PreviewGrid = new GlancePreviewGridViewModel(
            imageService,
            configurationService,
            Selection,
            _annotationCache,
            mapper,
            logger,
            synchronizationContext);

        Selection.Attach(PreviewGrid.ImagePreviews);
        _annotationCache.SetSources(
            () => PreviewGrid.ImagePreviews,
            () => PreviewGrid.SelectedFolderPath);

        Annotations = new GlanceAnnotationEditorViewModel(
            Selection,
            annotationsService,
            notificationService,
            localization,
            logger,
            Operations);

        Batch = new GlanceBatchOperationsViewModel(
            Selection,
            folderService,
            batchOperationsService,
            fileDialogService,
            confirmationDialogService,
            configurationService,
            notificationService,
            localization,
            mapper,
            logger,
            Operations,
            synchronizationContext,
            () => _overviewPanel);

        DeletedFiles = new GlanceDeletedFilesViewModel(
            fileOperationsService,
            imageService,
            notificationService,
            localization,
            logger,
            synchronizationContext);

        FolderContent = new GlanceFolderContentCoordinator(
            folderService,
            fileSystemWatcherService,
            folderStatisticsService,
            folderClusterizationService,
            fileOperationsService,
            PreviewGrid,
            _annotationCache,
            logger,
            synchronizationContext);

        TimelineCoordinator = new GlanceTimelineCoordinator(
            TimelineVm,
            PreviewGrid,
            Selection,
            logger);
    }

    public TimelineViewModel TimelineVm { get; }

    public GlancePreviewGridViewModel PreviewGrid { get; }

    public GlanceSelectionViewModel Selection { get; }

    public GlanceAnnotationEditorViewModel Annotations { get; }

    public GlanceBatchOperationsViewModel Batch { get; }

    public GlanceOperationViewModel Operations { get; }

    public GlanceDeletedFilesViewModel DeletedFiles { get; }

    public GlanceFolderContentCoordinator FolderContent { get; }

    public GlanceTimelineCoordinator TimelineCoordinator { get; }

    public AdaptiveHelpService AdaptiveHelp => _adaptiveHelpService;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    /// <inheritdoc />
    public DialogCloseListener RequestClose { get; }

    /// <inheritdoc />
    public bool CanCloseDialog() => true;

    /// <inheritdoc />
    public void OnDialogClosed()
    {
        try
        {
            FolderContent.Stop();
            TimelineCoordinator.Stop();
            Batch.Stop();
            DeletedFiles.Stop();
            Operations.CancelAll();
            PreviewGrid.CancelImageLoading();
            PreviewGrid.Clear();
            _annotationCache.Reset();
            TimelineVm.Clear();
            _helpContext.LeaveFolderOverview();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during separate image window cleanup");
        }
    }

    /// <inheritdoc />
    public void OnDialogOpened(IDialogParameters parameters)
    {
        try
        {
            if (parameters["selectedFolder"] is not SelectedDirectory selectedDirectory)
            {
                return;
            }

            _overviewPanel = selectedDirectory.FileManagerPanel;
            Title = _localization.Get("Overview_Title", selectedDirectory.Path);
            _helpContext.EnterFolderOverview();

            TimelineVm.Clear();
            DeletedFiles.Start();
            Batch.Start();
            TimelineCoordinator.Start();
            FolderContent.Start(selectedDirectory);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize separate image window");
        }
    }
}
