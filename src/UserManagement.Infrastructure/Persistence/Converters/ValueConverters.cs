using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace UserManagement.Infrastructure.Persistence.Converters;

/// <summary>
/// SQLite stores no dedicated Guid/DateTime types. These converters keep the on-disk format
/// stable, sortable and culture-invariant so queries and migrations behave predictably.
/// </summary>
public class GuidToStringConverter : ValueConverter<Guid, string>
{
    public GuidToStringConverter()
        : base(v => v.ToString("D"), v => Guid.Parse(v)) { }
}

public class NullableGuidToStringConverter : ValueConverter<Guid?, string?>
{
    public NullableGuidToStringConverter()
        : base(v => v.HasValue ? v.Value.ToString("D") : null,
               v => string.IsNullOrEmpty(v) ? null : Guid.Parse(v)) { }
}

public class UtcDateTimeConverter : ValueConverter<DateTime, string>
{
    internal const string Format = "yyyy-MM-dd HH:mm:ss.fffffff";

    public UtcDateTimeConverter()
        : base(v => ToUtc(v).ToString(Format, CultureInfo.InvariantCulture),
               v => DateTime.SpecifyKind(DateTime.ParseExact(v, Format, CultureInfo.InvariantCulture), DateTimeKind.Utc)) { }

    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

public class NullableUtcDateTimeConverter : ValueConverter<DateTime?, string?>
{
    public NullableUtcDateTimeConverter()
        : base(v => v.HasValue
                   ? UtcDateTimeConverter.ToUtc(v.Value).ToString(UtcDateTimeConverter.Format, CultureInfo.InvariantCulture)
                   : null,
               v => string.IsNullOrEmpty(v)
                   ? null
                   : DateTime.SpecifyKind(
                       DateTime.ParseExact(v, UtcDateTimeConverter.Format, CultureInfo.InvariantCulture),
                       DateTimeKind.Utc)) { }
}
