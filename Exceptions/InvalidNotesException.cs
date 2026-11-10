namespace MovieTracker.Api.Exceptions;

public class InvalidNotesException : Exception
{
    public string ErrorMessage { get; }

    public InvalidNotesException(string message): base(message)
    {
        ErrorMessage = message;
    }
}
