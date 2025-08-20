using System.Diagnostics;
using System.Windows.Input;

namespace StarResonance.DPS.ViewModels;

public class AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
    : ObservableObject, ICommand
{
    private readonly Func<object?, Task> _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    private bool _isRunning;

    private bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value)) CommandManager.InvalidateRequerySuggested();
        }
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter)
    {
        return !IsRunning && (canExecute == null || canExecute(parameter));
    }

    public async void Execute(object? parameter)
    {
        try
        {
            if (!CanExecute(parameter)) return;
            try
            {
                IsRunning = true;
                await _execute(parameter);
            }
            finally
            {
                IsRunning = false;
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine("AsyncRelayCommand execution failed: " + e.Message);
        }
    }
}