using System;
using System.Globalization;
using Jellyfin.Plugin.JellyCrowd.Models;

namespace Jellyfin.Plugin.JellyCrowd.Services;

/// <summary>
/// Builds the subject and body text for request notifications. Pure and network-free so it can be
/// unit tested independently of the delivery channels.
/// </summary>
public static class NotificationMessages
{
  /// <summary>
  /// Builds the notification subject and body for a request event.
  /// </summary>
  /// <param name="request">The request the notification is about.</param>
  /// <param name="notificationEvent">The lifecycle event.</param>
  /// <returns>A subject/body pair.</returns>
  public static (string Subject, string Body) Build(RequestRecord request, NotificationEvent notificationEvent)
  {
    ArgumentNullException.ThrowIfNull(request);

    var kind = string.Equals(request.MediaType, "tv", StringComparison.Ordinal) ? "show" : "movie";
    var title = request.Season.HasValue
      ? string.Format(CultureInfo.InvariantCulture, "{0} (Season {1})", request.Title, request.Season.Value)
      : request.Title;

    return notificationEvent switch
    {
      NotificationEvent.Created => (
        string.Format(CultureInfo.InvariantCulture, "New request: {0}", title),
        string.Format(CultureInfo.InvariantCulture, "A new {0} request is pending approval: {1}.", kind, title)),
      NotificationEvent.Approved => (
        string.Format(CultureInfo.InvariantCulture, "Request approved: {0}", title),
        string.Format(CultureInfo.InvariantCulture, "The {0} request \"{1}\" was approved.", kind, title)),
      NotificationEvent.Denied => (
        string.Format(CultureInfo.InvariantCulture, "Request denied: {0}", title),
        string.Format(CultureInfo.InvariantCulture, "The {0} request \"{1}\" was denied.", kind, title)),
      NotificationEvent.Available => (
        string.Format(CultureInfo.InvariantCulture, "Now available: {0}", title),
        string.Format(CultureInfo.InvariantCulture, "\"{0}\" is now available in the library.", title)),
      _ => (
        string.Format(CultureInfo.InvariantCulture, "Jelly Crowd: {0}", title),
        title)
    };
  }
}
