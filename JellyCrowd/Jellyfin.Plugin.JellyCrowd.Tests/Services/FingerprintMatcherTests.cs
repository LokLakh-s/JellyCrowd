using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="FingerprintMatcher"/> against real Chromaprint fingerprints captured from the first
/// six minutes of Devil May Cry S01 and Rick &amp; Morty S09 episodes. These reproduce the probe findings:
/// R&amp;M shares a ~32s intro across every episode; DMC E02/E03 share the ~78s OP while the premiere (E01)
/// shares nothing but the studio logo, so it must get no intro.
/// </summary>
public class FingerprintMatcherTests
{
  // ~8.02 fingerprint frames/sec (2886 frames over the 360s captured window).
  private const double Rate = 2886.0 / 360.0;

  private static uint[] Load(string fixture)
  {
    var path = Path.Combine(AppContext.BaseDirectory, "TestData", "SkipIntro", fixture);
    var bytes = File.ReadAllBytes(path);
    var fp = new uint[bytes.Length / 4];
    Buffer.BlockCopy(bytes, 0, fp, 0, fp.Length * 4);
    return fp;
  }

  private static double Seconds(int frames) => frames / Rate;

  [Fact]
  public void FindSharedRegion_RickAndMorty_FindsTheThirtySecondIntro()
  {
    var e01 = Load("fp_rm_e01.bin");
    var e02 = Load("fp_rm_e02.bin");

    var match = FingerprintMatcher.FindSharedRegion(e01, e02);

    Assert.NotNull(match);
    Assert.InRange(Seconds(match!.Value.Length), 28, 36);
    // Intro sits at a different absolute position in each episode (variable cold opens).
    Assert.InRange(Seconds(match.Value.StartA), 55, 75);   // ~64s in E01
    Assert.InRange(Seconds(match.Value.StartB), 100, 120);  // ~108s in E02
  }

  [Fact]
  public void FindSharedRegion_DevilMayCry_FindsTheEightySecondOp_ForEpisodesThatHaveIt()
  {
    var match = FingerprintMatcher.FindSharedRegion(Load("fp_dmc_e02.bin"), Load("fp_dmc_e03.bin"));

    Assert.NotNull(match);
    Assert.InRange(Seconds(match!.Value.Length), 70, 85);
  }

  [Fact]
  public void FindSharedRegion_DevilMayCryPremiere_SharesNoIntro()
  {
    // E01 shares only the ~4s studio logo with the others — below the minimum run, so: no intro.
    Assert.Null(FingerprintMatcher.FindSharedRegion(Load("fp_dmc_e01.bin"), Load("fp_dmc_e02.bin")));
    Assert.Null(FingerprintMatcher.FindSharedRegion(Load("fp_dmc_e01.bin"), Load("fp_dmc_e03.bin")));
  }

  [Fact]
  public void FindSeasonIntros_RickAndMorty_GivesEveryEpisodeAnIntro()
  {
    var season = new IReadOnlyList<uint>[] { Load("fp_rm_e01.bin"), Load("fp_rm_e02.bin"), Load("fp_rm_e03.bin") };

    var intros = FingerprintMatcher.FindSeasonIntros(season);

    Assert.All(intros, intro =>
    {
      Assert.NotNull(intro);
      Assert.InRange(Seconds(intro!.Value.End - intro.Value.Start), 28, 36);
    });
  }

  [Fact]
  public void FindSeasonIntros_DevilMayCry_SkipsThePremiereButFindsTheRest()
  {
    var season = new IReadOnlyList<uint>[] { Load("fp_dmc_e01.bin"), Load("fp_dmc_e02.bin"), Load("fp_dmc_e03.bin") };

    var intros = FingerprintMatcher.FindSeasonIntros(season);

    Assert.Null(intros[0]);      // premiere: no standard OP
    Assert.NotNull(intros[1]);
    Assert.NotNull(intros[2]);
    Assert.InRange(Seconds(intros[1]!.Value.End - intros[1]!.Value.Start), 70, 85);
    Assert.InRange(Seconds(intros[2]!.Value.End - intros[2]!.Value.Start), 70, 85);
  }

  [Fact]
  public void FindSharedRegion_ReturnsNull_ForEmptyInput()
  {
    Assert.Null(FingerprintMatcher.FindSharedRegion(Array.Empty<uint>(), Load("fp_rm_e01.bin")));
  }
}
