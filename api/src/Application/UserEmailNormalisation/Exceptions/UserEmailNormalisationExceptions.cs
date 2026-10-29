namespace Application.UserEmailNormalisation.Exceptions;

public abstract class UserEmailNormalisationException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public class UserEmailNormalisationUnknownException(Exception innerException)
    : UserEmailNormalisationException("An unknown error occurred while normalising user emails.", innerException);