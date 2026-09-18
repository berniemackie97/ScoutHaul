using HaulScout.Application.Common;
using HaulScout.Application.Imports;

namespace HaulScout.Tests;

/// <summary>The two primitives every import leans on: reading a date, and telling a change from none.</summary>
public sealed class ImportPrimitivesTests
{
    [Fact]
    public void Reads_eBay_s_own_date_format_with_its_zone()
    {
        // Straight from a real Active Listings export. The general parser could not read this, so every
        // eBay listing's start and end date was silently dropped.
        Assert.Equal(new DateTimeOffset(2026, 6, 10, 20, 11, 11, TimeSpan.Zero), CsvTable.ParseDate("Jun-10-26 13:11:11 PDT"));
        Assert.Equal(new DateTimeOffset(2026, 1, 10, 21, 11, 11, TimeSpan.Zero), CsvTable.ParseDate("Jan-10-26 13:11:11 PST"));
    }

    [Fact]
    public void Reads_eBay_s_date_only_format()
    {
        Assert.Equal(new DateTimeOffset(2026, 7, 8, 0, 0, 0, TimeSpan.Zero), CsvTable.ParseDate("Jul-08-26"));
    }

    [Fact]
    public void Always_returns_UTC()
    {
        // The database refuses to store any other offset; one such value used to fail a whole import.
        DateTimeOffset? parsed = CsvTable.ParseDate("2026-07-01T09:30:00-07:00");

        Assert.Equal(TimeSpan.Zero, parsed!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 16, 30, 0, TimeSpan.Zero), parsed);
    }

    [Fact]
    public void Leaves_a_time_in_an_unknown_zone_empty_rather_than_wrong()
    {
        Assert.Null(CsvTable.ParseDate("Jun-10-26 13:11:11 XYZ"));
    }

    [Fact]
    public void Treats_blank_as_no_date()
    {
        Assert.Null(CsvTable.ParseDate("  "));
    }

    [Fact]
    public void Money_is_compared_to_the_cent()
    {
        // 40 parsed from "$40" and 40.00 read back from a numeric column are the same amount.
        Assert.Equal(ImportFingerprint.Of(40m), ImportFingerprint.Of(40.00m));
        Assert.NotEqual(ImportFingerprint.Of(40m), ImportFingerprint.Of(40.01m));
    }

    [Fact]
    public void Times_are_compared_by_instant()
    {
        Assert.Equal(
            ImportFingerprint.Of(new DateTimeOffset(2026, 7, 1, 9, 30, 0, TimeSpan.FromHours(-7))),
            ImportFingerprint.Of(new DateTimeOffset(2026, 7, 1, 16, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Json_is_compared_by_meaning_not_by_spelling()
    {
        // What was written, and what Postgres hands back from a jsonb column.
        const string written = "{\"paymentsFee\":1.60,\"depopFee\":0}";
        const string readBack = "{\"depopFee\": 0, \"paymentsFee\": 1.6}";

        Assert.Equal(ImportFingerprint.Json(written), ImportFingerprint.Json(readBack));
        Assert.NotEqual(ImportFingerprint.Json(written), ImportFingerprint.Json("{\"depopFee\":0,\"paymentsFee\":1.7}"));
    }

    [Fact]
    public void Missing_facts_do_not_collide_with_present_ones()
    {
        Assert.NotEqual(ImportFingerprint.Of(null, "a"), ImportFingerprint.Of("a", null));
        Assert.NotEqual(ImportFingerprint.Of("a", "b"), ImportFingerprint.Of("ab"));
    }
}
