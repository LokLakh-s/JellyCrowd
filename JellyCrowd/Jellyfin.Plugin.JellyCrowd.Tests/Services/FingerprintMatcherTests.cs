using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

  // ---------- Season coverage: a real title/credits sequence recurs across the season ----------

  /// <summary>
  /// Builds a deterministic pseudo-random fingerprint. Unrelated values are far apart in Hamming distance,
  /// so two of these never match by accident — a shared run only exists where one is planted.
  /// </summary>
  /// <param name="seed">The generator seed (a different seed means unrelated audio).</param>
  /// <param name="length">How many sub-fingerprints to produce.</param>
  /// <returns>The fingerprint.</returns>
  private static uint[] Noise(uint seed, int length)
  {
    var fp = new uint[length];
    var state = seed * 2654435761u;
    for (var i = 0; i < length; i++)
    {
      state = (state * 1664525u) + 1013904223u;
      fp[i] = state ^ (state >> 13);
    }

    return fp;
  }

  /// <summary>Copies a shared block into a fingerprint, so the two episodes have that stretch in common.</summary>
  /// <param name="fp">The episode fingerprint to plant into.</param>
  /// <param name="block">The shared audio.</param>
  /// <param name="at">Where the shared stretch starts in this episode.</param>
  private static void Plant(uint[] fp, uint[] block, int at)
    => Array.Copy(block, 0, fp, at, block.Length);

  /// <summary>
  /// Builds a season of <paramref name="count"/> unrelated episodes and plants the same 200-frame block
  /// into the first <paramref name="sharing"/> of them.
  /// </summary>
  /// <param name="count">Season length.</param>
  /// <param name="sharing">How many episodes carry the shared sequence.</param>
  /// <returns>The season fingerprints.</returns>
  private static IReadOnlyList<IReadOnlyList<uint>> Season(int count, int sharing)
  {
    var block = Noise(999, 200);
    var season = new List<IReadOnlyList<uint>>(count);
    for (var i = 0; i < count; i++)
    {
      var fp = Noise((uint)(i + 1), 2000);
      if (i < sharing)
      {
        Plant(fp, block, 100 + (i * 10)); // a different cold open in each episode
      }

      season.Add(fp);
    }

    return season;
  }

  [Fact]
  public void FindSeasonIntros_DropsTheSeason_WhenOnlyACoupleOfEpisodesShareASequence()
  {
    // Two episodes out of eight "sharing" a short stretch is a coincidence between quiet passages, not a
    // title sequence — serving it puts a skip button in the middle of a scene (Tales from the Loop S01).
    var season = Season(count: 8, sharing: 2);

    var withoutGuard = FingerprintMatcher.FindSeasonIntros(season, minRunFrames: 120, minSeasonCoveragePercent: 0);
    Assert.Equal(2, withoutGuard.Count(r => r is not null)); // the coincidence is really there...

    var guarded = FingerprintMatcher.FindSeasonIntros(season, minRunFrames: 120, minSeasonCoveragePercent: 50);
    Assert.All(guarded, Assert.Null);                        // ...and the season guard drops all of it.
  }

  [Fact]
  public void FindSeasonIntros_KeepsTheSequence_WhenMostOfTheSeasonSharesIt()
  {
    var intros = FingerprintMatcher.FindSeasonIntros(Season(count: 8, sharing: 6), minRunFrames: 120, minSeasonCoveragePercent: 50);

    Assert.Equal(6, intros.Count(r => r is not null));
    Assert.All(intros.Take(6), r => Assert.InRange(r!.Value.End - r.Value.Start + 1, 190, 210));
    Assert.All(intros.Skip(6), Assert.Null); // the two that genuinely don't have it stay untouched
  }

  [Fact]
  public void FindSeasonIntros_DevilMayCry_SurvivesTheSeasonGuard()
  {
    // Real media: 2 of the 3 episodes carry the OP (66% coverage), so the guard must not touch them.
    var season = new IReadOnlyList<uint>[] { Load("fp_dmc_e01.bin"), Load("fp_dmc_e02.bin"), Load("fp_dmc_e03.bin") };

    var intros = FingerprintMatcher.FindSeasonIntros(season, minSeasonCoveragePercent: 50);

    Assert.Null(intros[0]);
    Assert.NotNull(intros[1]);
    Assert.NotNull(intros[2]);
  }

  [Theory]
  [InlineData(0, 4, 50, false)]   // nothing found
  [InlineData(1, 4, 50, false)]   // 25%
  [InlineData(2, 4, 50, true)]    // exactly at the threshold
  [InlineData(3, 4, 50, true)]
  [InlineData(1, 8, 0, true)]     // 0 disables the check
  public void HasSeasonCoverage_ComparesFoundRegionsAgainstTheThreshold(int found, int total, int percent, bool expected)
  {
    var regions = new (int Start, int End)?[total];
    for (var i = 0; i < found; i++)
    {
      regions[i] = (0, 10);
    }

    Assert.Equal(expected, FingerprintMatcher.HasSeasonCoverage(regions, percent));
  }

  [Fact]
  public void HasSeasonCoverage_IsFalse_ForAnEmptySeason()
    => Assert.False(FingerprintMatcher.HasSeasonCoverage(Array.Empty<(int Start, int End)?>(), 50));

  // ---------- Confirmations must agree on WHERE the sequence is ----------

  [Fact]
  public void FindSeasonIntros_IgnoresSiblingsThatMatchInUnrelatedPlaces()
  {
    // Episode 0 shares one stretch with episode 1 and a different, disjoint stretch with episode 2. Those
    // two siblings do not confirm the same sequence, so neither can be taken as "the intro": averaging
    // them would invent a region that no episode actually contains.
    var e0 = Noise(1, 3000);
    var e1 = Noise(2, 3000);
    var e2 = Noise(3, 3000);
    var early = Noise(101, 250);
    var late = Noise(102, 250);
    Plant(e0, early, 100);
    Plant(e1, early, 400);
    Plant(e0, late, 2000);
    Plant(e2, late, 1500);

    // Both siblings really do share something with episode 0 — they just disagree on where.
    Assert.NotNull(FingerprintMatcher.FindSharedRegion(e0, e1, minRunFrames: 120));
    Assert.NotNull(FingerprintMatcher.FindSharedRegion(e0, e2, minRunFrames: 120));

    var season = new IReadOnlyList<uint>[] { e0, e1, e2 };

    var intros = FingerprintMatcher.FindSeasonIntros(season, minRunFrames: 120, minConfirmations: 2);

    Assert.Null(intros[0]);

    // A single confirmation is still enough to accept ONE of the two places (the guard is about
    // agreement between siblings, not about refusing a lone match).
    Assert.NotNull(FingerprintMatcher.FindSeasonIntros(season, minRunFrames: 120, minConfirmations: 1)[0]);
  }

  [Fact]
  public void FindSeasonIntros_AcceptsASequence_WhenTheSiblingsAgreeOnIt()
  {
    // The same three episodes, but now both siblings carry the SAME stretch: two agreeing confirmations.
    var shared = Noise(101, 250);
    var e0 = Noise(1, 3000);
    var e1 = Noise(2, 3000);
    var e2 = Noise(3, 3000);
    Plant(e0, shared, 100);
    Plant(e1, shared, 400);
    Plant(e2, shared, 700);

    var intros = FingerprintMatcher.FindSeasonIntros(
      new IReadOnlyList<uint>[] { e0, e1, e2 },
      minRunFrames: 120,
      minConfirmations: 2);

    Assert.NotNull(intros[0]);
    Assert.InRange(intros[0]!.Value.Start, 95, 105);
  }
}
