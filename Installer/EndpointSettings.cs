namespace ThalesBridgeInstaller;

internal static class EndpointSettings
{
    public static bool TryGetLocalPort(string value, out int port)
        => TryGetLocalEndpoint(value, out port, out _);

    public static bool TryGetLocalEndpoint(string value, out int port, out string scheme)
    {
        scheme = "wss";
        port = 0;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("wss" or "ws") || !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || uri.UserInfo.Length != 0 || uri.Port is < 1 or > 65535)
            return false;
        scheme = uri.Scheme;
        port = uri.Port;
        return true;
    }
}
