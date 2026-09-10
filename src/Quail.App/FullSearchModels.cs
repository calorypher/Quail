using System.Globalization;
using Quail.Core;

namespace Quail.App;

internal enum FullSearchEntryType { All, Files, Folders }

internal enum FullSearchSortField { Relevance, Name, Path, Size, Modified }

internal enum FullSearchSortDirection { Ascending, Descending }

internal enum FullSearchSizeUnit { KB, MB, GB }

internal sealed record FullSearchCriteria(
    FullSearchEntryType EntryType,
    string? Extension,
    long? MinimumSize,
    long? MaximumSize,
    DateOnly? ModifiedFrom,
    DateOnly? ModifiedTo,
    bool Hidden,
    bool System,
    bool ReadOnly,
    FullSearchSortField SortField,
    FullSearchSortDirection SortDirection);

internal sealed record FullSearchResultFields(
    string? Path,
    bool IsDirectory,
    long? Size,
    long? ModifiedUtcFileTime,
    uint Attributes);

internal static class FullSearchCriteriaFactory
{
    public static bool TryCreate(
        FullSearchEntryType entryType,
        string? extension,
        double minimumSize,
        FullSearchSizeUnit minimumUnit,
        double maximumSize,
        FullSearchSizeUnit maximumUnit,
        DateOnly? modifiedFrom,
        DateOnly? modifiedTo,
        bool hidden,
        bool system,
        bool readOnly,
        FullSearchSortField sortField,
        FullSearchSortDirection sortDirection,
        out FullSearchCriteria? criteria,
        out string? error)
    {
        if (!TrySize(minimumSize, minimumUnit, out var minimum, out error) ||
            !TrySize(maximumSize, maximumUnit, out var maximum, out error))
        {
            criteria = null;
            return false;
        }

        if (minimum is not null && maximum is not null && minimum > maximum)
        {
            criteria = null;
            error = "Minimum size must not exceed maximum size.";
            return false;
        }

        if (modifiedFrom is not null && modifiedTo is not null && modifiedFrom > modifiedTo)
        {
            criteria = null;
            error = "Modified From must not be later than Modified To.";
            return false;
        }

        var normalizedExtension = string.IsNullOrWhiteSpace(extension) ? null : extension.Trim();
        if (normalizedExtension?.StartsWith(".", StringComparison.Ordinal) == true)
        {
            normalizedExtension = normalizedExtension.TrimStart('.');
        }
        if (normalizedExtension is { Length: 0 } ||
            normalizedExtension?.Contains('.') == true ||
            normalizedExtension?.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '%', '_']) >= 0)
        {
            criteria = null;
            error = "Extension must be one value such as pdf or .pdf.";
            return false;
        }

        criteria = new FullSearchCriteria(
            entryType,
            normalizedExtension,
            minimum,
            maximum,
            modifiedFrom,
            modifiedTo,
            hidden,
            system,
            readOnly,
            sortField,
            sortDirection);
        error = null;
        return true;
    }

    private static bool TrySize(double value, FullSearchSizeUnit unit, out long? bytes, out string? error)
    {
        if (double.IsNaN(value))
        {
            bytes = null;
            error = null;
            return true;
        }
        if (double.IsInfinity(value) || value < 0)
        {
            bytes = null;
            error = "Size must be a non-negative finite number.";
            return false;
        }

        var multiplier = unit switch
        {
            FullSearchSizeUnit.KB => 1024d,
            FullSearchSizeUnit.MB => 1024d * 1024,
            FullSearchSizeUnit.GB => 1024d * 1024 * 1024,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
        var scaled = value * multiplier;
        if (scaled > long.MaxValue)
        {
            bytes = null;
            error = "Size is too large.";
            return false;
        }

        bytes = checked((long)Math.Round(scaled, MidpointRounding.AwayFromZero));
        error = null;
        return true;
    }
}

internal static class FullSearchDateRange
{
    public static (long? FromUtcFileTime, long? ToUtcFileTime) ToUtcFileTimeBounds(
        DateOnly? from,
        DateOnly? to,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var fromUtc = from is null ? (DateTime?)null : StartOfLocalDateUtc(from.Value, timeZone);
        var toUtc = to is null ? (DateTime?)null : StartOfLocalDateUtc(to.Value.AddDays(1), timeZone).AddTicks(-1);
        return (fromUtc?.ToFileTimeUtc(), toUtc?.ToFileTimeUtc());
    }

    private static DateTime StartOfLocalDateUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            var earliestOffset = timeZone.GetAmbiguousTimeOffsets(local).Max();
            return new DateTimeOffset(local, earliestOffset).UtcDateTime;
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }
}

public sealed class FullSearchResultItem
{
    internal SearchResult Result { get; init; } = null!;
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Kind { get; init; }
    public required string Size { get; init; }
    public required string Modified { get; init; }

    internal static FullSearchResultItem Create(SearchResult result, FullSearchResultFields fields) => new()
    {
        Result = result,
        Name = result.Title,
        Path = fields.Path ?? "Context unavailable",
        Kind = fields.IsDirectory ? "Folder" : "File",
        Size = fields.IsDirectory ? string.Empty : FormatSize(fields.Size),
        Modified = FormatModified(fields.ModifiedUtcFileTime)
    };

    private static string FormatSize(long? bytes)
    {
        if (bytes is null) return "—";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes.Value / 1024d).ToString("0.#", CultureInfo.CurrentCulture)} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{(bytes.Value / (1024d * 1024)).ToString("0.#", CultureInfo.CurrentCulture)} MB";
        return $"{(bytes.Value / (1024d * 1024 * 1024)).ToString("0.#", CultureInfo.CurrentCulture)} GB";
    }

    private static string FormatModified(long? fileTime)
    {
        if (fileTime is null) return "—";
        try
        {
            return DateTime.FromFileTimeUtc(fileTime.Value).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "—";
        }
    }
}
