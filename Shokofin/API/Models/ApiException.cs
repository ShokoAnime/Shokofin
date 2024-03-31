
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;

#nullable enable
namespace Shokofin.API.Models;

[Serializable]
public class ApiException : Exception
{

    private record ValidationResponse
    {
        public Dictionary<string, string[]> errors = new();

        public string title = string.Empty;

        public HttpStatusCode status = HttpStatusCode.BadRequest;
    }

    public readonly HttpStatusCode StatusCode;

    public readonly ApiExceptionType  Type;

    public readonly RemoteApiException? Inner;

    public readonly Dictionary<string, string[]> ValidationErrors;

    public ApiException(HttpStatusCode statusCode, string source, string? message) : base(string.IsNullOrEmpty(message) ? source : $"{source}: {message}")
    {
        StatusCode = statusCode;
        Type = ApiExceptionType.Simple;
        ValidationErrors = new();
    }

    protected ApiException(HttpStatusCode statusCode, RemoteApiException inner) : base(inner.Message, inner)
    {
        StatusCode = statusCode;
        Type = ApiExceptionType.RemoteException;
        Inner = inner;
        ValidationErrors = new();
    }

    protected ApiException(HttpStatusCode statusCode, string source, string? message, Dictionary<string, string[]>? validationErrors = null): base(string.IsNullOrEmpty(message) ? source : $"{source}: {message}")
    {
        StatusCode = statusCode;
        Type = ApiExceptionType.ValidationErrors;
        ValidationErrors = validationErrors ?? new();
    }

    public static ApiException FromResponse(HttpResponseMessage response)
    {
        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (text.Length > 0 && text[0] == '{') {
            var full = JsonSerializer.Deserialize<ValidationResponse>(text);
            var title = full?.title;
            var validationErrors = full?.errors;
            return new ApiException(response.StatusCode, "ValidationError", title, validationErrors);
        }
        var index = text.IndexOf("HEADERS");
        if (index != -1)
        {
            var (firstLine, lines) = text.Substring(0, index).TrimEnd().Split('\n');
            var (name, splitMessage) = firstLine?.Split(':') ?? new string[] {};
            var message = string.Join(':', splitMessage).Trim();
            var stackTrace = string.Join('\n', lines);
            return new ApiException(response.StatusCode, new RemoteApiException(name ?? "InternalServerException", message, stackTrace));
        }
        return new ApiException(response.StatusCode, response.StatusCode.ToString() + "Exception", text.Split('\n').FirstOrDefault() ?? string.Empty);
    }

    public class RemoteApiException : Exception
    {
        public RemoteApiException(string source, string message, string stack) : base($"{source}: {message}")
        {
            Source = source;
            StackTrace = stack;
        }

        /// <inheritdoc/>
        public override string StackTrace { get; }
    }

    public enum ApiExceptionType
    {
        Simple = 0,
        ValidationErrors = 1,
        RemoteException = 2,
    }
}

public static class IListExtension {
    public static void Deconstruct<T>(this IList<T> list, out T? first, out IList<T> rest) {
        first = list.Count > 0 ? list[0] : default(T); // or throw
        rest = list.Skip(1).ToList();
    }
}
