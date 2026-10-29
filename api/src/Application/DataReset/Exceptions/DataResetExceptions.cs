namespace Application.DataReset.Exceptions;

public abstract class DataResetException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public class DataResetUnknownException(Exception innerException)
    : DataResetException("An unknown error occurred while resetting the database to seed data.", innerException);

public class MfaDataClearUnknownException(Exception innerException)
    : DataResetException("An unknown error occurred while clearing MFA data.", innerException);