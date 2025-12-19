using ImageCare.UI.Avalonia.ViewModels;

using DelegateCommand = Prism.Commands.Ex.DelegateCommand;

namespace ImageCare.UI.Avalonia.Tests;

[TestFixture]
public class ViewModelBaseTests
{
    private TestViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _viewModel = new TestViewModel();
    }

    [Test]
    public void CreateCommand_WithExecuteAction_ShouldCreateWorkingCommand()
    {
        var executed = false;
        var command = _viewModel.CreateCommand(() => executed = true);

        command.Execute();

        Assert.That(executed, Is.True);
        Assert.That(command.CanExecute(), Is.True);
    }

    [Test]
    public void CreateCommand_WithCanExecute_ShouldRespectCanExecute()
    {
        var canExecute = false;

        var command = _viewModel.CreateCommand(
            execute: () => _ = true,
            canExecute: () => canExecute);

        Assert.That(command.CanExecute(), Is.False);

        canExecute = true;
        command.RaiseCanExecuteChanged();

        Assert.That(command.CanExecute(), Is.True);
    }

    [Test]
    public void CreateCommand_WithExceptionHandler_ShouldHandleExceptions()
    {
        var exceptionHandled = false;
        var testException = new InvalidOperationException("Test exception");

        var command = _viewModel.CreateCommand(
            execute: () => throw testException,
            canExecute: () => true,
            exceptionHandler: ex => exceptionHandled = true);

        command.Execute();

        Assert.That(exceptionHandled, Is.True);
    }

    [Test]
    public void CreateCommand_Generic_WithParameter_ShouldPassParameter()
    {
        string? receivedParameter = null;
        var testParameter = "test parameter";

        var command = _viewModel.CreateCommand<string>(param => receivedParameter = param);

        command.Execute(testParameter);

        Assert.That(receivedParameter, Is.EqualTo(testParameter));
    }

    [Test]
    public async Task CreateAsyncCommand_Simple_ShouldExecuteAsyncOperation()
    {
        var executed = false;
        var command = _viewModel.CreateAsyncCommand(async () =>
        {
            await Task.Delay(10);
            executed = true;
        });

        await command.ExecuteAsync();

        Assert.That(executed, Is.True);
    }

    [Test]
    public void CreateAsyncCommand_WithProgressAndCancellation_ShouldSupportProgress()
    {
        var command = _viewModel.CreateAsyncCommand(async (progress, token) =>
        {
            progress.Report(25);
            progress.Report(50);
            progress.Report(75);
            progress.Report(100);
            await Task.Delay(10, token);
        });

        Assert.DoesNotThrowAsync(async () => await command.ExecuteAsync());
    }

    [Test]
    public async Task CreateAsyncCommand_WhenCancelled_ShouldCancelOperation()
    {
        var operationWasCancelled = false;
        var cancellationSignal = new SemaphoreSlim(0, 1);

        var command = _viewModel.CreateAsyncCommand(async (progress, token) =>
        {
            try
            {
                cancellationSignal.Release();

                await Task.Delay(2000, token);
            }
            catch (TaskCanceledException)
            {
                operationWasCancelled = true;
                throw;
            }
        });

        var task = command.ExecuteAsync();

        await cancellationSignal.WaitAsync(TimeSpan.FromSeconds(1));

        command.Cancel.Execute();

        await Task.WhenAny(task, Task.Delay(1000));

        Assert.That(operationWasCancelled, Is.True);
    }

    [Test]
    public void CreateAsyncCommand_WithExceptionHandler_ShouldHandleAsyncExceptions()
    {
        var exceptionHandled = false;
        var testException = new InvalidOperationException("Async test exception");

        var command = _viewModel.CreateAsyncCommand(
            execute: async (progress, token) =>
            {
                await Task.Delay(10);
                throw testException;
            },
            exceptionHandler: ex => exceptionHandled = true);

        Assert.DoesNotThrowAsync(async () => await command.ExecuteAsync());
        Assert.That(exceptionHandled, Is.True);
    }

    [Test]
    public void AsyncCommand_WhileExecuting_ShouldUpdateCanExecute()
    {
        var command = _viewModel.CreateAsyncCommand(async () => { await Task.Delay(100); });

        Assert.That(command.CanExecute(), Is.True);

        var executionTask = command.ExecuteAsync();

        Assert.That(command.CanExecute(), Is.False);

        executionTask.Wait(200);

        Assert.That(command.CanExecute(), Is.True);
    }

    [Test]
    public void CreateCommand_GenericWithoutCanExecute_ShouldPassParameter()
    {
        string receivedParameter = null;
        var testParameter = "test parameter";

        var command = _viewModel.CreateCommand<string>(param => receivedParameter = param);

        command.Execute(testParameter);

        Assert.That(receivedParameter, Is.EqualTo(testParameter));
    }

    private class TestViewModel : ViewModelBase
    {
        private readonly Action? _onDispose;

        public TestViewModel(Action? onDispose = null)
        {
            _onDispose = onDispose;
        }

        public new DelegateCommand CreateCommand(Action execute)
        {
            return base.CreateCommand(execute);
        }

        public new DelegateCommand CreateCommand(Action execute, Func<bool> canExecute)
        {
            return base.CreateCommand(execute, canExecute);
        }

        public new DelegateCommand CreateCommand(Action execute, Func<bool> canExecute, Action<Exception> exceptionHandler)
        {
            return base.CreateCommand(execute, canExecute, exceptionHandler);
        }

        public new Prism.Commands.Ex.DelegateCommand<T> CreateCommand<T>(Action<T> execute)
        {
            return base.CreateCommand(execute);
        }

        public new Prism.Commands.Ex.DelegateCommand<T> CreateCommand<T>(Action<T> execute, Func<T, bool> canExecute)
        {
            return base.CreateCommand(execute, canExecute);
        }

        public new Prism.Commands.Ex.AsyncDelegateCommand CreateAsyncCommand(Func<Task> execute)
        {
            return base.CreateAsyncCommand(execute);
        }

        public new Prism.Commands.Ex.AsyncDelegateCommand CreateAsyncCommand(Func<Task> execute, Func<bool> canExecute)
        {
            return base.CreateAsyncCommand(execute, canExecute);
        }

        public new Prism.Commands.Ex.AsyncDelegateCommand CreateAsyncCommand(Func<IProgress<int>, CancellationToken, Task> execute)
        {
            return base.CreateAsyncCommand(execute);
        }

        public new Prism.Commands.Ex.AsyncDelegateCommand CreateAsyncCommand(Func<IProgress<int>, CancellationToken, Task> execute, Func<bool> canExecute)
        {
            return base.CreateAsyncCommand(execute, canExecute);
        }

        public new Prism.Commands.Ex.AsyncDelegateCommand CreateAsyncCommand(Func<IProgress<int>, CancellationToken, Task> execute, Action<Exception> exceptionHandler)
        {
            return base.CreateAsyncCommand(execute, exceptionHandler);
        }

        public new Prism.Commands.Ex.AsyncDelegateCommand CreateAsyncCommand(Func<IProgress<int>, CancellationToken, Task> execute, Func<bool> canExecute, Action<Exception> exceptionHandler)
        {
            return base.CreateAsyncCommand(execute, canExecute, exceptionHandler);
        }
    }
}