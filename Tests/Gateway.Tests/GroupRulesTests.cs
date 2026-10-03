using System.ComponentModel.DataAnnotations;
using Gateway.Areas.Admin.Models.Groups;
using Models;
using Xunit;

namespace Gateway.Tests;

public class GroupRulesTests
{
    // ------------------------------------------- name auto-prefixed correctly

    [Theory]
    [InlineData("HR Portal", "Admins", "HR Portal-Admins")]
    [InlineData("Payroll", "Auditors", "Payroll-Auditors")]
    [InlineData("  HR Portal ", "  Admins  ", "HR Portal-Admins")]          // both trimmed
    [InlineData("HR Portal", "HR Portal-Admins", "HR Portal-Admins")]       // not doubled
    [InlineData("HR Portal", "hr portal-Admins", "HR Portal-Admins")]       // not doubled, any casing
    [InlineData("HR", "HR-Ops-Lead", "HR-Ops-Lead")]                        // dashes elsewhere in the name are fine
    [InlineData("HR", "Ops-HR", "HR-Ops-HR")]                               // only a LEADING prefix is stripped
    public void BuildName_ProducesAppNameDashGroupName(string appName, string typed, string expected)
    {
        Assert.Equal(expected, GroupRules.BuildName(appName, typed));
    }

    [Fact]
    public void ShortName_RemovesThePrefix_SoEditFormsShowWhatTheAdminTyped()
    {
        Assert.Equal("Admins", GroupRules.ShortName("HR Portal", "HR Portal-Admins"));
        Assert.Equal("Admins", GroupRules.ShortName("HR Portal", "Admins"));
        Assert.Equal(string.Empty, GroupRules.ShortName("HR Portal", "HR Portal-"));
        Assert.Equal(string.Empty, GroupRules.ShortName("HR Portal", null));
    }

    [Fact]
    public void ReplacePrefix_SwapsTheAppNamePart_AndLeavesOtherNamesAlone()
    {
        Assert.Equal("People-Admins", GroupRules.ReplacePrefix("HR", "People", "HR-Admins"));
        Assert.Equal("People-Admins", GroupRules.ReplacePrefix("hr", "People", "HR-Admins"));
        Assert.Equal("Other-Admins", GroupRules.ReplacePrefix("HR", "People", "Other-Admins"));
    }

    // ------------------------------------------------- level validation works

    [Theory]
    [InlineData(0, true)]                              // highest power
    [InlineData(1, true)]
    [InlineData(GroupRules.MaxPowerLevel, true)]       // least power allowed
    [InlineData(-1, false)]
    [InlineData(GroupRules.MaxPowerLevel + 1, false)]
    [InlineData(int.MinValue, false)]
    [InlineData(int.MaxValue, false)]
    public void IsValidPowerLevel_AcceptsZeroToMax_AndNothingElse(int level, bool expected)
    {
        Assert.Equal(expected, GroupRules.IsValidPowerLevel(level));
    }

    [Fact]
    public void ZeroIsTheHighestPower_SoTheMinimumIsZero()
    {
        Assert.Equal(0, GroupRules.MinPowerLevel);
        Assert.True(GroupRules.MaxPowerLevel > GroupRules.MinPowerLevel);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(100, true)]
    [InlineData(-1, false)]
    [InlineData(101, false)]
    [InlineData(null, false)]   // blank must be an error, never an implicit 0
    public void FormModel_ValidationAttributes_EnforceTheLevelRange(int? level, bool shouldBeValid)
    {
        var model = new GroupFormViewModel { TenantAppId = 1, GroupName = "Team", PowerLevel = level };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

        Assert.Equal(shouldBeValid, valid);
        if (!shouldBeValid)
        {
            Assert.Contains(results, r => r.MemberNames.Contains(nameof(GroupFormViewModel.PowerLevel)));
        }
    }

    [Fact]
    public void MaxNameLength_MatchesTheDatabaseColumn()
    {
        // SsoDbContext maps Groups.Name with HasMaxLength(100).
        Assert.Equal(100, GroupRules.MaxNameLength);
    }
}
