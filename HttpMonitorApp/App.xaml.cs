using System;
using System.Windows;
using System.Windows.Threading;

namespace HttpMonitor
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"Ошибка UI: {e.Exception.Message}\n\n{e.Exception.StackTrace}",
                "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (Exception)e.ExceptionObject;
            MessageBox.Show($"Системная ошибка: {ex.Message}\n\n{ex.StackTrace}",
                "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            MessageBox.Show($"Ошибка в задаче: {e.Exception.Message}\n\n{e.Exception.StackTrace}",
                "Ошибка задачи", MessageBoxButton.OK, MessageBoxImage.Error);
            e.SetObserved();
        }
    }
}