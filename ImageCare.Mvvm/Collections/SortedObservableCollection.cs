using System.Collections.ObjectModel;

namespace ImageCare.Mvvm.Collections;

public class SortedObservableCollection<T> : ObservableCollection<T>
{
	private readonly IComparer<T>? _comparer;

	public SortedObservableCollection(IEnumerable<T> collection, IComparer<T>? comparer)
	{
		_comparer = comparer;
		foreach (var item in collection)
		{
			InsertItem(item);
		}
	}

	public SortedObservableCollection(IComparer<T>? comparer)
	{
		_comparer = comparer;
	}

	public void InsertItem(T item)
	{
		var indexToInsert = BinarySearch(Items, 0, Count, item, _comparer);

		if (indexToInsert < 0)
		{
			indexToInsert = ~indexToInsert;
		}

		Insert(indexToInsert, item);
	}

	private int BinarySearch<T>(IList<T> items, int index, int length, T value, IComparer<T>? comparer)
	{
		comparer ??= Comparer<T>.Default;

		var low = index;
		var high = index + length - 1;

		while (low <= high)
		{
			var mid = low + ((high - low) >> 1);
			var comparison = comparer.Compare(items[mid], value);

			switch (comparison)
			{
				case 0:
					return mid;
				case < 0:
					low = mid + 1;
					break;
				default:
					high = mid - 1;
					break;
			}
		}

		return ~low;
	}
}