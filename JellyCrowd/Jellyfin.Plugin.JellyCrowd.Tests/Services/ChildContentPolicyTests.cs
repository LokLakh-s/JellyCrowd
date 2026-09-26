using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Models;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="ChildContentPolicy"/>.
/// </summary>
public class ChildContentPolicyTests
{
  [Theory]
  [InlineData("FR", 0, "FR", "U")]
  [InlineData("FR", 12, "FR", "12")]
  [InlineData("US", 12, "US", "PG-13")]
  [InlineData("GB", 16, "GB", "15")]
  [InlineData("DE", 10, "DE", "6")]
  public void CertificationFor_KnownCountry_MapsTier(string country, int age, string expectedCountry, string expectedCert)
  {
    var result = ChildContentPolicy.CertificationFor(country, age);
    Assert.NotNull(result);
    Assert.Equal(expectedCountry, result!.Value.Country);
    Assert.Equal(expectedCert, result.Value.Certification);
  }

  [Theory]
  [InlineData("JP")]   // not in the table
  [InlineData(null)]   // unspecified
  [InlineData("")]
  public void CertificationFor_UnknownCountry_FallsBackToUs(string? country)
  {
    var result = ChildContentPolicy.CertificationFor(country, 12);
    Assert.NotNull(result);
    Assert.Equal("US", result!.Value.Country);
    Assert.Equal("PG-13", result.Value.Certification);
  }

  [Fact]
  public void CertificationFor_BucketsArbitraryAgesToTiers()
  {
    Assert.Equal("G", ChildContentPolicy.CertificationFor("US", 0)!.Value.Certification);   // all ages
    Assert.Equal("PG", ChildContentPolicy.CertificationFor("US", 8)!.Value.Certification);  // → 10 tier
    Assert.Equal("PG-13", ChildContentPolicy.CertificationFor("US", 11)!.Value.Certification); // → 12 tier
    Assert.Equal("R", ChildContentPolicy.CertificationFor("US", 99)!.Value.Certification);  // → 16 tier
  }

  [Fact]
  public void VisibleGenres_HidesHorror_KeepsTheRest()
  {
    var genres = new List<Genre>
    {
      new() { Id = 18, Name = "Drama" },
      new() { Id = 27, Name = "Horror" },
      new() { Id = 53, Name = "Thriller" }
    };

    Assert.Equal(new[] { 18, 53 }, ChildContentPolicy.VisibleGenres(genres).Select(g => g.Id));
  }

  [Theory]
  [InlineData("27", null)]
  [InlineData("27,18", "18")]
  [InlineData(" 18 , 27 ,53", "18,53")]
  [InlineData("18", "18")]
  [InlineData("", null)]
  [InlineData(null, null)]
  public void AllowedGenres_DropsHorror_EvenFromAHandMadeRequest(string? genres, string? expected)
  {
    Assert.Equal(expected, ChildContentPolicy.AllowedGenres(genres));
  }
}
