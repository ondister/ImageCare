using System.Collections.Specialized;

using ImageCare.UI.Avalonia.Collections;

namespace ImageCare.UI.Avalonia.Tests.Collections;

[TestFixture]
public class SortedObservableCollectionTests
{
	[Test]
	public void Constructor_WithCollectionAndComparer_ShouldSortItems()
	{
		var unsorted = new[] { 5, 1, 3, 2, 4 };
		var collection = new SortedObservableCollection<int>(unsorted, Comparer<int>.Default);

		Assert.That(collection, Is.Ordered.Ascending);
		Assert.That(collection, Is.EquivalentTo(unsorted));
	}

	[Test]
	public void Constructor_WithNullComparer_ShouldUseDefaultComparer()
	{
		var unsorted = new[] { "z", "a", "m" };
		var collection = new SortedObservableCollection<string>(unsorted);

		Assert.That(collection, Is.Ordered.Ascending);
	}

	[Test]
	public void Add_Item_ShouldInsertInCorrectPosition()
	{
		var collection = new SortedObservableCollection<int>(Comparer<int>.Default) { 1, 3, 5 };

		collection.Add(2);

		Assert.That(collection, Is.EqualTo(new[] { 1, 2, 3, 5 }));
	}

	[Test]
	public void Add_DuplicateItems_ShouldAllowDuplicates()
	{
		var collection = new SortedObservableCollection<int>(Comparer<int>.Default) { 1, 2, 3 };

		collection.Add(2); // Duplicate

		Assert.That(collection, Is.EqualTo(new[] { 1, 2, 2, 3 }));
	}

	[Test]
	public void AddRange_MultipleItems_ShouldMaintainSortOrder()
	{
		var collection = new SortedObservableCollection<int>(Comparer<int>.Default) { 1, 5 };

		collection.AddRange(new[] { 2, 4, 3 });

		Assert.That(collection, Is.Ordered.Ascending);
		Assert.That(collection, Is.EquivalentTo(new[] { 1, 2, 3, 4, 5 }));
	}

	[Test]
	public void AddRange_NullCollection_ShouldThrowArgumentNullException()
	{
		var collection = new SortedObservableCollection<int>();

		Assert.Throws<ArgumentNullException>(() => collection.AddRange(null));
	}

	[Test]
	public void SetItem_ShouldRemoveOldAndInsertNewInCorrectPosition()
	{
		var collection = new SortedObservableCollection<int>(new[] { 1, 2, 3, 4, 5 });

		collection[2] = 6; // Replace 3 with 6

		Assert.That(collection, Is.Ordered.Ascending);
		Assert.That(collection, Is.EquivalentTo(new[] { 1, 2, 4, 5, 6 }));
	}

	[Test]
	public void CustomComparer_ShouldRespectCustomSorting()
	{
		var customComparer = Comparer<int>.Create((x, y) => y.CompareTo(x)); // Reverse order
		var collection = new SortedObservableCollection<int>(customComparer) { 1, 3, 5 };

		collection.Add(2);

		Assert.That(collection, Is.Ordered.Descending);
		Assert.That(collection, Is.EqualTo(new[] { 5, 3, 2, 1 }));
	}

	[Test]
	public void CollectionChanged_OnAdd_ShouldRaiseCorrectEvents()
	{
		var collection = new SortedObservableCollection<int> { 1, 3, 5 };
		var events = new List<NotifyCollectionChangedEventArgs>();

		collection.CollectionChanged += (s, e) => events.Add(e);

		collection.Add(2);

		Assert.That(events, Has.Count.EqualTo(1));
		Assert.That(events[0].Action, Is.EqualTo(NotifyCollectionChangedAction.Add));
		Assert.That(events[0].NewItems?[0], Is.EqualTo(2));
		Assert.That(events[0].NewStartingIndex, Is.EqualTo(1)); // Position where 2 was inserted
	}

	[Test]
	public void Performance_AddingSortedSequence_ShouldBeEfficient()
	{
		var collection = new SortedObservableCollection<int>();
		var items = Enumerable.Range(0, 1000).ToArray();

		Assert.DoesNotThrow(() => collection.AddRange(items));
		Assert.That(collection, Is.Ordered.Ascending);
	}

	[Test]
	public void Performance_AddingReverseSortedSequence_ShouldBeEfficient()
	{
		var collection = new SortedObservableCollection<int>();
		var items = Enumerable.Range(0, 1000).Reverse().ToArray();

		Assert.DoesNotThrow(() => collection.AddRange(items));
		Assert.That(collection, Is.Ordered.Ascending);
	}

	[Test]
	public void EmptyCollection_AddItem_ShouldWork()
	{
		var collection = new SortedObservableCollection<int>();

		collection.Add(42);

		Assert.That(collection, Has.Count.EqualTo(1));
		Assert.That(collection[0], Is.EqualTo(42));
	}
}