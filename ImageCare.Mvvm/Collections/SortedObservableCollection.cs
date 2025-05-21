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

	private int BinarySearch<T>(IList<T> items, int index, int count, T item, IComparer<T>? comparer)
	{
		try
		{
			return Array.BinarySearch(items.ToArray(), index, count, item, comparer);
		}
		catch (
			Exception ex)
		{
			return 0;
		}
	}
}