using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ElderSafe.Api.Data;

/// <summary>
/// Stores every <see cref="DateTimeOffset"/> as UTC ticks, and reads it back
/// as a UTC <see cref="DateTimeOffset"/>.
/// </summary>
/// <remarks>
/// <para>
/// SQLite has no offset-aware timestamp type. EF Core's SQLite provider
/// refuses to translate a <c>DateTimeOffset</c> in an <c>ORDER BY</c> or a
/// comparison at all, throwing <see cref="NotSupportedException"/>, so storing
/// one directly would leave the application unable to ask for a resident's
/// most recent readings or their alerts in order — which is most of what it
/// does.
/// </para>
/// <para>
/// Converting to a 64-bit integer solves both problems at once. UTC ticks are
/// a single instant with no offset to disagree about, so two rows describing
/// the same moment are the same number whatever offset they were written with,
/// and SQLite compares and orders integers natively.
/// </para>
/// <para>
/// The trade is readability: a timestamp column holds a large number rather
/// than a date, so inspecting the database by hand needs a conversion. That is
/// worth it here, because ordering and comparison are correctness properties
/// and legibility in a SQLite browser is a convenience.
/// </para>
/// <para>
/// Changing database would not have avoided this. MySQL has no offset-aware
/// type either; only PostgreSQL's <c>timestamptz</c> and SQL Server's
/// <c>datetimeoffset</c> do. Normalising at the persistence boundary is the
/// portable answer, and it keeps ADR-001 intact.
/// </para>
/// </remarks>
public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, long>
{
    public UtcDateTimeOffsetConverter()
        : base(
            value => value.ToUniversalTime().UtcTicks,
            ticks => new DateTimeOffset(ticks, TimeSpan.Zero))
    {
    }
}
