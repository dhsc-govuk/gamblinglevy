using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Infrastructure.Persistence.Converters;
public class DateOnlyConverter() : ValueConverter<DateOnly, DateTime>(
    // Store as UTC midnight: the columns are 'timestamp with time zone', and Npgsql rejects a
    // DateTime with Kind=Unspecified (the default from DateOnly.ToDateTime) for a timestamptz column.
    dateOnly => DateTime.SpecifyKind(dateOnly.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
    dateTime => DateOnly.FromDateTime(dateTime));