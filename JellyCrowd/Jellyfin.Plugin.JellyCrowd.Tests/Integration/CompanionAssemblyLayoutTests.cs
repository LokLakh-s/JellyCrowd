using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;   // GetMetadataReader() is an extension method here
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Integration;

/// <summary>
/// Guards the packaging rules the whole plugin's survival on Jellyfin 12 depends on: the isolated
/// companion ships as <c>lib/*.dll.bin</c>, in BOTH its halves — one compiled against the 10.11 SDK, one
/// against 12.x — carrying no <c>.dll</c> extension anywhere in the plugin folder.
/// <para>
/// Jellyfin enumerates <c>*.dll</c> under a plugin folder and loads every match. The companion implements
/// <c>IMediaSegmentProvider</c> against the 10.11 SDK, and Jellyfin 12 moved those types, so loading it
/// there throws a <see cref="System.TypeLoadException"/> — at which point the host logs "Failed to load
/// assembly ... Disabling plugin" and takes the ENTIRE plugin down (every route 500s and the web shell
/// stops being injected), not just Skip Outro.
/// </para>
/// <para>
/// Two narrower defences were measured against 12.1 and are NOT sufficient, which is why the rule is the
/// extension and why it is asserted here rather than left to the packaging scripts: meta.json's
/// <c>assemblies</c> allowlist is rewritten to <c>[]</c> ("scan everything") when installing from a
/// repository manifest, and the scan is recursive, so a plain subfolder is walked too.
/// </para>
/// </summary>
public sealed class CompanionAssemblyLayoutTests
{
  private const string CompanionAssemblyName = "Jellyfin.Plugin.JellyCrowd.Segments";
  private const string Companion12AssemblyName = "Jellyfin.Plugin.JellyCrowd.Segments12";
  private const string ShippedCompanionFile = CompanionAssemblyName + ".dll.bin";
  private const string ShippedCompanion12File = Companion12AssemblyName + ".dll.bin";

  /// <summary>
  /// The main plugin's build output, which is exactly what every packaging path copies from.
  /// </summary>
  private static string PluginOutputDirectory
  {
    get
    {
      // The test assembly sits in Jellyfin.Plugin.JellyCrowd.Tests/bin/<cfg>/<tfm>; the main plugin's
      // output is the same <cfg>/<tfm> under its own project folder.
      var testOutput = Path.GetDirectoryName(typeof(CompanionAssemblyLayoutTests).Assembly.Location)!;
      var tfm = Path.GetFileName(testOutput);
      var configuration = Path.GetFileName(Path.GetDirectoryName(testOutput)!);
      var repoRoot = Path.GetFullPath(Path.Combine(testOutput, "..", "..", "..", ".."));
      return Path.Combine(repoRoot, "Jellyfin.Plugin.JellyCrowd", "bin", configuration, tfm);
    }
  }

  [Theory]
  [InlineData(ShippedCompanionFile)]
  [InlineData(ShippedCompanion12File)]
  public void BothCompanionHalvesAreBuiltAsShippedNonScannedFiles(string fileName)
  {
    // Both, because a host only ever loads one: if the half for the OTHER major stops being packaged,
    // nothing fails here or in CI — Skip Outro just quietly disappears on that major.
    var expected = Path.Combine(PluginOutputDirectory, "lib", fileName);

    Assert.True(
      File.Exists(expected),
      $"The isolated companion must be built as 'lib/{fileName}' so packaging ships it there. "
      + $"Expected: {expected}");
  }

  [Fact]
  public void NoCompanionDllIsAnywhereInThePluginOutput()
  {
    // Recursive, because Jellyfin's own scan is: a subfolder hides nothing from it.
    string[] strays = Directory.Exists(PluginOutputDirectory)
      ?
      [
        .. Directory.GetFiles(PluginOutputDirectory, CompanionAssemblyName + ".dll", SearchOption.AllDirectories),
        .. Directory.GetFiles(PluginOutputDirectory, Companion12AssemblyName + ".dll", SearchOption.AllDirectories),
      ]
      : [];

    Assert.True(
      strays.Length == 0,
      "The companion must carry no '.dll' extension anywhere under the plugin output: Jellyfin loads every "
      + "*.dll it finds there, and on Jellyfin 12 this one throws a TypeLoadException that disables the "
      + $"whole plugin. Found: {string.Join(", ", strays)}");
  }

  [Fact]
  public void ShippedCompanionStillLoadsAndExposesItsProvider()
  {
    // Renaming the companion away from ".dll" hides it from Jellyfin's scan, and this asserts the other
    // half of that bargain: the file the plugin loads by path is still a real assembly exposing the
    // provider. Without it, packaging could ship a dud and Skip Outro would vanish in silence — the
    // reflection loader swallows every failure by design, so nothing else would ever report it.
    // Only the 10.11 half is loaded here: this test process runs on net9, which cannot load the net10 one.
    var shipped = Path.Combine(PluginOutputDirectory, "lib", ShippedCompanionFile);
    Assert.True(File.Exists(shipped), $"Companion not packaged at {shipped}");

    var assembly = Assembly.LoadFrom(shipped);
    var provider = assembly.GetType(CompanionAssemblyName + ".JellyCrowdSegmentProvider", throwOnError: false);

    Assert.NotNull(provider);
  }

  [Fact]
  public void The12CompanionTargetsTheRuntimeJellyfin12Runs()
  {
    // The whole point of the second half is that it was compiled against the 12 SDK, which ships net10.0
    // only. A net9.0 build here would mean it was silently compiled against the 10.11 references again —
    // it would load on 12 and then fail the same way the 10.11 half does.
    var shipped = Path.Combine(PluginOutputDirectory, "lib", ShippedCompanion12File);
    Assert.True(File.Exists(shipped), $"Companion not packaged at {shipped}");

    using var stream = File.OpenRead(shipped);
    using var peReader = new System.Reflection.PortableExecutable.PEReader(stream);
    var metadata = peReader.GetMetadataReader();
    var targetFramework = metadata.GetAssemblyDefinition().GetCustomAttributes()
      .Select(metadata.GetCustomAttribute)
      .Select(attribute => metadata.GetBlobBytes(attribute.Value))
      .Select(blob => System.Text.Encoding.UTF8.GetString(blob))
      .FirstOrDefault(text => text.Contains(".NETCoreApp,Version=v", StringComparison.Ordinal));

    Assert.NotNull(targetFramework);
    Assert.Contains(".NETCoreApp,Version=v10.0", targetFramework, StringComparison.Ordinal);
  }

  [Fact]
  public void MainAssemblyKeepsNoCompileTimeReferenceToTheCompanion()
  {
    // The isolation only holds while the main assembly reaches the companion purely by reflection: a
    // compile-time reference would make the CLR load it as soon as the registrator runs, reintroducing on
    // Jellyfin 12 exactly the TypeLoadException the subfolder placement is there to avoid.
    var mainAssembly = typeof(PluginServiceRegistrator).Assembly;

    Assert.DoesNotContain(
      mainAssembly.GetReferencedAssemblies(),
      reference => reference.Name == "Jellyfin.Plugin.JellyCrowd.Segments");
  }
}
