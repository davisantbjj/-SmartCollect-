namespace SmartCollect.Tests.Application.Common;

using System;
using Xunit;
using SmartCollect.Application.Common;

public class TimeUtilsTests
{
    [Fact]
    public void GetBrazilToday_ShouldReturnDateWithoutTime()
    {
        // Act
        var result = TimeUtils.GetBrazilToday();

        // Assert
        Assert.Equal(result.Date, result);
    }

    [Fact]
    public void GetBrazilNow_ShouldReturnCurrentTimeInBrazil()
    {
        // Act
        var result = TimeUtils.GetBrazilNow();
        var utcNow = DateTime.UtcNow;

        // The time difference between Brazil and UTC is typically -3 hours (no DST since 2019)
        // or -4 in some specific regions, but America/Sao_Paulo is -3.
        // We allow some flexibility for execution time.
        var diff = utcNow - result;
        
        // Assert
        Assert.True(diff.TotalHours > 2 && diff.TotalHours < 4, "TimeUtils should correctly offset UTC time to Brazilian time");
    }
}
