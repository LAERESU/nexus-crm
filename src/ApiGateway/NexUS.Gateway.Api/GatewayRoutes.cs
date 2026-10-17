namespace NexUS.Gateway.Api;

public static class GatewayRoutes
{
    /// <summary>
    /// Path milik komunikasi antar service yang diblokir di Gateway (selalu 404). Tulis segmen resource secara
    /// literal: pola dengan parameter di posisi resource kalah prioritas dari route YARP dan tidak akan memblokir.
    /// </summary>
    public static readonly string[] InternalPaths =
    [
        "/api/v1/users/{id}/permissions",
    ];
}
