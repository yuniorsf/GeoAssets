using FluentAssertions;
using Xunit;

namespace GeoAssets.Tools.SharedUseImport.Tests;

public sealed class LegacySharedUseCsvParserTests
{
    private static Task<IReadOnlyList<LegacySharedUseRow>> Parse(string text, CancellationToken ct = default) =>
        LegacySharedUseCsvParser.ParseAsync(new StringReader(text), ct);

    [Fact]
    public async Task ParseAsync_ReadsColumnsByName_InAnyOrderAndCase_IgnoringExtraColumnsAndBlankLines()
    {
        var rows = await Parse("""
            levdate,COUNT,WireTypeId,OccupantId,poleid,Id,Level
            2020-01-01, 2 ,3,0,40,7,1

            2020-01-02,1,4,5,41,-8,1
            """);

        rows.Should().Equal(
            new LegacySharedUseRow(Id: 7, PoleId: 40, OccupantId: 0, WireTypeId: 3, Count: 2, LineNumber: 2),
            new LegacySharedUseRow(Id: -8, PoleId: 41, OccupantId: 5, WireTypeId: 4, Count: 1, LineNumber: 4));
    }

    [Fact]
    public async Task ParseAsync_HeaderOnly_ReturnsNoRows()
    {
        (await Parse("Id,PoleId,OccupantId,WireTypeId,Count\n")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \nId,PoleId,OccupantId,WireTypeId,Count")]
    public Task ParseAsync_NoHeader_Throws(string text)
    {
        var act = () => Parse(text);

        return act.Should().ThrowAsync<InvalidDataException>().WithMessage("*empty*header*");
    }

    [Fact]
    public Task ParseAsync_RepeatedColumn_Throws()
    {
        var act = () => Parse("Id,PoleId,OccupantId,WireTypeId,Count,id\n");

        return act.Should().ThrowAsync<InvalidDataException>().WithMessage("*'id' more than once*");
    }

    [Theory]
    [InlineData("CopyId")]
    [InlineData("SoId")]
    [InlineData("TaskId")]
    public Task ParseAsync_ShadowTableExport_IsRefused(string shadowColumn)
    {
        var act = () => Parse($"Id,PoleId,OccupantId,WireTypeId,Count,{shadowColumn}\n");

        return act.Should().ThrowAsync<InvalidDataException>().WithMessage("*SharedUsesSO*");
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("PoleId")]
    [InlineData("OccupantId")]
    [InlineData("WireTypeId")]
    [InlineData("Count")]
    public Task ParseAsync_MissingRequiredColumn_Throws(string missing)
    {
        var header = string.Join(',', new[] { "Id", "PoleId", "OccupantId", "WireTypeId", "Count" }.Where(column => !string.Equals(column, missing, StringComparison.Ordinal)));

        var act = () => Parse(header + "\n");

        return act.Should().ThrowAsync<InvalidDataException>().WithMessage($"*no '{missing}' column*");
    }

    [Theory]
    [InlineData("1,2,3,4")]
    [InlineData("1,2,3,4,5,6")]
    public Task ParseAsync_LineWithWrongFieldCount_ThrowsNamingTheLine(string line)
    {
        var act = () => Parse($"Id,PoleId,OccupantId,WireTypeId,Count\n1,2,3,4,5\n{line}\n");

        return act.Should().ThrowAsync<InvalidDataException>().WithMessage("Line 3 has *fields*");
    }

    [Theory]
    [InlineData("x")]
    [InlineData("1.5")]
    [InlineData("")]
    public Task ParseAsync_FieldThatIsNotAWholeNumber_ThrowsNamingLineAndColumn(string count)
    {
        var act = () => Parse($"Id,PoleId,OccupantId,WireTypeId,Count\n1,2,3,4,{count}\n");

        return act.Should().ThrowAsync<InvalidDataException>().WithMessage("Line 2: 'Count'*not a whole number*");
    }

    [Fact]
    public Task ParseAsync_NullReader_Throws()
    {
        var act = () => LegacySharedUseCsvParser.ParseAsync(null!, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ParseAsync_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => Parse("Id,PoleId,OccupantId,WireTypeId,Count\n", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
