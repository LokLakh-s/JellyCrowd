using System;
using System.Linq;
using Jellyfin.Plugin.JellyCrowd.Services;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Services;

/// <summary>
/// Tests for <see cref="LibraryAccessResolver"/>.
/// </summary>
public class LibraryAccessResolverTests
{
  [Fact]
  public void KeepsOnlyRequestedIdsThatExist()
  {
    var a = Guid.NewGuid();
    var b = Guid.NewGuid();
    var missing = Guid.NewGuid();
    var existing = new[] { a.ToString(), b.ToString() };

    var result = LibraryAccessResolver.ResolveEnabledFolders(
      new[] { a.ToString(), missing.ToString() },
      existing);

    Assert.Equal(new[] { a }, result); // 'missing' is dropped, 'b' is not requested
  }

  [Fact]
  public void DeduplicatesAndPreservesFirstSeenOrder()
  {
    var a = Guid.NewGuid();
    var b = Guid.NewGuid();
    var existing = new[] { a.ToString(), b.ToString() };

    var result = LibraryAccessResolver.ResolveEnabledFolders(
      new[] { b.ToString(), a.ToString(), b.ToString() },
      existing);

    Assert.Equal(new[] { b, a }, result);
  }

  [Fact]
  public void IgnoresNonGuidStrings()
  {
    var a = Guid.NewGuid();
    var result = LibraryAccessResolver.ResolveEnabledFolders(
      new[] { "not-a-guid", a.ToString() },
      new[] { a.ToString(), "also-bad" });

    Assert.Equal(new[] { a }, result);
  }

  [Fact]
  public void EmptyOrNullInputs_ReturnEmpty()
  {
    Assert.Empty(LibraryAccessResolver.ResolveEnabledFolders(null, null));
    Assert.Empty(LibraryAccessResolver.ResolveEnabledFolders(Array.Empty<string>(), new[] { Guid.NewGuid().ToString() }));
    Assert.Empty(LibraryAccessResolver.ResolveEnabledFolders(new[] { Guid.NewGuid().ToString() }, Array.Empty<string>()));
  }
}
