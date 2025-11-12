using Prism.Commands.Ex;
using Prism.Mvvm;

namespace ImageCare.Mvvm;

public abstract class ViewModelBase : BindableBase, IDisposable
{
	private bool _isDisposed;
	private CancellationTokenSource _globalCts;

	protected CancellationToken GlobalCancellationToken => (_globalCts ??= new CancellationTokenSource()).Token;

	public virtual void Dispose()
	{
		if (!_isDisposed)
		{
			CancelGlobalOperations();
			_globalCts?.Dispose();
			OnDispose();
			_isDisposed = true;
		}
	}

	protected virtual DelegateCommand CreateCommand(Action execute)
	{
		return new DelegateCommand(execute);
	}

	protected virtual DelegateCommand CreateCommand(Action execute, Func<bool> canExecute)
	{
		return new DelegateCommand(execute, canExecute);
	}

	protected virtual DelegateCommand CreateCommand(Action execute, Func<bool> canExecute, Action<Exception> exceptionHandler)
	{
		return new DelegateCommand(execute, canExecute, exceptionHandler);
	}

	protected virtual DelegateCommand<T> CreateCommand<T>(Action<T> execute)
	{
		return new DelegateCommand<T>(execute);
	}

	protected virtual DelegateCommand<T> CreateCommand<T>(Action<T> execute, Func<T, bool> canExecute)
	{
		return new DelegateCommand<T>(execute, canExecute);
	}

	protected virtual AsyncDelegateCommand CreateAsyncCommand(Func<Task> execute)
	{
		return new AsyncDelegateCommand(async (progress, token) => await execute());
	}

	protected virtual AsyncDelegateCommand CreateAsyncCommand(Func<Task> execute, Func<bool> canExecute)
	{
		return new AsyncDelegateCommand(async (progress, token) => await execute(), canExecute);
	}

	protected virtual AsyncDelegateCommand CreateAsyncCommand(Func<IProgress<int>, CancellationToken, Task> execute)
	{
		return new AsyncDelegateCommand(execute);
	}

	protected virtual AsyncDelegateCommand CreateAsyncCommand(Func<IProgress<int>, CancellationToken, Task> execute,
	                                                          Func<bool> canExecute)
	{
		return new AsyncDelegateCommand(execute, canExecute);
	}

	protected virtual AsyncDelegateCommand CreateAsyncCommand(Func<IProgress<int>, CancellationToken, Task> execute,
	                                                          Action<Exception> exceptionHandler)
	{
		return new AsyncDelegateCommand(execute, () => true, exceptionHandler);
	}

	protected virtual AsyncDelegateCommand CreateAsyncCommand(Func<IProgress<int>, CancellationToken, Task> execute,
	                                                          Func<bool> canExecute,
	                                                          Action<Exception> exceptionHandler)
	{
		return new AsyncDelegateCommand(execute, canExecute, exceptionHandler);
	}

	protected virtual void CancelGlobalOperations()
	{
		_globalCts?.Cancel();
		_globalCts?.Dispose();
		_globalCts = new CancellationTokenSource();
	}

	protected virtual void OnDispose() { }
}