using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Default <see cref="INotificationService"/> delivering to a Discord webhook and/or SMTP email.
/// Discord messages are sent as rich embeds (title, synopsis, colored bar, poster thumbnail and
/// inline fields), mirroring the presentation used by jelly-quotas. Each channel is optional
/// (enabled when configured) and failures are swallowed and logged.
/// </summary>
public sealed class NotificationService : INotificationService
{
  private const int SmtpTimeoutMs = 15000;

  // Notifications are not user-scoped, so enrich with the catalog's default language.
  private const string TmdbLanguage = "en-US";

  private readonly IHttpClientFactory _httpClientFactory;
  private readonly ITmdbClient _tmdbClient;
  private readonly IUserManager _userManager;
  private readonly IReadOnlyList<ITextNotifier> _textNotifiers;
  private readonly IUserNotificationStore _userNotifications;
  private readonly IUserPrefsStore _userPrefs;
  private readonly IActivityLog _activityLog;
  private readonly ILogger<NotificationService> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="NotificationService"/> class.
  /// </summary>
  /// <param name="httpClientFactory">The HTTP client factory (for Discord).</param>
  /// <param name="tmdbClient">The TMDB client, used to enrich notifications with synopsis/poster.</param>
  /// <param name="userManager">The user manager, used to resolve the requesting user's name.</param>
  /// <param name="textNotifiers">The additional text notification channels (Telegram, ntfy, …).</param>
  /// <param name="userNotifications">The per-user in-app notification store (header bell).</param>
  /// <param name="userPrefs">The per-user delivery preferences (personal email / ntfy).</param>
  /// <param name="activityLog">The plugin activity log.</param>
  /// <param name="logger">The logger.</param>
  public NotificationService(
    IHttpClientFactory httpClientFactory,
    ITmdbClient tmdbClient,
    IUserManager userManager,
    IEnumerable<ITextNotifier> textNotifiers,
    IUserNotificationStore userNotifications,
    IUserPrefsStore userPrefs,
    IActivityLog activityLog,
    ILogger<NotificationService> logger)
  {
    _httpClientFactory = httpClientFactory;
    _tmdbClient = tmdbClient;
    _userManager = userManager;
    _textNotifiers = new List<ITextNotifier>(textNotifiers);
    _userNotifications = userNotifications;
    _userPrefs = userPrefs;
    _activityLog = activityLog;
    _logger = logger;
  }

  /// <inheritdoc />
  public async Task NotifyRequestEventAsync(RequestRecord request, NotificationEvent notificationEvent, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(request);

    var config = Plugin.Instance?.Configuration;
    if (config is null)
    {
      return;
    }

    var (subject, body) = NotificationMessages.Build(request, notificationEvent);
    await FanOutAsync(config, request, notificationEvent, subject, body, cancellationToken).ConfigureAwait(false);
  }

  /// <inheritdoc />
  public async Task NotifyAvailableBatchAsync(IReadOnlyList<RequestRecord> requests, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(requests);

    var episodes = requests.Where(r => r.Episode.HasValue).Select(r => r.Episode!.Value).ToList();
    if (episodes.Count < 2)
    {
      // Not a multi-episode set (a movie, a whole show/season, or a single episode) — notify each the
      // normal per-request way.
      foreach (var request in requests)
      {
        await NotifyRequestEventAsync(request, NotificationEvent.Available, cancellationToken).ConfigureAwait(false);
      }

      return;
    }

    var config = Plugin.Instance?.Configuration;
    if (config is null)
    {
      return;
    }

    var (subject, body) = NotificationMessages.BuildAvailableBatch(requests[0], episodes);
    await FanOutAsync(config, requests[0], NotificationEvent.Available, subject, body, cancellationToken).ConfigureAwait(false);
  }

  // Fans a fully-built (subject, body) out to every configured channel: activity log, the requester's
  // in-app bell + personal channels, the Discord embed, the ops mailbox and the text notifiers.
  private async Task FanOutAsync(PluginConfiguration config, RequestRecord request, NotificationEvent notificationEvent, string subject, string body, CancellationToken cancellationToken)
  {
    var details = await TryGetDetailsAsync(request, cancellationToken).ConfigureAwait(false);
    var username = ResolveUserName(request.UserId);

    _ = _activityLog.LogAsync("info", "request", subject + " — " + username, CancellationToken.None);

    await NotifyUserAsync(request, notificationEvent, subject, body, cancellationToken).ConfigureAwait(false);

    if (DiscordEnabledFor(config, notificationEvent))
    {
      var embed = NotificationEmbeds.BuildRequest(
        request,
        notificationEvent,
        subject,
        body,
        details?.Overview,
        details?.PosterPath ?? request.PosterPath,
        username,
        DateTime.UtcNow,
        BuildDiscordOptions(config, notificationEvent));
      await SendDiscordAsync(config, embed, cancellationToken).ConfigureAwait(false);
    }

    if (EmailEnabledFor(config, notificationEvent))
    {
      var emailBody = BuildEmailBody(request, body, details, username);
      await SendEmailAsync(config, "[Jelly Crowd] " + subject, emailBody, cancellationToken).ConfigureAwait(false);
    }

    var textBody = body + "\nRequested by: " + username;
    foreach (var notifier in _textNotifiers)
    {
      if (!notifier.IsConfigured(config))
      {
        continue;
      }

      try
      {
        await notifier.SendAsync(config, subject, textBody, cancellationToken).ConfigureAwait(false);
      }
#pragma warning disable CA1031 // A notification failure must never affect the request flow.
      catch (Exception ex)
#pragma warning restore CA1031
      {
        _logger.LogWarning(ex, "Failed to send {Channel} notification.", notifier.Channel);
      }
    }
  }

  /// <inheritdoc />
  public async Task SendTestAsync(string channel, CancellationToken cancellationToken)
  {
    var config = Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Plugin is not initialized.");
    const string Subject = "Jelly Crowd test notification";
    const string Body = "This is a test notification from Jelly Crowd. If you can read this, the channel works.";

    if (string.Equals(channel, "discord", StringComparison.OrdinalIgnoreCase))
    {
      if (string.IsNullOrWhiteSpace(config.DiscordWebhookUrl))
      {
        throw new InvalidOperationException("The Discord webhook URL is not configured.");
      }

      await SendDiscordCoreAsync(config, NotificationEmbeds.BuildSimple(Subject, Body, NotificationEmbeds.TestColor, DateTime.UtcNow), cancellationToken).ConfigureAwait(false);
    }
    else if (string.Equals(channel, "email", StringComparison.OrdinalIgnoreCase))
    {
      if (string.IsNullOrWhiteSpace(config.SmtpHost)
          || string.IsNullOrWhiteSpace(config.SmtpFromAddress)
          || string.IsNullOrWhiteSpace(config.NotificationEmailTo))
      {
        throw new InvalidOperationException("SMTP is not fully configured (host, from address and recipient are required).");
      }

      await SendEmailCoreAsync(config, config.NotificationEmailTo, "[Jelly Crowd] " + Subject, Body, cancellationToken).ConfigureAwait(false);
    }
    else
    {
      var notifier = _textNotifiers.FirstOrDefault(n => string.Equals(n.Channel, channel, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException("Unknown notification channel.", nameof(channel));
      if (!notifier.IsConfigured(config))
      {
        throw new InvalidOperationException("The " + notifier.Channel + " channel is not configured.");
      }

      await notifier.SendAsync(config, Subject, Body, cancellationToken).ConfigureAwait(false);
    }
  }

  // Notify the requester directly on the state changes they care about: in-app bell + their personal
  // channels (email / ntfy), when configured.
  private async Task NotifyUserAsync(RequestRecord request, NotificationEvent notificationEvent, string subject, string body, CancellationToken cancellationToken)
  {
    if (notificationEvent is not (NotificationEvent.Approved or NotificationEvent.Denied or NotificationEvent.Available or NotificationEvent.Failed))
    {
      return;
    }

    try
    {
      await _userNotifications.AddAsync(
        new UserNotification
        {
          UserId = request.UserId,
          Event = notificationEvent.ToString(),
          Title = request.Title,
          Message = body,
          PosterPath = request.PosterPath
        },
        cancellationToken).ConfigureAwait(false);
    }
#pragma warning disable CA1031 // In-app notification is best-effort; never break the request flow.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Could not store the in-app notification for user {UserId}.", request.UserId);
    }

    var kind = PersonalDelivery.KindFor(notificationEvent, request);
    await DeliverPersonalAsync(request.UserId, kind, subject, body, cancellationToken).ConfigureAwait(false);
  }

  /// <inheritdoc />
  public async Task NotifyPersonalAsync(Guid userId, PersonalNotifyKind kind, string title, string subject, string body, string? posterPath, CancellationToken cancellationToken)
  {
    try
    {
      await _userNotifications.AddAsync(
        new UserNotification
        {
          UserId = userId,
          Event = kind.ToString(),
          Title = title,
          Message = body,
          PosterPath = posterPath
        },
        cancellationToken).ConfigureAwait(false);
    }
#pragma warning disable CA1031 // In-app notification is best-effort.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Could not store the in-app notification for user {UserId}.", userId);
    }

    await DeliverPersonalAsync(userId, kind, subject, body, cancellationToken).ConfigureAwait(false);
  }

  // Deliver to the user's own channels (email / ntfy), each best-effort, gated by their per-category opt-in.
  private async Task DeliverPersonalAsync(Guid userId, PersonalNotifyKind kind, string subject, string body, CancellationToken cancellationToken)
  {
    var config = Plugin.Instance?.Configuration;
    if (config is null)
    {
      return;
    }

    var prefs = await _userPrefs.GetAsync(userId, cancellationToken).ConfigureAwait(false);
    if (!prefs.Enabled || !PersonalDelivery.IsKindEnabled(prefs, kind))
    {
      return;
    }

    if (PersonalDelivery.ShouldEmail(prefs, config))
    {
      try
      {
        await SendEmailCoreAsync(config, prefs.Email!, "[Jelly Crowd] " + subject, body, cancellationToken).ConfigureAwait(false);
      }
#pragma warning disable CA1031 // Personal delivery is best-effort.
      catch (Exception ex)
#pragma warning restore CA1031
      {
        _logger.LogWarning(ex, "Failed to send the personal email notification for user {UserId}.", userId);
      }
    }

    var ntfyUrl = PersonalDelivery.NtfyUrl(prefs, config);
    if (ntfyUrl is not null)
    {
      try
      {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ntfyUrl))
        {
          Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };
        request.Headers.TryAddWithoutValidation("Title", subject);
        if (!string.IsNullOrWhiteSpace(config.NtfyToken))
        {
          request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + config.NtfyToken);
        }

        await NotifierHttp.SendAsync(_httpClientFactory, request, cancellationToken).ConfigureAwait(false);
      }
#pragma warning disable CA1031 // Personal delivery is best-effort.
      catch (Exception ex)
#pragma warning restore CA1031
      {
        _logger.LogWarning(ex, "Failed to send the personal ntfy notification for user {UserId}.", userId);
      }
    }
  }

  private static bool DiscordEnabledFor(PluginConfiguration config, NotificationEvent notificationEvent) => notificationEvent switch
  {
    NotificationEvent.Created => config.DiscordNotifyCreated,
    NotificationEvent.Approved => config.DiscordNotifyApproved,
    NotificationEvent.Denied => config.DiscordNotifyDenied,
    NotificationEvent.Available => config.DiscordNotifyAvailable,
    NotificationEvent.Failed => config.DiscordNotifyDenied,
    _ => true
  };

  // The global ops mailbox is admin oversight, not a per-user channel: gate it per event so it isn't
  // flooded by end-user lifecycle notifications (which already reach the requester's own channels).
  private static bool EmailEnabledFor(PluginConfiguration config, NotificationEvent notificationEvent) => notificationEvent switch
  {
    NotificationEvent.Created => config.EmailNotifyCreated,
    NotificationEvent.Approved => config.EmailNotifyApproved,
    NotificationEvent.Denied => config.EmailNotifyDenied,
    NotificationEvent.Available => config.EmailNotifyAvailable,
    NotificationEvent.Failed => config.EmailNotifyDenied,
    _ => false
  };

  private static DiscordEmbedOptions BuildDiscordOptions(PluginConfiguration config, NotificationEvent notificationEvent)
  {
    var hex = notificationEvent switch
    {
      NotificationEvent.Created => config.DiscordColorCreated,
      NotificationEvent.Approved => config.DiscordColorApproved,
      NotificationEvent.Denied => config.DiscordColorDenied,
      NotificationEvent.Available => config.DiscordColorAvailable,
      NotificationEvent.Failed => config.DiscordColorDenied,
      _ => config.DiscordColorCreated
    };

    return new DiscordEmbedOptions
    {
      Color = NotificationEmbeds.ParseColor(hex, NotificationEmbeds.DefaultColorFor(notificationEvent)),
      ShowPoster = config.DiscordShowPoster,
      ShowSynopsis = config.DiscordShowSynopsis,
      ShowRequestedBy = config.DiscordShowRequestedBy,
      ShowStatus = config.DiscordShowStatus,
      ShowSeason = config.DiscordShowSeason,
      ShowLink = config.DiscordShowLink,
      Mention = string.IsNullOrWhiteSpace(config.DiscordMention) ? null : config.DiscordMention
    };
  }

  private static string BuildEmailBody(RequestRecord request, string body, CatalogItem? details, string username)
  {
    var builder = new StringBuilder();
    builder.AppendLine(body);
    builder.AppendLine();
    builder.Append("Requested by: ").AppendLine(username);
    if (request.Season.HasValue)
    {
      builder.Append("Season: ").AppendLine(request.Season.Value.ToString(CultureInfo.InvariantCulture));
    }

    builder.Append("More info: ").AppendLine(NotificationEmbeds.TmdbUrl(request.MediaType, request.TmdbId));

    if (details is not null && !string.IsNullOrWhiteSpace(details.Overview))
    {
      builder.AppendLine();
      builder.AppendLine(details.Overview);
    }

    return builder.ToString();
  }

  private async Task<CatalogItem?> TryGetDetailsAsync(RequestRecord request, CancellationToken cancellationToken)
  {
    try
    {
      return await _tmdbClient.GetDetailsAsync(request.MediaType, request.TmdbId, TmdbLanguage, cancellationToken).ConfigureAwait(false);
    }
#pragma warning disable CA1031 // Enrichment is best-effort; fall back to the stored request data on any failure.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Could not fetch TMDB details to enrich the notification.");
      return null;
    }
  }

  private string ResolveUserName(Guid userId)
  {
    try
    {
      return _userManager.GetUserById(userId)?.Username ?? "Unknown";
    }
#pragma warning disable CA1031 // Name resolution is best-effort and must not break notification delivery.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogDebug(ex, "Could not resolve the requesting user name.");
      return "Unknown";
    }
  }

  private async Task SendDiscordAsync(PluginConfiguration config, object payload, CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(config.DiscordWebhookUrl))
    {
      return;
    }

    try
    {
      await SendDiscordCoreAsync(config, payload, cancellationToken).ConfigureAwait(false);
    }
#pragma warning disable CA1031 // A notification failure must never affect the request flow.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogWarning(ex, "Failed to send Discord notification.");
    }
  }

  private async Task SendEmailAsync(PluginConfiguration config, string subject, string body, CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(config.SmtpHost)
        || string.IsNullOrWhiteSpace(config.SmtpFromAddress)
        || string.IsNullOrWhiteSpace(config.NotificationEmailTo))
    {
      return;
    }

    try
    {
      await SendEmailCoreAsync(config, config.NotificationEmailTo, subject, body, cancellationToken).ConfigureAwait(false);
    }
#pragma warning disable CA1031 // A notification failure must never affect the request flow.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _logger.LogWarning(ex, "Failed to send email notification.");
    }
  }

  private async Task SendDiscordCoreAsync(PluginConfiguration config, object payload, CancellationToken cancellationToken)
  {
    var client = _httpClientFactory.CreateClient(NamedClient.Default);
    var json = JsonSerializer.Serialize(payload);
    using var content = new StringContent(json, Encoding.UTF8, "application/json");
    using var response = await client.PostAsync(new Uri(config.DiscordWebhookUrl), content, cancellationToken).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
  }

  private static async Task SendEmailCoreAsync(PluginConfiguration config, string to, string subject, string body, CancellationToken cancellationToken)
  {
    using var message = new MimeMessage();
    message.From.Add(MailboxAddress.Parse(config.SmtpFromAddress));
    message.To.Add(MailboxAddress.Parse(to));
    message.Subject = subject;
    message.Body = new TextPart("plain") { Text = body };

    using var client = new SmtpClient { Timeout = SmtpTimeoutMs };
    if (config.SmtpAllowInvalidCertificate)
    {
#pragma warning disable CA5359 // Admin opted in to accept self-signed/invalid certs for their own SMTP server.
      client.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
#pragma warning restore CA5359
    }

    var options = config.SmtpUseSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None;
    await client.ConnectAsync(config.SmtpHost, config.SmtpPort, options, cancellationToken).ConfigureAwait(false);

    if (!string.IsNullOrWhiteSpace(config.SmtpUsername))
    {
      await client.AuthenticateAsync(config.SmtpUsername, config.SmtpPassword, cancellationToken).ConfigureAwait(false);
    }

    await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
    await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
  }
}
