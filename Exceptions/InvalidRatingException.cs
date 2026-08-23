namespace MovieTracker.Api.Exceptions;

public class InvalidRatingException : Exception
{
    public string ErrorMessage { get; }
    
    public InvalidRatingException(string message): base(message)
    {
        ErrorMessage = message;
    }

}