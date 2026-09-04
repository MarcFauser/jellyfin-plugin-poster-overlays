using System;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using Jellyfin.Plugin.PosterOverlays.Models;
using Jellyfin.Plugin.PosterOverlays.State;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PosterOverlays.Controllers;

/// <summary>
/// Runs the plugin for a single item on demand.
/// </summary>
/// <remarks>
/// The item context menu in the web client is built from a hard-coded list and cannot be
/// extended by a server plugin, so this is the way to badge one film without waiting for the
/// nightly task: from the plugin's own settings page, from a script, or from any other tool.
/// </remarks>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("PosterOverlays")]
[Produces(MediaTypeNames.Application.Json)]
public class PosterOverlaysController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly ILocalizationManager _localization;
    private readonly ILogger<PosterOverlaysController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PosterOverlaysController"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public PosterOverlaysController(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        ILocalizationManager localization,
        ILogger<PosterOverlaysController> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _localization = localization;
        _logger = logger;
    }

    /// <summary>
    /// Badges one item now.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">What was done, or would have been done in a dry run.</response>
    /// <response code="404">No such item.</response>
    /// <returns>The outcome.</returns>
    [HttpPost("Apply/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OverlayResultDto>> Apply(
        [FromRoute, Required] Guid itemId,
        CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        var store = OverlayStateStore.Shared(plugin.DataFolderPath);
        var applier = new OverlayApplier(_providerManager, _libraryManager, _logger, plugin.Configuration, store, _localization);
        var outcome = await applier.ApplyAsync(item, cancellationToken).ConfigureAwait(false);

        if (!plugin.Configuration.DryRun)
        {
            store.Flush();
        }

        return new OverlayResultDto
        {
            ItemId = itemId,
            Name = item.Name,
            Outcome = outcome.ToString(),
            DryRun = plugin.Configuration.DryRun,
        };
    }

    /// <summary>
    /// Puts the cached original back on one item.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Whether an original was restored.</response>
    /// <response code="404">No such item.</response>
    /// <returns>The outcome.</returns>
    [HttpPost("Restore/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OverlayResultDto>> Restore(
        [FromRoute, Required] Guid itemId,
        CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        var store = OverlayStateStore.Shared(plugin.DataFolderPath);
        var applier = new OverlayApplier(_providerManager, _libraryManager, _logger, plugin.Configuration, store, _localization);
        bool restored = await applier.RestoreAsync(item, cancellationToken).ConfigureAwait(false);

        if (!plugin.Configuration.DryRun)
        {
            store.Flush();
        }

        return new OverlayResultDto
        {
            ItemId = itemId,
            Name = item.Name,
            Outcome = restored ? nameof(OverlayOutcome.Restored) : nameof(OverlayOutcome.OriginalMissing),
            DryRun = plugin.Configuration.DryRun,
        };
    }

    /// <summary>
    /// Fetches a fresh primary image from the metadata provider for one item and forgets it.
    /// </summary>
    /// <remarks>
    /// The single-item form of the repair task, so a fix can be tried on one poster before it is
    /// let loose on a library.
    /// </remarks>
    /// <param name="itemId">The item id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Whether a fresh image was fetched.</response>
    /// <response code="404">The item does not exist.</response>
    /// <returns>What happened to that one item.</returns>
    [HttpPost("Repair/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OverlayResultDto>> Repair(
        [FromRoute, Required] Guid itemId,
        CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        var store = OverlayStateStore.Shared(plugin.DataFolderPath);
        var applier = new OverlayApplier(_providerManager, _libraryManager, _logger, plugin.Configuration, store, _localization);

        if (plugin.Configuration.DryRun)
        {
            return new OverlayResultDto
            {
                ItemId = itemId,
                Name = item.Name,
                Outcome = "WouldRefetch",
                DryRun = true,
            };
        }

        bool done = await applier.RefetchFromProviderAsync(item, cancellationToken).ConfigureAwait(false);
        store.Flush();

        return new OverlayResultDto
        {
            ItemId = itemId,
            Name = item.Name,
            Outcome = done ? "Refetched" : "NoProviderImage",
            DryRun = false,
        };
    }

    /// <summary>
    /// Lists the presets that ship with the plugin.
    /// </summary>
    /// <remarks>
    /// Served rather than repeated in the settings page's own script. A second copy of the same
    /// table in JavaScript would drift from the one in code the first time a default changes, and
    /// the drift would show up as a preset that looks different once it is duplicated.
    /// </remarks>
    /// <response code="200">The built-in presets.</response>
    /// <returns>The built-ins, in the order they should be listed.</returns>
    [HttpGet("BuiltInPresets")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<Collection<BadgePreset>> BuiltInPresetList() => BuiltInPresets.All();

    /// <summary>
    /// Draws one item with settings that have not been saved, and returns the picture.
    /// </summary>
    /// <remarks>
    /// The settings page used to imitate the badges in SVG. That is a second implementation of the
    /// drawing rules, and it was measurably wrong - it estimated text width as
    /// <c>length * fontSize * 0.62</c> where Skia measures it. This renders through the real path
    /// with the configuration posted from the page, so what is shown is what a run would produce.
    /// <para>
    /// Nothing is written. The configuration in the body is used for this one render and thrown
    /// away; the saved configuration is untouched, which is the whole point of previewing a change
    /// before saving it. The item is likewise not modified - no upload, no state record.
    /// </para>
    /// </remarks>
    /// <param name="itemId">The item to draw.</param>
    /// <param name="config">The settings to draw with, as edited on the page.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The rendered image.</response>
    /// <response code="404">No such item, or it has no primary image.</response>
    /// <returns>The image.</returns>
    [HttpPost("Preview/{itemId}")]
    [Produces("image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Preview(
        [FromRoute, Required] Guid itemId,
        [FromBody, Required] PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        var store = OverlayStateStore.Shared(plugin.DataFolderPath);
        var applier = new OverlayApplier(_providerManager, _libraryManager, _logger, config, store, _localization);
        var result = await applier.RenderPreviewAsync(item, cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            return NotFound();
        }

        // Carried in headers rather than in the body, because the body has to be the image itself
        // for an <img> to show it. Without the note a preview with no badges looks like a failure.
        Response.Headers["X-PosterOverlays-Badges"] = result.BadgeCount.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(result.Note))
        {
            Response.Headers["X-PosterOverlays-Note"] = result.Note;
        }

        return File(result.Bytes, result.MimeType);
    }

    /// <summary>
    /// Suggests items worth previewing, newest first.
    /// </summary>
    /// <remarks>
    /// Opening the page on an empty preview is a poor first impression, and picking any item at
    /// random is nearly as bad: most of the library carries no badge at all, so the preview would
    /// usually show an untouched poster. These are items the plugin already badges, so the picture
    /// shows something.
    /// </remarks>
    /// <param name="limit">How many to return.</param>
    /// <response code="200">Items that currently carry badges.</response>
    /// <returns>Id and name of each.</returns>
    [HttpGet("PreviewCandidates")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<Collection<PreviewCandidateDto>> PreviewCandidates([FromQuery] int limit = 12)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var store = OverlayStateStore.Shared(plugin.DataFolderPath);
        var found = new Collection<PreviewCandidateDto>();

        foreach (string key in store.KnownItemIds())
        {
            if (found.Count >= Math.Clamp(limit, 1, 100))
            {
                break;
            }

            if (!Guid.TryParse(key, out var id))
            {
                continue;
            }

            var item = _libraryManager.GetItemById(id);
            if (item is null || string.IsNullOrEmpty(item.Name))
            {
                continue;
            }

            found.Add(new PreviewCandidateDto
            {
                ItemId = id,
                Name = item.Name,
                ProductionYear = item.ProductionYear,
                Kind = OverlayApplier.TargetOf(item).ToString(),
            });
        }

        return found;
    }

    /// <summary>
    /// Reports how many items the plugin currently looks after.
    /// </summary>
    /// <response code="200">The current state of the plugin.</response>
    /// <returns>How many items are badged, and which modes are on.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<OverlayStatusDto> Status()
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var store = OverlayStateStore.Shared(plugin.DataFolderPath);
        return new OverlayStatusDto
        {
            BadgedItems = store.CountRecords(),
            DryRun = plugin.Configuration.DryRun,
            WatchForImageChanges = plugin.Configuration.WatchForImageChanges,
        };
    }
}
