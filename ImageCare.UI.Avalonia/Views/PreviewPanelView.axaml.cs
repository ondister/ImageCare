using Avalonia.Controls;

namespace ImageCare.UI.Avalonia.Views;

public partial class PreviewPanelView : UserControl
{
	public PreviewPanelView()
	{
		InitializeComponent();
	}

	internal bool FocusPanel() => PreviewStrip.FocusPanel();
}
