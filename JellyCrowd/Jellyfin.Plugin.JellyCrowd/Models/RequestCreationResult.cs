namespace Jellyfin.Plugin.JellyCrowd.Models;

/// <summary>
/// How an attempt to create a request ended.
/// </summary>
public enum RequestCreationOutcome
{
  /// <summary>The request was recorded (approved, or pending approval or quota).</summary>
  Created,

  /// <summary>The payload was invalid.</summary>
  Invalid,

  /// <summary>Not allowed: requests disabled, media type or granularity off, or above a parental restriction.</summary>
  Forbidden,

  /// <summary>The user already has the title, or it is already on its way.</summary>
  AlreadyCovered,

  /// <summary>The request is larger than the user's whole disk quota.</summary>
  TooLarge,

  /// <summary>The user reached their request limit for the period.</summary>
  RateLimited,

  /// <summary>Something the decision depends on could not be checked right now.</summary>
  Unavailable,
}

/// <summary>
/// The result of an attempt to create a request: the outcome, a message for a refusal, and the request
/// when one was recorded.
/// </summary>
public sealed class RequestCreationResult
{
  /// <summary>
  /// Gets the outcome.
  /// </summary>
  public RequestCreationOutcome Outcome { get; init; }

  /// <summary>
  /// Gets the reason of a refusal (empty when created).
  /// </summary>
  public string Message { get; init; } = string.Empty;

  /// <summary>
  /// Gets the recorded request, when <see cref="Outcome"/> is <see cref="RequestCreationOutcome.Created"/>.
  /// </summary>
  public RequestRecord? Request { get; init; }

  /// <summary>
  /// A recorded request.
  /// </summary>
  /// <param name="request">The request.</param>
  /// <returns>The result.</returns>
  public static RequestCreationResult Created(RequestRecord request) => new() { Outcome = RequestCreationOutcome.Created, Request = request };

  /// <summary>
  /// A refusal.
  /// </summary>
  /// <param name="outcome">Why it was refused.</param>
  /// <param name="message">The reason, for the caller.</param>
  /// <returns>The result.</returns>
  public static RequestCreationResult Refused(RequestCreationOutcome outcome, string message) => new() { Outcome = outcome, Message = message };
}
