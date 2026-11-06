import * as signalR from "@microsoft/signalr";

/**
 * Opens the live connection to the alerts hub.
 *
 * No token is passed. The session is an HttpOnly cookie on the same origin,
 * so the browser attaches it to the WebSocket handshake itself — which is why
 * cookie authentication was chosen over bearer tokens (ADR-006).
 *
 * Reconnection matters more here than in most apps: a connection that drops
 * silently is a caregiver who stops being told about falls. Callers should
 * refetch open alerts after a reconnect rather than assume nothing was missed
 * while the socket was down (RISK-08).
 */
export function createAlertsConnection(): signalR.HubConnection {
  return new signalR.HubConnectionBuilder()
    .withUrl("/hubs/alerts")
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();
}
