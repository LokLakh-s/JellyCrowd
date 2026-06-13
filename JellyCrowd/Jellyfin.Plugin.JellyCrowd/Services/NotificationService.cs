using System;
using System.Globalization;
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
  private readonly ILogger<NotificationService> _logger;

  /// <summary>
  /// Initializes a new instance of the <see cref="NotificationService"/> class.
  /// </summary>
  /// <param name="httpClientFactory">The HTTP client factory (for Discord).</param>
  /// <param name="tmdbClient">The TMDB client, used to enrich notifications with synopsis/poster.</param>
  /// <param name="userManager">The user manager, used to resolve the requesting user's name.</param>
  /// <param name="logger">The logger.</param>
  public NotificationService(
    IHttpClientFactory httpClientFactory,
    ITmdbClient tmdbClient,
    IUserManager userManager,
    ILogger<NotificationService> logger)
  {
    _httpClientFactory = httpClientFactory;
    _tmdbClient = tmdbClient;
    _userManager = userManager;
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
    var details = await TryGetDetailsAsync(request, cancellationToken).ConfigureAwait(false);
    var username = ResolveUserName(request.UserId);

    var embed = NotificationEmbeds.BuildRequest(
      request,
      notificationEvent,
      subject,
      body,
      details?.Overview,
      details?.PosterPath ?? request.PosterPath,
      username,
      DateTime.UtcNow);
    await SendDiscordAsync(config, embed, cancellationToken).ConfigureAwait(false);

    var emailBody = BuildEmailBody(request, body, details, username);
    await SendEmailAsync(config, "[Jelly Crowd] " + subject, emailBody, cancellationToken).ConfigureAwait(false);
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

      await SendEmailCoreAsync(config, "[Jelly Crowd] " + Subject, Body, cancellationToken).ConfigureAwait(false);
    }
    else
    {
      throw new ArgumentException("Unknown notification channel.", nameof(channel));
    }
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
      await SendEmailCoreAsync(config, subject, body, cancellationToken).ConfigureAwait(false);
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

  private static async Task SendEmailCoreAsync(PluginConfiguration config, string subject, string body, CancellationToken cancellationToken)
  {
    using var message = new MimeMessage();
    message.From.Add(MailboxAddress.Parse(config.SmtpFromAddress));
    message.To.Add(MailboxAddress.Parse(config.NotificationEmailTo));
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
