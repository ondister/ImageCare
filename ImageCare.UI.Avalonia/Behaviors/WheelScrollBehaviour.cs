using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Xaml.Interactivity;

namespace ImageCare.UI.Avalonia.Behaviors;

public class WheelScrollBehaviour : Behavior<ScrollViewer>
{
	protected override void OnAttached()
	{
		base.OnAttached();
		AssociatedObject.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
	}

	protected override void OnDetaching()
	{
		AssociatedObject.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged);
		base.OnDetaching();
	}

	private void OnPointerWheelChanged(object sender, PointerWheelEventArgs e)
	{
		if (AssociatedObject is ScrollViewer scrollViewer)
		{
			scrollViewer.Offset += new Vector(e.Delta.Y * 30, 0);
			e.Handled = true;
		}
	}
}