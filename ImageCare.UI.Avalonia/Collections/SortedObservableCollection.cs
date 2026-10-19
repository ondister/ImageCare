using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace ImageCare.UI.Avalonia.Collections;

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
		ArgumentNullException.ThrowIfNull(items);

		var incoming = items.OrderBy(item => item, _comparer).ToArray();
		if (incoming.Length == 0)
		{
			return;
		}

		var merged = new List<T>(Items.Count + incoming.Length);
		var existingIndex = 0;
		var incomingIndex = 0;
		while (existingIndex < Items.Count && incomingIndex < incoming.Length)
		{
			if (_comparer.Compare(Items[existingIndex], incoming[incomingIndex]) <= 0)
			{
				merged.Add(Items[existingIndex++]);
			}
			else
			{
				merged.Add(incoming[incomingIndex++]);
			}
		}

		while (existingIndex < Items.Count)
		{
			merged.Add(Items[existingIndex++]);
		}

		while (incomingIndex < incoming.Length)
		{
			merged.Add(incoming[incomingIndex++]);
		}

		Items.Clear();
		foreach (var item in merged)
		{
			Items.Add(item);
		}

		OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
		OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
		OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
	}

	public int RemoveWhere(Func<T, bool> predicate)
	{
		ArgumentNullException.ThrowIfNull(predicate);

		var removedCount = 0;
		for (var index = Items.Count - 1; index >= 0; index--)
		{
			if (!predicate(Items[index]))
			{
				continue;
			}

			Items.RemoveAt(index);
			removedCount++;
		}

		if (removedCount > 0)
		{
			OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
			OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
			OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
		}

		return removedCount;
	}

	public void RefreshSort()
	{
		var items = Items.ToList();
		Items.Clear();
		foreach (var item in items.OrderBy(x => x, _comparer))
		{
			Items.Add(item);
		}

		OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
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