using System;

using Mapsui;
using Mapsui.UI;

namespace ImageCare.UI.Avalonia.Controls;

internal sealed class MapControlMediator
{
	private IMapControl? _mapControl;

	public void SetMapControl(IMapControl mapControl)
	{
		_mapControl = mapControl;
	}

	public void SetMap(Map map)
	{
		if (_mapControl == null)
		{
			throw new NullReferenceException("Map control is null. Set Map control first");
		}

		_mapControl.Map = map;
	}
}