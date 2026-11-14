namespace Portal.Bff;

/// <summary>
/// CSRF-Schutz für die Cookie-Session: Schreibende Aufrufe brauchen den Header <c>X-CSRF: 1</c>.
/// Ein Formular einer fremden Seite kann keine eigenen Header setzen, und ein <c>fetch</c> mit eigenem Header löst
/// einen CORS-Preflight aus, den das BFF nicht erlaubt. Zusammen mit SameSite=Lax reicht das ohne Token-Abgleich.
/// </summary>
public static class Csrf
{
    public static bool IsAllowed(HttpRequest request) =>
        HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method)
        || request.Headers["X-CSRF"] == "1";
}
