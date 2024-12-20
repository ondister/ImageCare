using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Modules.Logging.Services;
using ImageCare.Mvvm;

using Prism.Commands;
using Prism.Services.Dialogs;

namespace ImageCare.UI.Avalonia.ViewModels
{
    internal class BottomBarViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogService;

        public ICommand OpenLogWindowCommand { get; }

        public BottomBarViewModel(IDialogService dialogService, ILogNotificationService logNotificationService)
        {
            _dialogService = dialogService;

            OpenLogWindowCommand = new DelegateCommand(OpenLogWindow);
        }

        private void OpenLogWindow()
        {
            IDialogParameters param= new DialogParameters();
            _dialogService.Show("logViewer", param, r => { }, "childWindow");
        }
    }
}
