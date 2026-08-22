using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.PosterOverlays;

/// <summary>
/// Registers the plugin's long-lived services.
/// </summary>
/// <remarks>
/// Scheduled tasks are found by the server on their own - it calls
/// <c>AddTasks(GetExports&lt;IScheduledTask&gt;(false))</c> over every loaded assembly - so only
/// the watcher needs registering here.
/// </remarks>
public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHostedService<OverlayRefreshWatcher>();
    }
}
