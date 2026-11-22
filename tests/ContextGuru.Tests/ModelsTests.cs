using ContextGuru.Core.Models;

namespace ContextGuru.Tests;

public class ModelsTests
{
    [Fact]
    public void ProjectSource_HasThreeValuesInOrder() =>
        Assert.Equal(["Argument", "Roots", "Cwd"], Enum.GetNames<ProjectSource>());

    [Fact]
    public void SaveResult_PreviousFieldsCanBeNull()
    {
        var r = new SaveResult("a", false, null, null);
        Assert.False(r.Overwritten);
        Assert.Null(r.PreviousUpdatedAt);
    }
}
