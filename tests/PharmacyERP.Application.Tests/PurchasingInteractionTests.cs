using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using Xunit;

namespace PharmacyERP.Application.Tests;

public partial class PurchasingWorkspaceTests
{
    [Fact]
    public async Task WorkspaceRefresh_CannotQueryWhileAnInlineEditorIsUsingItsContext()
    {
        await using var db = TestDb.CreateContext(_clock); await SeedAsync(db);
        var workspace = Workspace(db); await workspace.InitializeAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = workspace.RunEditorDialogAsync(async () => { started.SetResult(); await finish.Task; });
        await started.Task;
        workspace.CanInteract.Should().BeFalse(); workspace.RefreshCommand.CanExecute(null).Should().BeFalse();
        db.Suppliers.Add(new Supplier { Code = "SECOND", Name = "مذخر ثان" }); await db.SaveChangesAsync();
        await workspace.RefreshAsync(); workspace.Suppliers.Should().HaveCount(1);
        var secondEditorOpened = false;
        await workspace.RunEditorDialogAsync(() => { secondEditorOpened = true; return Task.CompletedTask; });
        secondEditorOpened.Should().BeFalse();
        finish.SetResult(); await operation;
        workspace.CanInteract.Should().BeTrue(); await workspace.RefreshAsync(); workspace.Suppliers.Should().HaveCount(2);
    }
}
