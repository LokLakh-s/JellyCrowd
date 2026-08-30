using System;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellyCrowd.Api;
using Jellyfin.Plugin.JellyCrowd.Configuration;
using Jellyfin.Plugin.JellyCrowd.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.JellyCrowd.Tests.Api;

/// <summary>
/// Tests for <see cref="GroupsController"/>.
/// </summary>
public class GroupsControllerTests
{
  private static GroupsController Create(PluginConfiguration config, IUserManager userManager, ILibraryManager libraryManager)
    => new(() => config, userManager, libraryManager, NullLogger<GroupsController>.Instance);

  [Fact]
  public void GetGroups_ReturnsSummariesAndAnnouncementTargets()
  {
    var group = new UserGroup { Id = Guid.NewGuid(), Name = "Family" };
    group.Members.Add(Guid.NewGuid());
    group.Members.Add(Guid.NewGuid());
    var config = new PluginConfiguration();
    config.UserGroups.Add(group);
    config.AnnouncementGroupIds.Add(group.Id);

    var controller = Create(config, Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>());
    var ok = Assert.IsType<OkObjectResult>(controller.GetGroups().Result);
    var dto = Assert.IsType<GroupsAdminDto>(ok.Value);

    var summary = Assert.Single(dto.Groups);
    Assert.Equal(group.Id, summary.Id);
    Assert.Equal("Family", summary.Name);
    Assert.Equal(2, summary.MemberCount);
    Assert.Equal(group.Id, Assert.Single(dto.AnnouncementGroupIds));
  }

  [Fact]
  public async Task ApplyLibraryAccess_UnknownGroup_ReturnsNotFound()
  {
    var controller = Create(new PluginConfiguration(), Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>());
    var result = await controller.ApplyLibraryAccess(Guid.NewGuid());
    Assert.IsType<NotFoundResult>(result.Result);
  }

  [Fact]
  public async Task ApplyLibraryAccess_SetsExactLibraries_SkipsMissingUsersAndStaleLibraries()
  {
    var libA = Guid.NewGuid();
    var libB = Guid.NewGuid();
    var staleLib = Guid.NewGuid();
    var member = Guid.NewGuid();
    var missingMember = Guid.NewGuid();

    var group = new UserGroup { Id = Guid.NewGuid(), Name = "G" };
    group.Members.Add(member);
    group.Members.Add(missingMember);
    group.LibraryIds.Add(libA.ToString());
    group.LibraryIds.Add(staleLib.ToString()); // no longer a real library → dropped
    var config = new PluginConfiguration();
    config.UserGroups.Add(group);

    var library = new Mock<ILibraryManager>();
    library.Setup(m => m.GetVirtualFolders()).Returns(new System.Collections.Generic.List<VirtualFolderInfo>
    {
      new VirtualFolderInfo { Name = "Movies", ItemId = libA.ToString() },
      new VirtualFolderInfo { Name = "Shows", ItemId = libB.ToString() },
    });

    var users = new Mock<IUserManager>();
    var user = new User("member", "Prov", "Prov");
    users.Setup(m => m.GetUserById(member)).Returns(user);
    users.Setup(m => m.GetUserById(missingMember)).Returns((User?)null);
    users.Setup(m => m.GetUserDto(user, It.IsAny<string>()))
      .Returns(new UserDto { Policy = new UserPolicy { EnableAllFolders = true, EnableContentDeletion = true } });
    UserPolicy? captured = null;
    users.Setup(m => m.UpdatePolicyAsync(member, It.IsAny<UserPolicy>()))
      .Callback<Guid, UserPolicy>((_, p) => captured = p)
      .Returns(Task.CompletedTask);

    var controller = Create(config, users.Object, library.Object);
    var result = await controller.ApplyLibraryAccess(group.Id);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var dto = Assert.IsType<ApplyLibraryAccessResultDto>(ok.Value);
    Assert.Equal(1, dto.Applied);
    Assert.Equal(1, dto.Skipped);
    Assert.Equal(2, dto.Total);
    Assert.Equal(1, dto.Libraries);

    Assert.NotNull(captured);
    Assert.False(captured!.EnableAllFolders);
    Assert.Equal(new[] { libA }, captured.EnabledFolders);
    Assert.True(captured.EnableContentDeletion); // other policy fields are preserved
    users.Verify(m => m.UpdatePolicyAsync(member, It.IsAny<UserPolicy>()), Times.Once);
    users.Verify(m => m.UpdatePolicyAsync(missingMember, It.IsAny<UserPolicy>()), Times.Never);
  }
}
