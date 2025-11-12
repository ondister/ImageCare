using System.Collections.ObjectModel;

namespace ImageCare.Mvvm.Collections;

public class SortedObservableCollection<T> : ObservableCollection<T>
{
	private readonly IComparer<T> _comparer;

	public SortedObservableCollection(IEnumerable<T> collection, IComparer<T>? comparer = null)
		: this(comparer)
	{
		AddRange(collection);
	}

	public SortedObservableCollection(IComparer<T>? comparer = null)
	{
		_comparer = comparer ?? Comparer<T>.Default;
	}

	public void AddRange(IEnumerable<T> items)
	{
		if (items == null)
		{
			throw new ArgumentNullException(nameof(items));
		}

		foreach (var item in items)
		{
			Add(item);
		}
	}

	protected override void InsertItem(int index, T item)
	{
		var correctIndex = FindInsertIndex(item);
		base.InsertItem(correctIndex, item);
	}

	protected override void SetItem(int index, T item)
	{
		RemoveAt(index);
		Add(item);
	}

	private int FindInsertIndex(T item)
	{
		var low = 0;
		var high = Count - 1;

		while (low <= high)
		{
			var mid = low + ((high - low) >> 1);
			var comparison = _comparer.Compare(Items[mid], item);

			switch (comparison)
			{
				case 0:
					return mid; // Insert before existing equal element
				case < 0:
					low = mid + 1;
					break;
				default:
					high = mid - 1;
					break;
			}
		}

		return low;
	}
}