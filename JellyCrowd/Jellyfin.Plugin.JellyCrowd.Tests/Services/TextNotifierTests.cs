using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for the text notification channels (<see cref="ITextNotifier"/> implementations).
/// </summary>
public class TextNotifierTests
{
  [Fact]
  public async Task Slack_PostsTextToWebhook()
  {
    var (handler, factory) = Stub();
    var config = new PluginConfiguration { SlackWebhookUrl = "https://hooks.slack.test/abc" };
    var notifier = new SlackNotifier(factory);

    Assert.True(notifier.IsConfigured(config));
    await notifier.SendAsync(config, "Title", "Body", CancellationToken.None);

    Assert.Equal("https://hooks.slack.test/abc", handler.LastUrl);
    Assert.Contains("Title", handler.LastBody, StringComparison.Ordinal);
    Assert.Contains("Body", handler.LastBody, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Telegram_PostsToBotSendMessage()
  {
    var (handler, factory) = Stub();
    var config = new PluginConfiguration { TelegramBotToken = "TKN", TelegramChatId = "123" };
    var notifier = new TelegramNotifier(factory);

    await notifier.SendAsync(config, "Title", "Body", CancellationToken.None);

    Assert.Equal("https://api.telegram.org/botTKN/sendMessage", handler.LastUrl);
    Assert.Contains("123", handler.LastBody, StringComparison.Ordinal);
    Assert.Contains("Title", handler.LastBody, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Gotify_PostsToMessageWithToken()
  {
    var (handler, factory) = Stub();
    var config = new PluginConfiguration { GotifyServer = "https://gotify.test/", GotifyToken = "APP" };
    var notifier = new GotifyNotifier(factory);

    await notifier.SendAsync(config, "Title", "Body", CancellationToken.None);

    Assert.StartsWith("https://gotify.test/message?token=APP", handler.LastUrl, StringComparison.Ordinal);
    Assert.Contains("Title", handler.LastBody, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Ntfy_DefaultsToPublicServer_AndSetsTitleHeader()
  {
    var (handler, factory) = Stub();
    var config = new PluginConfiguration { NtfyTopic = "mytopic" };
    var notifier = new NtfyNotifier(factory);

    await notifier.SendAsync(config, "Title", "Body", CancellationToken.None);

    Assert.Equal("https://ntfy.sh/mytopic", handler.LastUrl);
    Assert.Equal("Body", handler.LastBody);
    Assert.Equal("Title", handler.LastTitleHeader);
  }

  [Fact]
  public async Task Pushover_PostsFormWithCredentials()
  {
    var (handler, factory) = Stub();
    var config = new PluginConfiguration { PushoverToken = "APP", PushoverUser = "USR" };
    var notifier = new PushoverNotifier(factory);

    await notifier.SendAsync(config, "Title", "Body", CancellationToken.None);

    Assert.Equal("https://api.pushover.net/1/messages.json", handler.LastUrl);
    Assert.Contains("token=APP", handler.LastBody, StringComparison.Ordinal);
    Assert.Contains("user=USR", handler.LastBody, StringComparison.Ordinal);
  }

  [Fact]
  public void IsConfigured_FalseWhenMissing()
  {
    var (_, factory) = Stub();
    var empty = new PluginConfiguration();
    Assert.False(new SlackNotifier(factory).IsConfigured(empty));
    Assert.False(new TelegramNotifier(factory).IsConfigured(empty));
    Assert.False(new GotifyNotifier(factory).IsConfigured(empty));
    Assert.False(new NtfyNotifier(factory).IsConfigured(empty));
    Assert.False(new PushoverNotifier(factory).IsConfigured(empty));
    Assert.False(new WebhookNotifier(factory).IsConfigured(empty));
  }

  private static (StubHandler Handler, IHttpClientFactory Factory) Stub()
  {
    var handler = new StubHandler();
    return (handler, new StubFactory(handler));
  }

  private sealed class StubHandler : HttpMessageHandler
  {
    public string? LastUrl { get; private set; }

    public string LastBody { get; private set; } = string.Empty;

    public string? LastTitleHeader { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      LastUrl = request.RequestUri!.ToString();
      if (request.Headers.TryGetValues("Title", out var titles))
      {
        LastTitleHeader = string.Join(",", titles);
      }

      if (request.Content is not null)
      {
        LastBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
      }

      return new HttpResponseMessage(HttpStatusCode.OK);
    }
  }

  private sealed class StubFactory : IHttpClientFactory
  {
    private readonly HttpMessageHandler _handler;

    public StubFactory(HttpMessageHandler handler) => _handler = handler;

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
  }
}
