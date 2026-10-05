using Ingestion.Gtfs;
using Xunit;

namespace Ingestion.Tests;

public class GtfsTimeTests
{
    [Fact]
    public void ParsesNormalTimes() =>
        Assert.Equal(8 * 3600 + 5 * 60, GtfsTime.ParseSeconds("08:05:00"));

    [Fact]
    public void ParsesHoursAboveTwentyFour() =>
        Assert.Equal(25 * 3600 + 10 * 60, GtfsTime.ParseSeconds("25:10:00"));

    [Fact]
    public void EmptyOrInvalid_ReturnsNull()
    {
        Assert.Null(GtfsTime.ParseSeconds(""));
        Assert.Null(GtfsTime.ParseSeconds(null));
        Assert.Null(GtfsTime.ParseSeconds("8:5"));
        Assert.Null(GtfsTime.ParseSeconds("12:60:00"));
        Assert.Null(GtfsTime.ParseSeconds("ab:cd:ef"));
    }
}

public class BoundingBoxTests
{
    [Fact]
    public void Parse_ReadsFourNumbers() =>
        Assert.Equal(BoundingBox.Default, BoundingBox.Parse("50.7,12.8,51.4,14.3"));

    [Fact]
    public void Parse_RejectsWrongInput()
    {
        Assert.Throws<FormatException>(() => BoundingBox.Parse("1,2,3"));
        Assert.Throws<FormatException>(() => BoundingBox.Parse("51.4,12.8,50.7,14.3")); // min above max
    }

    [Fact]
    public void Contains_ChecksBothAxes()
    {
        Assert.True(BoundingBox.Default.Contains(51.04, 13.74));   // Dresden
        Assert.True(!BoundingBox.Default.Contains(52.52, 13.40));  // Berlin
    }
}