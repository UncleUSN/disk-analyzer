using System.IO;
using System.Windows;
using System.Windows.Threading;
using DiskAnalyzer.Core;
using Xunit;

namespace DiskAnalyzer.UiTests;

public class ScanThreadingTests
{
    [Fact]
    public void Scan_does_not_touch_ui_objects_from_the_worker_thread()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Exception? error = null;
        string? status = null;
        var names = new List<string>();
        using var done = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            string? root = null;
            try
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                var application = new App
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                application.InitializeComponent();

                root = CreateSampleFolder();
                var window = new MainWindow();
                window.Show();
                window.PathBox.Text = root;
                window.AgeSlider.Value = 1;
                window.SizeSlider.Value = 1;

                var frame = new DispatcherFrame();
                var scan = window.RunScanAsync();
                scan.ContinueWith(
                    completed =>
                    {
                        try
                        {
                            if (completed.IsFaulted)
                                error = completed.Exception!.GetBaseException();
                            status = window.StatusText.Text;
                            names.AddRange(window.ResultsGrid.Items.Cast<FileCandidate>().Select(item => item.Name));
                        }
                        catch (Exception ex)
                        {
                            error ??= ex;
                        }
                        finally
                        {
                            frame.Continue = false;
                        }
                    },
                    TaskScheduler.FromCurrentSynchronizationContext());

                Dispatcher.PushFrame(frame);
                window.Close();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                try
                {
                    if (root is not null && Directory.Exists(root))
                        Directory.Delete(root, recursive: true);
                }
                catch (IOException)
                {
                }

                try
                {
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
                catch (InvalidOperationException)
                {
                }

                done.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        Assert.True(done.Wait(TimeSpan.FromSeconds(60)), "Сканирование в потоке окна не завершилось.");
        Assert.True(error is null, error?.ToString());
        Assert.DoesNotContain("Ошибка сканирования", status ?? string.Empty);
        Assert.DoesNotContain("другой поток", status ?? string.Empty);
        Assert.Contains("Готово", status ?? string.Empty);
        Assert.Equal(new[] { "old-cache.dat" }, names);
    }

    private static string CreateSampleFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "disk-analyzer-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "old-cache.dat");
        File.WriteAllBytes(file, new byte[1024 * 1024]);
        var old = DateTime.UtcNow.AddDays(-30);
        File.SetLastWriteTimeUtc(file, old);
        File.SetLastAccessTimeUtc(file, old);
        return root;
    }
}
