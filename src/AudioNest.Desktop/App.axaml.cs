using AudioNest.Application;
using AudioNest.Audio;
using AudioNest.Hardware;
using AudioNest.Hardware.SoundFlow;
using AudioNest.Persistence;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace AudioNest.Desktop;

public partial class App : Avalonia.Application
{
    private ServiceProvider? _services;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Explicit factories keep the composition root easy to audit and AOT-friendly.
            var services = new ServiceCollection();
            services.AddSingleton<IAudioBackend>(_ => new SoundFlowBackend());
            services.AddSingleton<IProjectStore>(_ => new JsonProjectStore());
            services.AddSingleton(_ => new ProjectSession());
            services.AddSingleton(p => new AudioLibrary(p.GetRequiredService<IAudioBackend>()));
            services.AddSingleton(p => new AudioTransport(p.GetRequiredService<IAudioBackend>()));
            services.AddSingleton(p => new MainViewModel(
                p.GetRequiredService<ProjectSession>(), p.GetRequiredService<AudioLibrary>(),
                p.GetRequiredService<AudioTransport>(), p.GetRequiredService<IAudioBackend>(),
                p.GetRequiredService<IProjectStore>()));
            _services = services.BuildServiceProvider();
            desktop.MainWindow = new MainWindow(_services.GetRequiredService<MainViewModel>());
            desktop.Exit += (_, _) => _services.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
