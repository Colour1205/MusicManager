using MusicManager.Common;
using Xunit;

namespace MusicManager.Tests.Common;

public class JavaStringTest
{
    [Fact]
    public void TrimRemovesControlCharactersLikeJava() => Assert.Equal("a b", JavaString.Trim("\u0001 a b\t\n"));

    [Fact]
    public void SplitDropsTrailingEmptyStringsLikeJava() => Assert.Equal(new[] { "a", "b" }, JavaString.Split("a,b,,", ","));

    [Fact]
    public void SplitKeepsLeadingEmptyStringForARealMatch() => Assert.Equal(new[] { "", "a" }, JavaString.Split(",a", ","));

    [Fact]
    public void SplitWithoutAMatchReturnsTheWholeString() => Assert.Equal(new[] { "abc" }, JavaString.Split("abc", ","));

    [Fact]
    public void SubstringTakesAnEndIndex() => Assert.Equal("bc", JavaString.Substring("abcd", 1, 3));

    [Fact]
    public void UserHomeCanBeOverriddenAndRestored()
    {
        string real = UserHome.Path;
        UserHome.Path = @"C:\temp\x";
        Assert.Equal(@"C:\temp\x", UserHome.Path);
        UserHome.Path = null;
        Assert.Equal(real, UserHome.Path);
    }
}
