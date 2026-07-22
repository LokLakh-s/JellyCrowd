using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="EmailTemplate"/>.
/// </summary>
public class EmailTemplateTests
{
  private static readonly System.Func<string, string> En = ServerStrings.For("en");

  private static RequestRecord Show() => new()
  {
    TmdbId = 94997,
    MediaType = "tv",
    Title = "House of the Dragon",
    Season = 3,
    PosterPath = "/z2yahl2uefxDCl0nogcRBstwruJ.jpg"
  };

  private static RequestRecord Movie() => new()
  {
    TmdbId = 27205,
    MediaType = "movie",
    Title = "Inception",
    PosterPath = "/oYuLEt3zVCKq57qu2F8dT7NIa6f.jpg"
  };

  [Fact]
  public void BuildRequest_RendersTheCard()
  {
    var (html, text) = EmailTemplate.BuildRequest(
      Show(), NotificationEvent.Available, "Now available: House of the Dragon (Season 3)",
      "\"House of the Dragon\" is now available in the library.", "Dragons, and family.", "/poster.jpg", "Torinou", showRequestedBy: true, En);

    Assert.StartsWith("<!DOCTYPE html>", html, System.StringComparison.Ordinal);
    Assert.Contains("House of the Dragon", html, System.StringComparison.Ordinal);
    Assert.Contains("https://image.tmdb.org/t/p/w300/poster.jpg", html, System.StringComparison.Ordinal);
    // HtmlEncode turns the separator into a numeric entity, which is what the client renders as "·".
    Assert.Contains("Show &#183; Season 3", html, System.StringComparison.Ordinal);
    Assert.Contains("Show · Season 3", text, System.StringComparison.Ordinal);
    Assert.Contains("Available", html, System.StringComparison.Ordinal);
    Assert.Contains("Dragons, and family.", html, System.StringComparison.Ordinal);
    Assert.Contains("Requested by", html, System.StringComparison.Ordinal);
    Assert.Contains("Torinou", html, System.StringComparison.Ordinal);
    Assert.Contains("https://www.themoviedb.org/tv/94997", html, System.StringComparison.Ordinal);

    // The plain-text alternative carries the same facts without any markup.
    Assert.DoesNotContain('<', text);
    Assert.Contains("House of the Dragon", text, System.StringComparison.Ordinal);
    Assert.Contains("Requested by: Torinou", text, System.StringComparison.Ordinal);
    Assert.Contains("https://www.themoviedb.org/tv/94997", text, System.StringComparison.Ordinal);
  }

  [Fact]
  public void BuildRequest_EscapesUntrustedText()
  {
    // Titles, synopses and user names come from TMDB and from users; none of them may inject markup.
    var request = Movie();
    request.Title = "<script>alert(1)</script> & \"quoted\"";

    var (html, _) = EmailTemplate.BuildRequest(
      request, NotificationEvent.Created, "New request", "A new movie request is pending approval.",
      "</td></tr></table><b>nope</b>", "/ok.jpg", "<img src=x onerror=alert(1)>", showRequestedBy: true, En);

    Assert.DoesNotContain("<script>", html, System.StringComparison.Ordinal);
    Assert.DoesNotContain("<b>nope</b>", html, System.StringComparison.Ordinal);
    Assert.DoesNotContain("onerror=alert(1)>", html, System.StringComparison.Ordinal);
    Assert.Contains("&lt;script&gt;", html, System.StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("poster.jpg")]                       // no leading slash
  [InlineData("//evil.example.com/x.jpg")]         // protocol-relative host
  [InlineData("/x.jpg\" onerror=\"alert(1)")]      // attribute break-out
  [InlineData("/x.jpg?a=b")]                       // query string
  [InlineData("")]
  public void BuildRequest_DropsAPosterPathThatIsNotATmdbPath(string posterPath)
  {
    var (html, _) = EmailTemplate.BuildRequest(
      Movie(), NotificationEvent.Created, "New request", "Pending approval.", null, posterPath, "Torinou", showRequestedBy: true, En);

    Assert.DoesNotContain("<img", html, System.StringComparison.Ordinal);
    Assert.DoesNotContain("image.tmdb.org", html, System.StringComparison.Ordinal);
    // The card still renders, just without artwork.
    Assert.Contains("Inception", html, System.StringComparison.Ordinal);
  }

  [Fact]
  public void BuildRequest_ForTheRequester_OmitsTheRequestedByLine()
  {
    var (html, text) = EmailTemplate.BuildRequest(
      Movie(), NotificationEvent.Approved, "Request approved: Inception", "The movie request \"Inception\" was approved.",
      null, null, "Torinou", showRequestedBy: false, En);

    Assert.DoesNotContain("Requested by", html, System.StringComparison.Ordinal);
    Assert.DoesNotContain("Torinou", html, System.StringComparison.Ordinal);
    Assert.DoesNotContain("Requested by", text, System.StringComparison.Ordinal);
  }

  [Fact]
  public void BuildRequest_MovieWithoutSeason_ShowsNoSeason()
  {
    var (html, _) = EmailTemplate.BuildRequest(
      Movie(), NotificationEvent.Created, "New request: Inception", "A new movie request is pending approval: Inception.",
      null, null, "Torinou", showRequestedBy: true, En);

    Assert.Contains(">Movie<", html, System.StringComparison.Ordinal);
    Assert.DoesNotContain("Season", html, System.StringComparison.Ordinal);
  }

  [Fact]
  public void BuildRequest_UsesTheEventAccent()
  {
    var available = EmailTemplate.BuildRequest(Show(), NotificationEvent.Available, "s", "b", null, null, "u", true, En).Html;
    var denied = EmailTemplate.BuildRequest(Show(), NotificationEvent.Denied, "s", "b", null, null, "u", true, En).Html;
    var failed = EmailTemplate.BuildRequest(Show(), NotificationEvent.Failed, "s", "b", null, null, "u", true, En).Html;

    Assert.Contains("#10B981", available, System.StringComparison.Ordinal); // green, as on Discord
    Assert.Contains("#EF4444", denied, System.StringComparison.Ordinal);    // red, as on Discord
    Assert.Contains("#F59E0B", failed, System.StringComparison.Ordinal);    // amber: needs a human, not "all good" blue
    Assert.Contains("Needs attention", failed, System.StringComparison.Ordinal);
  }

  [Fact]
  public void BuildNotice_WithoutMedia_RendersAPlainCard()
  {
    var (html, text) = EmailTemplate.BuildNotice("Jelly Crowd test notification", "If you can read this, the channel works.", null, null, En);

    Assert.Contains("If you can read this, the channel works.", html, System.StringComparison.Ordinal);
    Assert.Contains("Jelly&nbsp;Crowd", html, System.StringComparison.Ordinal);
    Assert.DoesNotContain("<img", html, System.StringComparison.Ordinal);
    Assert.DoesNotContain("themoviedb.org", html, System.StringComparison.Ordinal);
    Assert.Contains("If you can read this, the channel works.", text, System.StringComparison.Ordinal);
  }

  [Fact]
  public void BuildNotice_WithMedia_ShowsThePoster()
  {
    var (html, _) = EmailTemplate.BuildNotice("Deletion scheduled", "It leaves the library in 3 days.", "Inception", "/ok.jpg", En);

    Assert.Contains("https://image.tmdb.org/t/p/w300/ok.jpg", html, System.StringComparison.Ordinal);
    Assert.Contains("Inception", html, System.StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("jellycrowd@example.com", "Jelly Crowd")]
  [InlineData("Media Butler <butler@example.com>", "Media Butler")]
  public void Sender_KeepsAConfiguredNameAndFallsBackToTheBrand(string configured, string expectedName)
  {
    var sender = EmailTemplate.Sender(configured);

    Assert.Equal(expectedName, sender.Name);
    Assert.Contains("@", sender.Address, System.StringComparison.Ordinal);
  }
}
