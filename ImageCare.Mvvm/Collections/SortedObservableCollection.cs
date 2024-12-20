using System.Collections.ObjectModel;

namespace ImageCare.Mvvm.Collections;

public class SortedObservableCollection<T> : ObservableCollection<T>
{
    public SortedObservableCollection() { }

    public SortedObservableCollection(IEnumerable<T> collection)
    {
        foreach (var item in collection)
        {
            InsertItem(item);
        }
    }

    public void InsertItem(T item, IComparer<T>? comparer = null)
    {
        var indexToInsert = BinarySearch(Items, 0, Count, item, comparer);

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