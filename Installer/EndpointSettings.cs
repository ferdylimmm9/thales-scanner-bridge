namespace ThalesBridgeInstaller;

internal static class EndpointSettings
{
    public static bool TryGetLocalPort(string value, out int port)
    {
        port = 0;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != "wss" || !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || uri.UserInfo.Length != 0 || uri.Port is < 1 or > 65535)
            return false;
        port = uri.Port;
        return true;
    }
}
