namespace MovieTracker.Api.Exceptions;

public class InvalidPageSizeException : Exception
{
    public string ErrorMessage { get; }
    
    public InvalidPageSizeException(string message): base(message)
    {
        ErrorMessage = message;
    }
}