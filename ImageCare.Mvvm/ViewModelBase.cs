using Prism.Commands.Ex;
using Prism.Mvvm;

namespace ImageCare.Mvvm;

public abstract class ViewModelBase : BindableBase
{
	protected DelegateCommand CreateCommand(Action execute)
	{
		return new DelegateCommand(execute);
	}

	protected DelegateCommand CreateCommand(Action execute, Func<bool> canExecute)
	{
		return new DelegateCommand(execute, canExecute);
	}

	protected DelegateCommand CreateCommand(Action execute, Func<bool> canExecute, Action<Exception> exceptionHandler)
	{
		return new DelegateCommand(execute, canExecute, exceptionHandler);
	}

	protected DelegateCommand<T> CreateCommand<T>(Action<T> execute)
	{
		return new DelegateCommand<T>(execute);
	}

	protected DelegateCommand<T> CreateCommand<T>(Action<T> execute, Func<T, bool> canExecute)
	{
		return new DelegateCommand<T>(execute, canExecute);
	}

	protected AsyncDelegateCommand CreateAsyncCommand(Func<Task> execute)
	{
		return new AsyncDelegateCommand(async (progress, token) => await execute());
	}

	protected AsyncDelegateCommand CreateAsyncCommand(Func<Task> execute, Func<bool> canExecute)
	{
		return new AsyncDelegateCommand(async (progress, token) => await execute(), canExecute);
	}

	protected AsyncDelegateCommand CreateAsyncCommand(Func<IProgress<int>, CancellationToken, Task> execute)
	{
		return new AsyncDelegateCommand(execute);
	}

	protected AsyncDelegateCommand CreateAsyncCommand(Func<IProgress<int>, CancellationToken, Task> execute,
	                                                  Func<bool> canExecute)
	{
		return new AsyncDelegateCommand(execute, canExecute);
	}

	protected AsyncDelegateCommand CreateAsyncCommand(Func<IProgress<int>, CancellationToken, Task> execute,
	                                                  Action<Exception> exceptionHandler)
	{
		return new AsyncDelegateCommand(execute, () => true, exceptionHandler);
	}

	protected AsyncDelegateCommand CreateAsyncCommand(Func<IProgress<int>, CancellationToken, Task> execute,
	                                                  Func<bool> canExecute,
	                                                  Action<Exception> exceptionHandler)
	{
		return new AsyncDelegateCommand(execute, canExecute, exceptionHandler);
	}
}