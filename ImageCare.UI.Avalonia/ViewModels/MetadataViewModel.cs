using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

using DryIoc;

using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.Core.Services.FileSystemImageService;
using ImageCare.Mvvm;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Commands;

using XmpCore.Impl;

namespace ImageCare.UI.Avalonia.ViewModels
{
    internal sealed class MetadataViewModel:ViewModelBase
    {
        private readonly IFileSystemImageService _imageService;
        private readonly IFileOperationsService _fileOperationsService;

        public ObservableCollection<TagDescriptionViewModel> MetadataList { get; }

        public MetadataViewModel(IFileSystemImageService imageService, 
                                 IFileOperationsService fileOperationsService)
        {
            _imageService = imageService;
            _fileOperationsService = fileOperationsService;

            MetadataList = new ObservableCollection<TagDescriptionViewModel>();
            OnViewLoadedCommand = new DelegateCommand(OnViewLoaded);
        }

        private void OnViewLoaded()
        {
            MetadataList.Clear();

            var lastSelectedMediaPreview = _fileOperationsService.GetLastSelectedMediaPreview();

            if (lastSelectedMediaPreview == null)
            {
                return;
            }

            FillMetadataListAsync(lastSelectedMediaPreview);
        }

        private async Task FillMetadataListAsync(MediaPreview lastSelectedMediaPreview)
        {
            var metadata = await _imageService.GetMediaMetadataAsync(lastSelectedMediaPreview);

            foreach (var item in metadata.AllMetadata)
            {
                MetadataList.Add(new TagDescriptionViewModel(item.Key, item.Value));
            }
        }

        public ICommand OnViewLoadedCommand { get; }

    }
}
