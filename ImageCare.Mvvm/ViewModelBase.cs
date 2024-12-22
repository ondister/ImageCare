using CommunityToolkit.Mvvm.ComponentModel;

using Prism.Regions;

namespace ImageCare.Mvvm
{
    public class ViewModelBase : ObservableObject, INavigationAware
    {
        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            // Auto-allow navigation
            return OnNavigatingTo(navigationContext);
        }

        /// <summary>Perform any (event) cleanup, we're navigating away.</summary>
        /// <param name="navigationContext">Navigation parameters.</param>
        public virtual void OnNavigatedFrom(NavigationContext navigationContext)
        {
        }

        /// <summary>Navigated to view.</summary>
        /// <param name="navigationContext">Navigation parameters.</param>
        public virtual void OnNavigatedTo(NavigationContext navigationContext)
        {
        }

        public virtual bool OnNavigatingTo(NavigationContext navigationContext)
        {
            return true;
        }
    }
}
